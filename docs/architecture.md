# Current V0 architecture

## Design principles

- Keep macro execution independent from WPF and Windows message handling.
- Keep Win32 P/Invoke declarations and native lifecycle logic out of the UI
  project.
- Use small interfaces and internal seams only where they create a real
  execution, ownership, persistence, or deterministic-testing boundary.
- Compose the application manually; V0 does not need a dependency-injection
  container or MVVM framework.
- Treat the editor draft, saved settings, and active runtime as separate states.
- Do not introduce a generic scripting, plugin, or preset framework before the
  product requires one.

## Project boundaries

### `NovaMacro.Core`

Core owns the platform-independent macro model and execution rules:

- `MacroSequence`, `MacroStep`, `KeyTapStep`, and `DelayStep`;
- `BuiltInMacroSequences.AbcTest` for the starter configuration;
- `MacroRunner` and its per-instance non-reentrancy rule;
- `IKeyboardOutput` and `IDelay` execution boundaries.

The current macro-output vocabulary is A-Z, Digit0-Digit9, the four arrow keys,
Enter, Escape, Space, Tab, and Backspace. Core does not reference WPF and does
not call Win32 APIs.

### `NovaMacro.Windows`

Windows adapts Core output and keyboard triggers to Win32:

- `WindowsKeyboardOutput` translates supported Core keys to `SendInput`;
- `WindowsGlobalHotkeyService` owns `RegisterHotKey`, `WM_HOTKEY`,
  registration lifecycle, and its background message thread;
- physical trigger/modifier release is checked through `GetAsyncKeyState`;
- `KeyboardHotkey`, `HotkeyKey`, modifiers, and virtual-key mapping define
  the keyboard-trigger vocabulary;
- narrow internal native and trigger-work seams support deterministic tests.

The trigger-key model is deliberately separate from Core's output-key model.
Windows references Core but does not reference WPF or App. Its internal seams
are test boundaries, not a plugin system or general platform abstraction.

### `NovaMacro.App`

App is the WPF host and composition root. It owns:

- `SingleInstanceGuard`, which admits one NovaMacro process per current Windows
  session;
- the one-macro configuration model, validation policy, JSON codec, and portable
  settings store;
- `StartupConfigurationCoordinator`, which loads once and chooses editor and
  runtime settings without saving;
- `MainWindow`, `MacroEditorDraft`, and `MacroEditorSaveCoordinator`;
- construction of the Windows output, delay, runner, hotkey service, and
  `MacroHotkeyCoordinator` when runtime activation is appropriate;
- startup/runtime status, activation-failure recovery, and shutdown cleanup.

App contains no P/Invoke and does not reimplement macro execution.

### `NovaMacro.Tests`

Tests reference Core, Windows, and App. Coverage includes:

- Core model and runner contracts;
- final Windows keyboard packets and hotkey lifecycle/concurrency behavior;
- configuration validation, JSON serialization, and portable storage;
- editor draft/save coordination and single-instance ownership;
- startup four-state resolution, same-snapshot mapping, and zero automatic Save;
- App hotkey-to-macro coordination.

Controlled fakes make boundary behavior deterministic. Real Windows validation
still covers the actual `RegisterHotKey`, `SendInput`, process, restart, and
foreground-window paths.

## Dependency direction

```text
NovaMacro.App ───────► NovaMacro.Windows ───────► NovaMacro.Core
      └────────────────────────────────────────► NovaMacro.Core

NovaMacro.Tests ─────► NovaMacro.App
NovaMacro.Tests ─────► NovaMacro.Windows
NovaMacro.Tests ─────► NovaMacro.Core
```

Core never depends on Windows, App, or Tests. Windows never depends on WPF or
App.

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
    │
    ├─ Missing ─────► starter editor + starter runtime
    ├─ Loaded ──────► saved editor + same saved runtime
    ├─ Invalid ─────► starter recovery editor + no runtime
    └─ Unavailable ─► starter recovery editor + no runtime
```

Startup never saves settings. Missing does not create a file. Invalid or
unavailable settings are not replaced automatically, and neither state silently
activates the starter runtime.

For a ready state, App passes the same immutable `ApplicationSettings` snapshot
to the runtime hotkey and sequence selection. This prevents mixed combinations
such as a loaded hotkey with the starter sequence.

## Active runtime flow

```text
configured hotkey press/release
       │
       ▼
WindowsGlobalHotkeyService
(RegisterHotKey, WM_HOTKEY, MOD_NOREPEAT)
       │
       ▼
physical trigger/modifier release gate
(GetAsyncKeyState)
       │
       ▼
MacroHotkeyCoordinator
       │
       ▼
one reused MacroRunner
       │
       ▼
startupSettings.Macro.Sequence
       │
       ▼
WindowsKeyboardOutput → SendInput
       │
       ▼
current foreground application
```

The service waits until the configured trigger and modifiers are physically
released before publishing `Triggered`. If the one runner is already active,
`MacroRunner` returns `AlreadyRunning`; the new activation is dropped rather
than queued or retried.

`KeyTapStep` performs key-down followed by key-up. If key-down succeeds, the
runner attempts key-up exactly once. Delays use `TaskDelay`, so the WPF thread
is not blocked while the macro waits.

## Editor save flow and runtime snapshot

```text
MainWindow
    ▼
MacroEditorDraft
    ▼
ConfigurationValidator
    ▼
MacroEditorSaveCoordinator
    ▼
JsonSettingsStore.Save
```

The startup resolver and save coordinator share one store instance so they use
the same canonical path. Save validates and replaces the versioned JSON file,
but it does not access or replace the active coordinator. If runtime A was
loaded at startup, editing and saving B leaves runtime A active; B is loaded on
the next launch. There is no live apply or automatic restart.

## Ownership, failure, and shutdown

Single-instance ownership is acquired before the settings store is constructed
or loaded. A rejected secondary instance therefore does not become a settings
or runtime owner.

When runtime settings are available, App manually constructs the runtime graph
and transfers hotkey-service ownership to `MacroHotkeyCoordinator`. If hotkey
registration or runtime activation fails, the temporary coordinator/service is
disposed, the editor still opens with the attempted configuration, and runtime
status is inactive. NovaMacro does not choose a fallback key or starter F8.

During normal shutdown the coordinator closes admission, cancels its macro
lifetime, disposes the hotkey service, and waits for tracked macro tasks. The
single-instance guard remains held until late application shutdown. Recovery
mode has no coordinator, so shutdown is null-safe.

## Foreground-target policy

NovaMacro does not select, remember, or validate a target window. `SendInput`
feeds the Windows input stream, so whichever application is in the foreground
when an event is sent is the effective target.

## Hotkey capability and App policy

The Windows backend can represent A-Z and F1-F11 triggers with no modifier or
any combination of Control, Alt, and Shift. The App/configuration layer exposes
that vocabulary but rejects modifierless A-Z. Modifierless F1-F11 and modified
A-Z/F1-F11 combinations are allowed; F12 is unsupported.

The letter restriction is an App safety policy, not a Windows model defect.
Registering a modifierless letter can suppress ordinary foreground typing, and
matching generated output can create self-trigger risk. Mouse4/Mouse5 triggers
remain unimplemented; a future mouse trigger source should not require changing
`MacroRunner`.
