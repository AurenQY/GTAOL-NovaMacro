# V0 scope

## Current product status

NovaMacro V0 is the smallest complete configurable vertical slice: a user can
edit one global keyboard trigger and one ordered keyboard sequence, save it,
restart the application, and run that sequence in the current foreground
window.

It is intentionally a one-macro Windows desktop application. It is not yet a
general macro platform or a GTA-aware automation product.

## Approved product decisions

- Use C#, .NET 10, and WPF.
- Support exactly one configurable macro in V0.
- Keep V0 triggers keyboard-only and allow triggers without modifiers where the
  App safety policy permits them.
- Send generated input to the current foreground application.
- Store settings portably at `data/settings.json` beneath the application base
  directory, with no `%LocalAppData%` fallback.
- Apply saved changes on the next launch, not live.
- Keep trigger registration separate from macro execution.
- Do not add computer vision, OCR, AI, a recorder, or a preset library to V0.

## Implemented V0 capability

The current editor supports:

- editing the macro name;
- selecting one trigger key;
- selecting Ctrl, Alt, Shift, or their combinations;
- adding Key Tap and Delay steps;
- removing and reordering steps;
- validation and Save;
- separate startup/runtime status, validation errors, and save results.

The application also implements versioned JSON persistence, four-state startup
recovery, single-instance ownership, physical hotkey-release gating,
non-reentrant sequence execution, real Windows `SendInput`, and deterministic
boundary tests.

## User flow

```text
Editor
  → Validate
  → Save data/settings.json
  → close and restart
  → Load saved configuration
  → register configured hotkey
  → execute configured MacroSequence
  → SendInput to the current foreground window
```

There is no Apply button, live hotkey replacement, automatic restart, or runtime
mutation from the editor.

## Trigger-key policy

Trigger keys and output keys are different vocabularies.

The current hotkey trigger vocabulary is:

- `A` through `Z`;
- `F1` through `F11`;
- Control, Alt, Shift, or any combination of those modifiers.

Allowed configurations:

- modifierless F1-F11;
- modified A-Z;
- modified F1-F11.

Modifierless A-Z is rejected by App/configuration validation. Although the
Windows backend can represent it, `RegisterHotKey` can suppress ordinary letter
typing and generated matching output can create self-trigger risk. This is an
App safety policy, not a Core or Windows implementation defect. F12 is not
supported.

Physical hotkey capture is not implemented; the user selects the key and
modifiers from the editor controls.

## Macro-output vocabulary

Key Tap steps currently support:

- A-Z;
- Digit0-Digit9;
- ArrowUp, ArrowDown, ArrowLeft, and ArrowRight;
- Enter, Escape, Space, Tab, and Backspace.

Current output does not support F-keys, modifier/chord output, explicit
hold/release scripting, numpad keys, punctuation, or mouse actions.

Delay steps use integer milliseconds within the validated V0 range. Steps run
in their displayed order.

## Settings and restart semantics

Settings use JSON schema version 1. They are stored at
`AppContext.BaseDirectory/data/settings.json`. For the normal repository
Release output this is:

```text
src/NovaMacro.App/bin/Release/net10.0-windows/data/settings.json
```

Example:

```json
{
  "version": 1,
  "macro": {
    "name": "Example",
    "hotkey": {
      "key": "F9",
      "modifiers": ["Control"]
    },
    "steps": [
      { "type": "keyTap", "key": "M" },
      { "type": "delay", "milliseconds": 250 },
      { "type": "keyTap", "key": "Enter" }
    ]
  }
}
```

Save validates the current editor draft and writes the file. It does not change
the active process:

```text
startup loads A  → runtime A
edit and save B  → disk B, runtime still A
restart          → runtime B
```

The editor draft, disk settings, and active startup runtime are therefore three
separate states.

## Startup behavior

### No settings file

- The editor opens with `StarterConfiguration.Default`.
- The same starter configuration becomes the active runtime.
- NovaMacro does not automatically create `settings.json`.

### Valid saved settings

- The saved configuration populates the editor.
- The same immutable configuration snapshot supplies both the registered hotkey
  and the active macro sequence.
- Starter F8 is not separately registered when a different saved key is active.

### Invalid saved settings

- NovaMacro stays open with a starter recovery draft.
- Runtime remains inactive; starter F8 does not run as a fallback.
- Codec, validation, or storage-level invalid diagnostics are displayed.
- The invalid canonical file is not deleted or automatically overwritten.
- The user can repair and Save, then restart to activate the repaired settings.

### Settings unavailable

- NovaMacro stays open with a starter recovery draft.
- Runtime remains inactive with a readable storage error.
- No starter runtime is activated and no automatic write occurs.

### Hotkey registration or runtime activation failure

- NovaMacro stays open and the editor retains the attempted configuration.
- Runtime is inactive and an activation error is displayed.
- NovaMacro does not select another hotkey or fall back to F8.
- The user may edit and Save another hotkey, but must restart to activate it.

## Starter configuration

The starter configuration is:

- Name: `ABC Test`
- Hotkey: modifierless `F8`
- Sequence: tap A, delay 150 ms, tap B, delay 150 ms, tap C

It is an active runtime only when no saved settings file exists. When a saved
file is invalid or unavailable, the starter configuration is only an editable
recovery draft and runtime remains inactive.

The ABC sequence is deterministic test behavior rather than GTA-specific logic.
A key tap means one key-down followed by one key-up; NovaMacro guarantees the
A/B/C key events, not particular text after keyboard layout, Caps Lock, Shift,
foreground state, and target-application behavior are considered.

## Runtime behavior

- One `MacroRunner` instance is reused for the active macro.
- A retrigger while the sequence is running returns `AlreadyRunning` and is
  dropped; there is no queue or retry.
- Trigger publication waits for physical release of the key and modifiers.
- `SendInput` targets whichever application is foreground for each event.
- Runtime uses the immutable startup-loaded configuration; editing or saving
  cannot change it before restart.
- One NovaMacro process is allowed per current Windows session. A secondary
  launch is rejected before it loads settings or owns runtime resources.

## Safety and product boundary

NovaMacro uses ordinary Windows input automation. The current project does not:

- modify or read GTA process memory;
- inject code into the game;
- bypass anti-cheat;
- automate through an anti-cheat bypass mechanism.

This architecture statement is not a claim that any game or online service
permits macros. Users remain responsible for applicable rules and policies.

## Not implemented

- multiple macros or simultaneous configurable hotkeys;
- live apply or hotkey replacement from the editor;
- physical hotkey capture;
- mouse triggers or mouse output;
- macro recording;
- loops, conditions, or a scripting language;
- modifier chords or explicit key-hold actions;
- F-key, numpad, punctuation, or other additional output keys;
- preset management or a GTA-specific preset library;
- screenshots, state-aware vision, GTA menu-state detection, OCR, or AI;
- import/export, plugins, tray integration, release packaging, or installer;
- automatic elevation or `%LocalAppData%` migration.
