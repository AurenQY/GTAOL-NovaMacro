# V0 scope

## Product goal

Build the smallest complete vertical slice in which a user configures one
global keyboard trigger, focuses a target window, presses the trigger, and the
application reliably executes one predefined keyboard sequence.

## Approved founder decisions

- Use C#, .NET 10, and WPF.
- V0 triggers are keyboard-only.
- A keyboard trigger may contain zero or more modifier keys. Shared models and
  APIs must not require Ctrl, Alt, or Shift.
- Future versions may add single-key and mouse-button triggers such as Mouse4
  and Mouse5. V0 must not implement mouse triggers, but trigger registration
  must remain separate from macro execution so this can be added later.
- Send generated input to the current foreground window.
- Store portable settings relative to the executable at
  `data/settings.json`.
- Do not add computer vision, OCR, AI, a macro recorder, or a preset library.

## V0.0 deterministic test macro

The first sequence is intentionally unrelated to GTA:

1. Tap `A`.
2. Wait 150 ms.
3. Tap `B`.
4. Wait 150 ms.
5. Tap `C`.

For this definition, "tap" means one key-down event followed by one key-up
event, with no intentional hold duration. The 150 ms delays occur after the
preceding tap is complete. Notepad is the first manual validation target so
the automation engine can be tested without GTA-specific variables.

## Settings policy

The future settings store will resolve its path from `AppContext.BaseDirectory`
and create `data/settings.json` beside the built or published application. It
must not depend on the process working directory.

This portable-first policy assumes the application is run from a user-writable
folder. Installer behavior and migration to `%LocalAppData%` are deferred.

Only user-controlled settings belong in this file. The V0.0 test macro remains
defined in code and is not a configurable preset.

## Explicitly out of scope

- GTA-specific sequences or window detection
- Mouse-button triggers
- Mouse output steps
- Multiple hotkeys or macros
- Macro editing or recording
- Computer vision, OCR, and AI
- Preset libraries
- Tray integration and installers
- Automatic elevation to administrator
