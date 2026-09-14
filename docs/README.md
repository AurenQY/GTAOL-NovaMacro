# Documentation

## Index

- [Architecture](architecture.md): module boundaries, startup decisions,
  runtime coordination, persistence, ownership, and Windows isolation.
- [V1 product scope](v0-scope.md): current capabilities, input vocabulary,
  factory defaults, startup behavior, safety constraints, and deferred work.

The `v0-scope.md` filename is retained for repository link compatibility; its
content now describes the shipped V1 product state.

## Current product state

NovaMacro V1 is a configurable multi-macro Windows desktop application:

- zero to 32 ordinary editable macros;
- keyboard and Mouse4/Mouse5 global triggers;
- `Once` and mouse `RepeatWhileHeld` execution;
- keyboard, numpad, left-mouse-tap, and delay steps;
- portable JSON settings with V1/V2 migration and V3 writes;
- missing-only GTA factory defaults;
- restart-applied saves and immutable active runtime settings;
- single-instance ownership and deterministic boundary coverage.

The runtime sends input to the current foreground application. Mouse side-button
triggers are global while registered; V1 has no GTA foreground-process filter.

NovaMacro remains fixed-sequence automation. Recording, conditions, loops,
computer vision, OCR, GTA state recognition, live apply, plugins, and an
installer are not part of V1.
