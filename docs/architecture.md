# Current V0 architecture

## Design principles

- Keep macro execution independent from WPF and Windows message handling.
- Keep Win32 P/Invoke declarations and native lifecycle logic out of the UI
  project.
- Use small interfaces and internal seams only where they create a real
  execution, ownership, or deterministic-testing boundary.
- Compose the application manually; V0 does not need a dependency-injection
  container or MVVM framework.
- Do not introduce a generic scripting, plugin, or preset framework before the
  product requires one.

## Project boundaries

### `NovaMacro.Core`

Core owns the platform-independent macro model and execution rules:

- `MacroSequence`, `MacroStep`, `KeyTapStep`, and `DelayStep`;
- `BuiltInMacroSequences.AbcTest`;
- `MacroRunner` and its per-instance non-reentrancy rule;
- `IKeyboardOutput` and `IDelay` execution boundaries.

The current macro-output vocabulary is intentionally limited to `A`, `B`, and
`C`. Core does not reference WPF and does not call Win32 APIs.

### `NovaMacro.Windows`

Windows adapts Core execution and keyboard triggers to Win32:

- `WindowsKeyboardOutput` translates Core A/B/C output to `SendInput`;
- `WindowsGlobalHotkeyService` owns `RegisterHotKey`, `WM_HOTKEY`, registration
  lifecycle, and its background message thread;
- physical trigger/modifier release is checked through `GetAsyncKeyState`;
- `KeyboardHotkey`, `HotkeyKey`, modifiers, and virtual-key mapping define the
  current keyboard-trigger vocabulary;
- narrow internal native and trigger-work scheduling seams support
  deterministic tests.

Those internal seams are test boundaries, not a public platform abstraction,
plugin system, or dependency-injection framework. Windows references Core but
does not reference WPF.

### `NovaMacro.App`

App is the WPF host and composition root. It owns:

- construction of the real Windows output, delay, runner, hotkey service, and
  coordinator;
- `MacroHotkeyCoordinator`, which bridges synchronous hotkey events to tracked
  asynchronous macro execution;
- `TaskDelay`, the production `IDelay` adapter;
- fixed modifierless `F8` registration for the technical slice;
- startup failure handling and shutdown cleanup.

App contains no P/Invoke and does not reimplement macro execution. Its current
window reports technical-test mode; it is not a configuration UI.

### `NovaMacro.Tests`

Tests reference Core, Windows, and App. Coverage includes:

- Core model and runner contracts;
- Windows keyboard-output validation;
- deterministic global-hotkey mapping, lifecycle, concurrency, callback, and
  terminal-failure behavior;
- App coordinator startup, execution, failure, overlap, and shutdown behavior.

Real Windows integration complements these deterministic tests; the internal
fakes do not replace manual `RegisterHotKey` or `SendInput` validation.

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

## Current runtime flow

```text
F8 press/release
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
reused MacroRunner
       │
       ▼
BuiltInMacroSequences.AbcTest
       │
       ▼
WindowsKeyboardOutput → SendInput
       │
       ▼
current foreground application
```

`MOD_NOREPEAT` prevents repeated `WM_HOTKEY` activations while F8 is held. The
service waits until the trigger and configured modifiers are physically
released before publishing `Triggered`. The coordinator then starts the ABC
sequence on its one shared runner. If that runner is already active, it returns
`AlreadyRunning`; the new activation is dropped rather than queued or retried.

`KeyTapStep` performs key-down followed by key-up. If key-down returns
successfully, the runner attempts key-up exactly once; it does not issue key-up
when key-down throws. Delays use `TaskDelay`, so the WPF thread is not blocked
while the macro waits.

## Ownership and lifecycle

On startup, App manually constructs the runtime graph and transfers hotkey
service ownership to `MacroHotkeyCoordinator`. The coordinator subscribes to
the hotkey events and becomes active only after registration succeeds. The main
window is shown only after fixed F8 registration succeeds.

The coordinator tracks every macro task it admits. During shutdown it:

1. closes coordinator admission and unsubscribes from hotkey events;
2. cancels the macro lifetime;
3. disposes the hotkey service, which unregisters F8 and stops its message
   thread;
4. waits for tracked macro tasks to finish;
5. reports runtime cleanup failures through the current diagnostic path.

Runtime failures are currently written to `Trace`. If another NovaMacro
instance already owns F8, a second instance reports a startup registration
error and exits; there is no single-instance mutex yet.

## Foreground-target policy

NovaMacro does not select, remember, or validate a target window. `SendInput`
feeds the system input stream, so the application in the foreground when a key
event is sent receives that event. Changing the foreground application changes
the effective target.

## Hotkey capability and App policy

The Windows backend can represent A-Z and F1-F11 triggers with no modifier or
any combination of Control, Alt, and Shift. This trigger vocabulary is separate
from the current macro-output vocabulary, which remains A/B/C only.

Deciding which representable bindings are safe to expose is an App policy.
Future V0 configuration UI must reject modifierless A-Z even though the backend
can represent it; fixed modifierless F8 remains valid. The detailed allowed
matrix and Windows QA rationale are recorded in the
[V0 hotkey policy](v0-scope.md#v0-hotkey-policy).

Mouse4/Mouse5 triggers are not implemented. A future mouse trigger should add
or replace a Windows trigger source without changing `MacroRunner`.
