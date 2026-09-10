# NovaMacro

NovaMacro is a C#/.NET 10 WPF application for configurable Windows keyboard
automation. The current V0 supports one user-configurable global hotkey and one
ordered macro sequence. It is a complete small vertical slice, not yet a
full-featured macro product.

## Current V0 flow

1. Launch NovaMacro.
2. Edit one macro: its name, trigger key, Ctrl/Alt/Shift modifiers, and ordered
   Key Tap or Delay steps.
3. Validate and save the configuration.
4. Close and restart NovaMacro.
5. NovaMacro loads `data/settings.json`, registers the configured hotkey, and
   executes the configured sequence when that hotkey is pressed and released.

Saving does not change the active runtime. If startup loaded configuration A,
editing and saving B changes the file on disk while A remains active. B becomes
active only after NovaMacro is restarted. There is no Apply button, live hotkey
replacement, or automatic restart.

When no settings file exists, NovaMacro runs the starter `F8` → A/150 ms/B/150
ms/C configuration without automatically creating the file. Invalid or
unreadable settings leave the runtime inactive and open the editor with a
starter recovery draft.

## Supported input

Hotkey triggers and macro output use separate key vocabularies:

- Trigger keys: `A`–`Z` and `F1`–`F11`, with Ctrl, Alt, Shift, or combinations.
- Modifierless triggers: `F1`–`F11` are allowed; `A`–`Z` are rejected by the
  application safety policy. `F12` is not supported.
- Output keys: `A`–`Z`, `Digit0`–`Digit9`, arrow keys, Enter, Escape, Space, Tab,
  and Backspace.

The macro sends ordinary `SendInput` events to whichever application is in the
foreground at the time each event is sent. It does not select or lock a target
window.

## Settings location

Settings use versioned JSON schema version 1 and are stored at
`data/settings.json` under the application base/output directory. For a normal
Release build in this repository, the path is:

```text
src/NovaMacro.App/bin/Release/net10.0-windows/data/settings.json
```

NovaMacro does not use or fall back to `%LocalAppData%` in V0.

## Build and run

Prerequisites:

- Windows
- .NET 10 SDK

```powershell
dotnet build NovaMacro.sln --configuration Release
dotnet test NovaMacro.sln --configuration Release --no-build
dotnet run --project src/NovaMacro.App/NovaMacro.App.csproj --configuration Release
```

Current verified baseline: Release build with 0 warnings and 0 errors; 653
automated tests passed with 0 failures and 0 skipped. Real Windows hotkey,
`SendInput`, startup recovery, persistence, and single-instance paths have also
been validated.

## Repository structure

- `NovaMacro.Core`: platform-independent macro model and execution engine.
- `NovaMacro.Windows`: global-hotkey and `SendInput` integration.
- `NovaMacro.App`: WPF UI, configuration, persistence, startup composition, and
  lifecycle ownership.
- `NovaMacro.Tests`: deterministic Core, Windows, configuration, persistence,
  UI-model, and App orchestration tests.

NovaMacro does not modify or read game memory, inject game code, or implement an
anti-cheat bypass. Ordinary Windows input automation is not a guarantee that a
particular game or service permits its use.

See the [documentation index](docs/README.md) for architecture, detailed V0
behavior, limitations, and explicit non-goals.
