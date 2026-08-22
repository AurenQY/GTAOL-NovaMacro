# V0 architecture

## Design principles

- Keep macro execution independent from WPF and Windows message handling.
- Keep Win32 P/Invoke code out of the UI project.
- Use small direct interfaces only where they create a real test or ownership
  boundary.
- Compose objects manually in the WPF application; do not add a dependency
  injection container or MVVM framework for V0.
- Do not build a generic scripting, plugin, or preset system.

## Project boundaries and ownership

### `NovaMacro.Core` — Core Engine agent

Will contain macro step definitions, the predefined V0.0 sequence, execution
coordination, non-reentrancy behavior, and narrow abstractions required to test
the runner. It must not reference WPF or call Win32 APIs.

### `NovaMacro.Windows` — Core Engine agent

Will contain Windows global-trigger registration, keyboard state checks,
`SendInput` translation, and all related P/Invoke declarations. It references
`NovaMacro.Core` and adapts Core concepts to Windows.

The V0 implementation may use `RegisterHotKey` for keyboard triggers, but its
public boundary must not claim that every trigger has a modifier. Mouse4 and
Mouse5 will require a different Windows mechanism later; that mechanism must
be addable without changing `MacroRunner`.

### `NovaMacro.App` — UI agent

Contains the WPF application, configuration user experience, status display,
portable settings store, application startup/shutdown, and composition root.
It references both production libraries. It must not contain P/Invoke or macro
execution logic.

### `NovaMacro.Tests` — QA agent

Contains automated tests for Core and Windows integration boundaries. It does
not reference the WPF executable at skeleton stage. UI and end-to-end behavior
will initially be covered by a manual reliability checklist.

## Dependency direction

```text
NovaMacro.Core
      ▲
      │
NovaMacro.Windows
      ▲
      │
NovaMacro.App ─────────► NovaMacro.Core

NovaMacro.Tests ──────► NovaMacro.Core
NovaMacro.Tests ──────► NovaMacro.Windows
```

Dependencies point toward Core. Core never depends on the UI or Windows
integration projects, and `NovaMacro.Windows` never depends on WPF.

## Planned runtime flow

```text
WPF configuration UI
        │
        ▼
Application coordinator
        │
        ├── register keyboard trigger ──► Windows trigger adapter
        │
        └── trigger received ───────────► Macro runner
                                               │
                                               ▼
                                      Windows keyboard sender
                                               │
                                               ▼
                                      current foreground window
```

The trigger source and macro runner are deliberately separate. Adding a future
mouse-button trigger should change the Windows trigger adapter and UI binding
model, not macro execution.

## Planned reliability rules

- Wait for the keyboard trigger keys to be released before sending the macro,
  including the case where the trigger has no modifiers.
- Ignore additional triggers while a macro is running; do not queue them.
- Keep the WPF UI thread responsive during delays.
- Release any generated key that remains down after failure or cancellation.
- Report registration conflicts and execution failures to the UI.
- Unregister the trigger during normal application shutdown.
- Do not auto-elevate. Input injection is supported only when Windows integrity
  levels permit it.
