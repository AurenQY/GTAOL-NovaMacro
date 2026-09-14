# Current V1 architecture

## Design principles

- Keep macro execution independent from WPF and Win32 lifecycle code.
- Keep native calls and Windows message/hook ownership out of the UI layer.
- Use narrow internal seams only for real execution or deterministic-test
  boundaries.
- Compose the application manually; V1 does not need a dependency-injection or
  MVVM framework.
- Treat editor drafts, saved settings, and the active startup runtime as three
  separate states.
- Keep fixed-sequence automation simple until a new product requirement proves
  that another abstraction is needed.

## Project boundaries

### `NovaMacro.Core`

Core owns the platform-independent macro model and execution rules:

- `MacroSequence` and `MacroStep`;
- `KeyTapStep`, `DelayStep`, and `MouseButtonTapStep`;
- keyboard and mouse output vocabularies;
- `IKeyboardOutput`, `IMouseOutput`, and `IDelay` boundaries;
- `MacroRunner`, including ordered execution, cancellation cleanup, and
  per-runner non-reentrancy.

`BuiltInMacroSequences.AbcTest` remains a technical test helper, not a current
factory product macro. Core does not reference WPF or Win32.

### `NovaMacro.Windows`

Windows adapts Core output and global triggers to Win32:

- `WindowsKeyboardOutput` and `WindowsMouseOutput` use `SendInput`;
- `WindowsGlobalHotkeyService` owns `RegisterHotKey`, `WM_HOTKEY`, physical
  release gating, and its message thread;
- `WindowsGlobalKeyboardFallbackService` owns the shared `WH_KEYBOARD_LL`
  observation fallback used only for `RegisterHotKey` collision error 1409;
- `WindowsGlobalMouseButtonService` owns the low-level XButton hook and
  Mouse4/Mouse5 press/release events;
- registered XButtons are swallowed globally, while unregistered XButtons are
  forwarded;
- narrow internal native/scheduler seams support deterministic tests.

Windows references Core but not App or WPF. These seams are test boundaries,
not plugins or a general platform framework.

### `NovaMacro.App`

App is the WPF host and composition root. It owns:

- `SingleInstanceGuard`;
- the canonical zero-to-32 macro configuration domain;
- validation, JSON V1/V2/V3 compatibility, and the portable settings store;
- `StartupConfigurationCoordinator` and status presentation;
- `MainWindow`, the multi-macro editor draft/control, and save coordination;
- `MultiMacroHotkeyCoordinator`, registration ownership, global execution
  admission, and held-mouse iteration policy;
- construction and shutdown of Windows services and the shared runner.

App contains no P/Invoke and does not reimplement input execution.

### `NovaMacro.Tests`

Tests reference all three production projects. Controlled native APIs,
schedulers, delays, and outputs make concurrency and lifecycle tests
deterministic. Real-Windows and human checks cover the boundaries that fakes
cannot prove, including actual input, global registration, GTA behavior, and
the final WPF layout.

## Dependency direction

```text
NovaMacro.App ───────► NovaMacro.Windows ───────► NovaMacro.Core
      └────────────────────────────────────────► NovaMacro.Core

NovaMacro.Tests ─────► NovaMacro.App / NovaMacro.Windows / NovaMacro.Core
```

Core never depends on Windows, App, or Tests. Windows never depends on App or
WPF.

## Canonical configuration

```text
ApplicationSettings
└─ Macros (0..32)
   └─ MacroDefinition
      ├─ MacroId
      ├─ Name
      ├─ Enabled
      ├─ MacroTrigger
      ├─ MacroExecutionMode
      └─ MacroSequence
```

`MacroTrigger` separates keyboard hotkeys from Mouse4/Mouse5 buttons. Trigger
keys and output keys deliberately use different vocabularies. Macro identity is
a stable `MacroId`; names are editable labels, not identifiers.

## Startup decision flow

