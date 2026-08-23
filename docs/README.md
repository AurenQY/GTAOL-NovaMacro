# Documentation

## Index

- [Architecture](architecture.md): current project boundaries, runtime flow,
  ownership, lifecycle, Windows isolation, and hotkey policy boundary.
- [V0 scope](v0-scope.md): implemented infrastructure, the fixed-F8 technical
  slice, planned product work, and explicit non-goals.

## Current development state

NovaMacro is past the skeleton stage. The repository currently includes:

- a platform-independent macro model and non-reentrant runner;
- real Windows keyboard output through `SendInput`;
- configurable keyboard-hotkey infrastructure built on `RegisterHotKey`;
- deterministic hotkey lifecycle and concurrency coverage;
- a WPF technical slice that connects fixed `F8` to the built-in ABC macro;
- deterministic App coordinator tests and real Windows integration validation.

The current application is still a technical V0 slice. Configurable hotkey UX,
macro editing, JSON settings persistence, mouse triggers, and GTA-specific
behavior are not implemented.

## Validation snapshot

At the current documented baseline:

- the Release solution builds with 0 warnings and 0 errors;
- the complete automated suite has 178 passing tests;
- real Windows `SendInput`, global-hotkey, and F8-to-ABC application paths have
  been validated.

The next product boundary is configuration and persistence. That work is
planned, not present in the current UI.
