# NovaMacro

NovaMacro is a Windows desktop application for configurable keyboard and mouse
input automation. The repository currently contains only the V0 project
skeleton; macro execution, global hotkeys, and configuration behavior have not
yet been implemented.

## V0 goal

A user can configure one global keyboard trigger, focus another window, press
the trigger, and reliably execute the deterministic test sequence `A`, wait
150 ms, `B`, wait 150 ms, `C`.

## Build

Prerequisite: .NET 10 SDK.

```powershell
dotnet build NovaMacro.sln
```

Start with [docs/README.md](docs/README.md) for the current scope and
architecture.
