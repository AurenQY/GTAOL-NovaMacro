# V1 product scope

This file retains its historical filename so existing repository links remain
valid. It now describes the shipped NovaMacro V1 product boundary.

## Current product

NovaMacro is a Windows x64 fixed-sequence input automation utility with a WPF
editor. A user can configure zero to 32 macros, save them portably, restart the
application, and activate enabled macros through global keyboard or Mouse4/
Mouse5 triggers.

NovaMacro is configured around GTA Online convenience macros, but it is not
GTA-aware: it does not inspect game state or select a GTA process. Generated
input goes to the current foreground application.

## First-run factory macros

Only a genuinely missing settings file produces these enabled in-memory
defaults:

1. **Buy All Ammo** — `Alt+R`, `Once`.
2. **Drop Bull Shark** — `Alt+E`, `Once`.
3. **Request Sparrow** — `Ctrl+Alt+H`, `Once`.
4. **Heavy Weapon Rapid Fire** — `Mouse4`, `RepeatWhileHeld`.

They are ordinary macros. Users can rename, edit, disable, delete, reorder, or
change their triggers and steps. They have fresh IDs and no factory lock or
origin metadata. After an explicit Save, the saved configuration is
authoritative; deleted defaults are not restored.

Missing settings are not automatically written. Existing V1/V2/V3 settings,
including valid empty V2/V3 configurations, never receive factory macro
injection. Invalid or unavailable settings retain recovery diagnostics and are
not silently replaced.

## Editor capabilities

The compact editor supports:

- zero to 32 macros;
- name and enabled state;
- keyboard or Mouse4/Mouse5 triggers;
- `Once` execution for keyboard or mouse triggers;
- `RepeatWhileHeld` for mouse triggers;
- Key Tap, Left Mouse Tap, and Delay steps;
- step insertion, removal, and ordering;
- validation and portable Save.

Factory macros appear directly in the same list as user-created macros. V1 has
no preset browser or Add-from-preset control.

## Trigger vocabulary

Keyboard trigger keys include:

- A-Z;
- F1-F11;
- Numpad0-Numpad9;
- numpad Add, Subtract, Multiply, Divide, and Decimal.

Control, Alt, and Shift may be combined. Modifierless A-Z is rejected by App
validation because global registration can suppress normal typing and create
self-trigger risk. Modifierless F1-F11 and supported numpad triggers are
allowed. NumpadEnter is not a distinct trigger.

Mouse triggers are Mouse4/XButton1 and Mouse5/XButton2. When an enabled macro
owns one of these buttons, NovaMacro globally captures it while the runtime is
active. Registered side-button events are swallowed; unregistered ones are
forwarded normally.

## Output vocabulary

Key Tap supports:

- A-Z and Digit0-Digit9;
- ArrowUp, ArrowDown, ArrowLeft, ArrowRight;
- Enter, Escape, Space, Tab, and Backspace;
- Numpad0-Numpad9;
- numpad Add, Subtract, Multiply, Divide, Decimal, and NumpadEnter.

Left Mouse Tap performs one left-button down/up pair. Delay steps use validated
whole milliseconds. Steps execute in displayed order.

## Execution behavior

One macro owns execution globally at a time. If any keyboard, mouse-once, or
held-mouse trigger arrives while another macro owns execution, the new trigger
is dropped immediately. It is not queued or retried.

`RepeatWhileHeld` follows this contract:

- quick tap: one complete iteration;
- hold: one immediate iteration, then serial repeats;
- release: finish the current iteration, then stop;
- release before the accepted worker begins: still one iteration.

The held macro keeps global ownership between iterations.

## Settings and restart behavior

Settings are stored at:

```text
AppContext.BaseDirectory/data/settings.json
```

The codec reads JSON V1, V2, and V3. New saves use V3. Loading V1/V2 migrates
the data in memory without automatically rewriting the file.

Save does not change the current runtime:

```text
startup loads A  → runtime A
edit and save B  → disk B, runtime still A
restart          → runtime B
```

There is no Apply button, live trigger replacement, or automatic restart.

## Startup and failure behavior

- Missing: show four factory macros and start that same immutable snapshot.
- Loaded: show and run only the saved snapshot.
- Invalid: show an empty recovery editor, no runtime, and diagnostics.
- Unavailable: show an empty recovery editor, no runtime, and storage errors.
- Registration failures: keep the editor usable and identify affected macros.
- Terminal trigger-service failure: keep diagnostic wording that distinguishes
  initial activation from a later runtime failure.

One NovaMacro process is allowed per current Windows session.

## Foreground and policy boundary

NovaMacro uses ordinary Windows `SendInput`. It sends each event to whichever
application is foreground at that moment. Users should ensure GTA is foreground
before triggering GTA macros. Mouse side-button capture is global; V1 does not
filter by GTA foreground process.

NovaMacro does not read or modify game memory, inject code, bypass anti-cheat,
claim to be undetectable, guarantee policy compliance, or guarantee that a game
or service permits macros.

## Not implemented in V1

- GTA state recognition, computer vision, OCR, or AI;
- macro recording;
- conditions, branches, loops, or a scripting language;
- explicit key-hold/chord output steps;
- live apply;
- GTA foreground-process filtering;
- preset browser or restore-defaults workflow;
- import/export, plugins, tray integration, or installer;
- automatic elevation or `%LocalAppData%` migration.