```text
App.OnStartup
    │
    ▼
SingleInstanceGuard
    │ primary instance only
    ▼
JsonSettingsStore.Load (once)
    │
    ▼
StartupConfigurationCoordinator
    ├─ Missing ─────► four GTA factory macros in editor + runtime
    ├─ Loaded ──────► saved editor + same saved runtime
    ├─ Invalid ─────► empty recovery editor + no runtime
    └─ Unavailable ─► empty recovery editor + no runtime
```

Startup never saves. Missing settings remain missing until an explicit Save.
Existing V1, V2, V3, and valid empty settings are authoritative and never
receive factory-macro injection. Invalid or unavailable data is not silently
overwritten.

The factory macros are ordinary `MacroDefinition` objects with fresh,
non-legacy IDs. They have no persisted origin marker, lock, badge, or automatic
restore behavior.

## Registration and runtime flow

```text
immutable startup settings
    │ enabled macros
    ▼
MultiMacroHotkeyCoordinator
    ├─ keyboard trigger ─► WindowsGlobalHotkeyService
    │                       └─ error 1409 only
    │                          ─► WindowsGlobalKeyboardFallbackService
    └─ Mouse4/Mouse5 ────► WindowsGlobalMouseButtonService
                              │
                              ▼
                    synchronous global admission
                              │ accepted only if idle
                              ▼
                         MacroRunner
                              │
                              ▼
          WindowsKeyboardOutput / WindowsMouseOutput
                              │
                              ▼
                  current foreground application
```

One coordinator-level owner covers keyboard `Once`, mouse `Once`, and mouse
`RepeatWhileHeld` work. A trigger received while busy is dropped synchronously:
no worker is queued and no delayed retry occurs.

For `RepeatWhileHeld`, an accepted press always receives one complete
iteration. A continuing hold starts further iterations serially. Release is
soft: it finishes the current iteration and prevents the next one. The global
owner remains held between iterations.

## Factory product configuration

Only genuinely missing settings create these enabled macros, in order:

1. Buy All Ammo — `Alt+R`, `Once`.
2. Drop Bull Shark — `Alt+E`, `Once`.
3. Request Sparrow — `Ctrl+Alt+H`, `Once`.
4. Heavy Weapon Rapid Fire — `Mouse4`, `RepeatWhileHeld`.

The catalog is internal factory-data infrastructure. The main UI does not
expose a preset selector or Add-from-preset workflow.

## Persistence and save flow

`JsonSettingsStore` uses
`AppContext.BaseDirectory/data/settings.json`. It reads V1, V2, and V3; current
saves write V3. Older versions migrate in memory and are not rewritten merely
because they were loaded.

```text
MainWindow draft
    ▼ validate
MacroEditorSaveCoordinator
    ▼
JsonSettingsStore.Save (V3)
```

Save replaces the file but never reconfigures the running coordinator. If
startup loaded A, saving B leaves runtime A active until the next launch.

## Failure and shutdown

Registration failures are reported per macro. Partial and zero-registration
states keep the editor usable, while terminal service failures preserve whether
the failure occurred during initial startup or after a previously active
runtime.

Shutdown closes trigger admission, cancels owned work, disposes all active global
trigger services, waits for tracked macro tasks, and releases the
single-instance guard late in application shutdown.

## Windows behavior and limitations

`SendInput` targets whichever application is foreground for each event.
NovaMacro neither chooses nor validates a GTA window.

Keyboard trigger policy allows F1-F11 and supported numpad keys without
modifiers; A-Z requires at least one of Control, Alt, or Shift. NumpadEnter is
an output key but is not exposed as a distinct trigger. Mouse4/Mouse5 triggers
are global and intentionally have no GTA foreground filter in V1.

Keyboard hotkeys first use Windows `RegisterHotKey`. Only an
`ERROR_HOTKEY_ALREADY_REGISTERED` (1409) collision activates the shared
low-level keyboard observation fallback. The fallback observes and forwards
input rather than taking exclusive ownership, so the application that already
owns the shortcut may also react. It does not bypass Windows security or add a
GTA foreground filter.
