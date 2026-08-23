# NovaMacro

NovaMacro is a C#/.NET 10 WPF project for Windows input automation. The
repository now contains a working technical V0 slice, but it is not yet a
finished configurable macro product.

## Current technical slice

The current application uses one fixed, modifierless `F8` hotkey:

1. Launch NovaMacro.
2. Focus another application, such as Notepad.
3. Press and release `F8`.
4. NovaMacro sends `A`, waits 150 ms, sends `B`, waits 150 ms, then sends `C`.

The target is whichever application is in the foreground when input is sent.
The hotkey fires only after its physical release, holding it does not repeat
the macro, and a second activation is dropped while the first macro is still
running. Exiting NovaMacro unregisters `F8`.

This path has been validated with 178 automated tests and real Windows
applications, including the global-hotkey and `SendInput` integrations.

## Build and run

Prerequisites:

- Windows
- .NET 10 SDK

```powershell
dotnet build NovaMacro.sln --configuration Release
dotnet test NovaMacro.sln --configuration Release --no-build
dotnet run --project src/NovaMacro.App/NovaMacro.App.csproj --configuration Release
```

The current UI is a technical status window. It does not yet allow the user to
change `F8`, edit the ABC macro, or save settings.

## Repository structure

- `NovaMacro.Core`: platform-independent macro model and execution engine.
- `NovaMacro.Windows`: global-hotkey and `SendInput` integration.
- `NovaMacro.App`: WPF host, application composition, and lifecycle ownership.
- `NovaMacro.Tests`: Core, Windows lifecycle/concurrency, and App orchestration
  tests.

See the [documentation index](docs/README.md) for the current architecture,
scope, hotkey policy, and explicit non-goals.
