# NovaMacro

NovaMacro is a Windows x64 C#/.NET 10 WPF utility for configurable,
fixed-sequence keyboard and mouse input automation. Version 1 is currently
configured around a small set of GTA Online convenience macros, while keeping
each macro editable as ordinary user configuration.

## Factory macros

When `data/settings.json` is genuinely missing, NovaMacro starts in memory with
four enabled macros:

| Macro | Trigger |
| --- | --- |
| Buy All Ammo | `Alt+R` |
| Drop Bull Shark | `Alt+E` |
| Request Sparrow | `Ctrl+Alt+H` |
| Heavy Weapon Rapid Fire | `Mouse4` |

Heavy Weapon Rapid Fire performs one iteration for a quick Mouse4 tap. Holding
Mouse4 repeats iterations serially; releasing it lets the current iteration
finish and prevents another from starting.

These factory-created macros are not locked templates. Users may edit, rename,
disable, delete, or change their triggers, steps, and delays. Once a
configuration has been saved, it is authoritative: deleted factory macros are
not automatically restored.

## Important runtime behavior

- NovaMacro sends input to whichever application is currently foreground. Make
  sure GTA is foreground before triggering a GTA macro.
- When an enabled macro owns Mouse4 or Mouse5, NovaMacro globally captures that
  button while the runtime is active. V1 does not filter triggers by the
  foreground GTA process.
- Only one macro executes at a time. A trigger received while another macro is
  running is dropped rather than queued.
- Saving does not live-apply. Saved changes become active after NovaMacro is
  restarted.

## Configuration and settings

The editor supports up to 32 macros, keyboard or Mouse4/Mouse5 triggers,
`Once` and mouse `RepeatWhileHeld` execution, keyboard taps, left-mouse taps,
delays, and step reordering.

Settings are stored portably at `data/settings.json` below the application
directory. NovaMacro reads legacy JSON V1/V2 settings and the current V3 schema;
new saves use V3. Loading an older file does not rewrite it automatically.
Factory defaults appear only for a genuinely missing file, not for an existing
empty, invalid, or unavailable configuration.

## Build and test

Prerequisites for building from source:

- Windows x64
- .NET 10 SDK

```powershell
dotnet build NovaMacro.sln --configuration Release
dotnet test NovaMacro.sln --configuration Release --no-build
dotnet run --project src/NovaMacro.App/NovaMacro.App.csproj --configuration Release
```

Current verified baseline: Release build with 0 warnings and 0 errors; 975
automated tests passed with 0 failures and 0 skipped. Real Windows input,
hotkey, first-run, persistence, single-instance, GTA functional, and compact UI
flows have also been validated.

## Repository structure

- `NovaMacro.Core`: platform-independent macro model and execution engine.
- `NovaMacro.Windows`: Win32 hotkey, mouse-hook, and `SendInput` integration.
- `NovaMacro.App`: WPF editor, configuration, persistence, startup composition,
  and runtime ownership.
- `NovaMacro.Tests`: deterministic Core, Windows, App, persistence, and UI tests.

NovaMacro does not read or modify game memory, inject game code, bypass
anti-cheat, recognize GTA state, or guarantee compliance with any game or
service policy. Users remain responsible for applicable rules.

See the [documentation index](docs/README.md) for architecture, detailed V1
behavior, limitations, and explicit non-goals.
