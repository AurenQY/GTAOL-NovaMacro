# V0 scope

## Product goal

The V0 product goal is the smallest complete application in which a user can
configure one global keyboard trigger, focus another window, activate the
trigger, and reliably execute one predefined keyboard sequence.

The repository has not reached that configurable product state yet. It now has
the required execution and Windows infrastructure plus a fixed-F8 technical
slice that proves the complete runtime path.

## Approved product decisions

- Use C#, .NET 10, and WPF.
- Keep V0 triggers keyboard-only.
- Do not assume a keyboard trigger always has Ctrl, Alt, or Shift.
- Send generated input to the current foreground application.
- Keep future settings portable-first under `data/settings.json` beside the
  application.
- Keep trigger registration separate from macro execution so future trigger
  mechanisms do not require a new runner.
- Do not add computer vision, OCR, AI, a recorder, or a large preset library to
  V0.

## Implemented V0 infrastructure

The repository currently implements:

- an immutable macro model with key-tap and delay steps;
- the deterministic built-in ABC sequence;
- a sequential, cancellation-aware, non-reentrant `MacroRunner`;
- narrow keyboard-output and delay interfaces;
- real A/B/C keyboard output through Windows `SendInput`;
- global keyboard registration through `RegisterHotKey` and `WM_HOTKEY`;
- modifierless and Ctrl/Alt/Shift keyboard-hotkey representation;
- physical trigger/modifier release gating and `MOD_NOREPEAT`;
- registration, replacement, failure, concurrency, and shutdown lifecycle;
- internal native and trigger-work seams for deterministic Windows tests;
- WPF composition, coordinator ownership, and tracked asynchronous execution.

## Current APP-01 technical slice

The current application uses fixed modifierless `F8`; it is not configurable
through the UI.

Current user-visible behavior is:

1. Launch NovaMacro.
2. NovaMacro registers F8 and displays a technical-test window.
3. Focus another foreground application.
4. Press and release F8.
5. NovaMacro executes the built-in ABC sequence.
6. The current foreground application receives the A/B/C key taps.

The trigger is published only after F8 is physically released. Holding F8 does
not repeat the macro. A second activation while the runner is active is dropped
without overlap, queueing, or retry. Normal shutdown unregisters F8, and a
restart can acquire it again.

There is no single-instance mutex. If one NovaMacro instance owns F8, a second
instance currently reports a registration error and exits while the first
instance remains functional.

## Deterministic ABC macro

The built-in sequence is intentionally unrelated to GTA:

1. Tap `A`.
2. Wait 150 ms.
3. Tap `B`.
4. Wait 150 ms.
5. Tap `C`.

Here, "tap" means one key-down followed by one key-up with no intentional hold
duration. The delays occur after the preceding tap completes. The sequence and
the full F8-to-ABC path have been validated with Notepad and other real Windows
applications.

## V0 hotkey policy

### Backend capability

The current `KeyboardHotkey` backend can represent:

- `A` through `Z`;
- `F1` through `F11`;
- no modifier;
- Control, Alt, Shift, or any combination of those modifiers.

The backend therefore can represent modifierless A-Z. It must not be documented
as incapable of doing so.

### Future App/configuration UI

The future V0 UI may expose:

- modifierless F1-F11;
- one or more of Control, Alt, and Shift with A-Z;
- one or more of Control, Alt, and Shift with F1-F11.

It must not expose modifierless A-Z. Real Windows validation showed that
`RegisterHotKey` consumes registered modifierless letters from normal
foreground input, while a matching letter generated through `SendInput` can
activate the registration. The combination creates foreground-input
suppression and possible self-trigger or recursive behavior.

This restriction belongs to App/configuration validation for the current
`RegisterHotKey` backend. It does not require narrowing the backend model.

## Planned V0 product work

The remaining configurable product layer is not implemented. Planned work
includes:

- a configurable hotkey UI with the policy above;
- minimal validation, status, and error UX;
- JSON settings persistence using the portable-first policy.

The approved future settings path is
`AppContext.BaseDirectory/data/settings.json`. It must not depend on the process
working directory. This assumes the application is run from a user-writable
folder; installer behavior and migration to `%LocalAppData%` remain deferred.

## Not implemented in the current V0 slice

- user-defined macro editor;
- macro preset management or a GTA-specific preset library;
- multiple simultaneous hotkeys or macros;
- mouse triggers, including Mouse4 and Mouse5;
- mouse output steps;
- input recorder;
- state-aware automation or game-state recovery;
- screenshots, computer vision, OCR, or AI features;
- plugin system;
- single-instance mutex;
- tray integration, release packaging, or installer;
- automatic elevation.
