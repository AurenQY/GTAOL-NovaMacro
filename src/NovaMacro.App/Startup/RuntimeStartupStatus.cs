using NovaMacro.App.Configuration;
using NovaMacro.Windows;

namespace NovaMacro.App.Startup;

internal enum RuntimeStartupState
{
    ActiveStarter,
    ActiveSaved,
    InactiveInvalidSettings,
    InactiveUnavailableSettings,
    InactiveActivationFailed
}

internal sealed class RuntimeStartupStatus
{
    private readonly IReadOnlyList<string> _diagnostics;

    private RuntimeStartupStatus(
        RuntimeStartupState state,
        string message,
        IEnumerable<string> diagnostics)
    {
        State = state;
        Message = message;
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    internal RuntimeStartupState State { get; }

    internal bool IsActive =>
        State is RuntimeStartupState.ActiveStarter or
            RuntimeStartupState.ActiveSaved;

    internal string Message { get; }

    internal IReadOnlyList<string> Diagnostics => _diagnostics;

    internal static RuntimeStartupStatus Active(
        StartupConfigurationResult startupConfiguration)
    {
        ArgumentNullException.ThrowIfNull(startupConfiguration);

        ApplicationSettings runtimeSettings =
            startupConfiguration.RuntimeSettings ??
            throw new ArgumentException(
                "An active runtime status requires runtime settings.",
                nameof(startupConfiguration));

        string hotkey = FormatHotkey(runtimeSettings.Macro.Hotkey);

        return startupConfiguration.Status switch
        {
            StartupConfigurationStatus.StarterReady =>
                new RuntimeStartupStatus(
                    RuntimeStartupState.ActiveStarter,
                    $"Runtime active with starter configuration: {hotkey}. " +
                    "No saved configuration found.",
                    Array.Empty<string>()),

            StartupConfigurationStatus.SavedReady =>
                new RuntimeStartupStatus(
                    RuntimeStartupState.ActiveSaved,
                    $"Runtime active: {hotkey}.",
                    Array.Empty<string>()),

            _ => throw new ArgumentException(
                "Recovery startup configuration cannot be active.",
                nameof(startupConfiguration))
        };
    }

    internal static RuntimeStartupStatus Recovery(
        StartupConfigurationResult startupConfiguration)
    {
        ArgumentNullException.ThrowIfNull(startupConfiguration);

        return startupConfiguration.Status switch
        {
            StartupConfigurationStatus.InvalidRecovery =>
                new RuntimeStartupStatus(
                    RuntimeStartupState.InactiveInvalidSettings,
                    "Runtime inactive: saved configuration could not be loaded.",
                    startupConfiguration.Diagnostics),

            StartupConfigurationStatus.UnavailableRecovery =>
                new RuntimeStartupStatus(
                    RuntimeStartupState.InactiveUnavailableSettings,
                    "Runtime inactive: settings file could not be read.",
                    startupConfiguration.Diagnostics),

            _ => throw new ArgumentException(
                "Ready startup configuration requires runtime activation.",
                nameof(startupConfiguration))
        };
    }

    internal static RuntimeStartupStatus ActivationFailed(
        KeyboardHotkey hotkey,
        Exception error)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(error);

        string formattedHotkey = FormatHotkey(hotkey);

        return new RuntimeStartupStatus(
            RuntimeStartupState.InactiveActivationFailed,
            $"Runtime inactive: hotkey {formattedHotkey} could not be " +
            "registered.",
            new[] { error.Message });
    }

    private static string FormatHotkey(KeyboardHotkey hotkey)
    {
        var parts = new List<string>();
        HotkeyModifiers modifiers = hotkey.Modifiers;

        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(hotkey.Key.ToString());
        return string.Join("+", parts);
    }
}
