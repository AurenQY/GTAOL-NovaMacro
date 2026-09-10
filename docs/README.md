# Documentation

## Index

- [Architecture](architecture.md): project boundaries, startup decisions,
  runtime and editor-save flows, ownership, lifecycle, and Windows isolation.
- [V0 scope](v0-scope.md): current capabilities, configuration format, startup
  recovery rules, supported keys, usage constraints, and deferred features.

## Current development state

NovaMacro now implements the configurable V0 vertical slice:

- one editable macro with a keyboard hotkey and ordered Key Tap/Delay steps;
- validation and portable versioned JSON persistence;
- startup loading from `data/settings.json`;
- configured hotkey registration and configured sequence execution;
- recovery UI for invalid, unavailable, or unregistrable settings;
- single-instance ownership for the current Windows session;
- real Windows keyboard output through `SendInput` to the foreground window;
- deterministic automated coverage plus real-Windows integration validation.

Saving is deliberately restart-applied: the active runtime keeps its immutable
startup configuration until NovaMacro exits. The editor, saved file, and active
runtime are separate states.

NovaMacro remains intentionally small. Multiple macros, live apply, physical
hotkey capture, mouse input, recording, loops, conditions, vision, OCR, AI, GTA
state detection, packaging, and installer behavior are not implemented.
