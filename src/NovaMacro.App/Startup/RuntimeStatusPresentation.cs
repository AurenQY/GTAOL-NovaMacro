using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Startup;

internal sealed class RuntimeStatusPresentation
{
    private RuntimeStatusPresentation(
        string message,
        bool isHealthy,
        IEnumerable<string> diagnostics)
    {
        Message = message;
        IsHealthy = isHealthy;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    internal string Message { get; }

    internal bool IsHealthy { get; }

    internal IReadOnlyList<string> Diagnostics { get; }

    internal static RuntimeStatusPresentation FromInitial(
        MultiMacroRuntimeStatus status,
        ApplicationSettings startupSettings)
    {
        return From(status, startupSettings, RuntimeStatusProvenance.Initial);
    }

    internal static RuntimeStatusPresentation FromUpdate(
        MultiMacroRuntimeStatus status,
        ApplicationSettings startupSettings)
    {
        return From(status, startupSettings, RuntimeStatusProvenance.Update);
    }

    private static RuntimeStatusPresentation From(
        MultiMacroRuntimeStatus status,
        ApplicationSettings startupSettings,
        RuntimeStatusProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(startupSettings);

        var macrosById = startupSettings.Macros.ToDictionary(macro => macro.Id);
        var diagnostics = new List<string>();

        foreach (RuntimeMacroStatus macroStatus in status.Macros)
        {
            if (macroStatus.State != RuntimeMacroState.RegistrationFailed ||
                !macrosById.TryGetValue(macroStatus.Id, out MacroDefinition? macro))
            {
                continue;
            }

            diagnostics.Add(
                $"{macro.Name} ({FormatTrigger(macro.Trigger)}): " +
                (macroStatus.Diagnostic ?? "Registration failed."));
        }

        if (status.State == MultiMacroRuntimeState.ServiceUnavailable)
        {
            diagnostics.AddRange(status.Macros
                .Where(macro => macro.State == RuntimeMacroState.RuntimeUnavailable)
                .Select(macro => macro.Diagnostic)
                .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
                .Distinct(StringComparer.Ordinal)!);
        }

        return new RuntimeStatusPresentation(
            status.State switch
            {
                MultiMacroRuntimeState.Active =>
                    "Runtime: Active",
                MultiMacroRuntimeState.PartiallyActive =>
                    "Runtime partially active: some enabled macros could not be registered.",
                MultiMacroRuntimeState.NoEnabledMacros =>
                    "Runtime inactive: no macros are enabled.",
                MultiMacroRuntimeState.NoRegistrationsActive =>
                    "Runtime inactive: no enabled macro registrations are active.",
                MultiMacroRuntimeState.ServiceUnavailable =>
                    provenance == RuntimeStatusProvenance.Initial
                    ? "Runtime unavailable: the global hotkey service could not become available during startup."
                    : "Runtime unavailable: the global hotkey service became unavailable after runtime startup.",
                _ => throw new InvalidOperationException(
                    $"Unsupported multi-macro runtime state '{status.State}'.")
            },
            status.State == MultiMacroRuntimeState.Active,
            diagnostics);
    }

    internal static string FormatExecutionFailure(
        MacroId id,
        Exception exception,
        ApplicationSettings startupSettings)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(startupSettings);

        MacroDefinition? macro = null;
        foreach (MacroDefinition candidate in startupSettings.Macros)
        {
            if (candidate.Id == id)
            {
                macro = candidate;
                break;
            }
        }

        return macro is null
            ? $"A running macro failed: {exception.Message}"
            : $"{macro.Name} ({FormatTrigger(macro.Trigger)}) failed: " +
                exception.Message;
    }

    internal static string FormatTrigger(MacroTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        return trigger switch
        {
            KeyboardMacroTrigger keyboard => FormatHotkey(keyboard.Hotkey),
            MouseButtonMacroTrigger { Button: MouseButton.XButton1 } => "Mouse4",
            MouseButtonMacroTrigger { Button: MouseButton.XButton2 } => "Mouse5",
            MouseButtonMacroTrigger mouse => mouse.Button.ToString(),
            _ => trigger.GetType().Name
        };
    }

    internal static string FormatHotkey(KeyboardHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        var parts = new List<string>();
        if ((hotkey.Modifiers & HotkeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(hotkey.Key.ToString());
        return string.Join("+", parts);
    }

    private enum RuntimeStatusProvenance
    {
        Initial,
        Update
    }
}
