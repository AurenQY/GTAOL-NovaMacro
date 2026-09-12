using NovaMacro.App.Configuration;

namespace NovaMacro.App;

internal enum RuntimeMacroState
{
    Disabled,
    Active,
    RegistrationFailed,
    RuntimeUnavailable
}

internal sealed record RuntimeMacroStatus(
    MacroId Id,
    RuntimeMacroState State,
    string? Diagnostic = null);

internal enum MultiMacroRuntimeState
{
    NoEnabledMacros,
    Active,
    PartiallyActive,
    NoRegistrationsActive,
    ServiceUnavailable
}

internal sealed class MultiMacroRuntimeStatus
{
    private readonly IReadOnlyList<RuntimeMacroStatus> _macros;

    internal MultiMacroRuntimeStatus(
        MultiMacroRuntimeState state,
        IEnumerable<RuntimeMacroStatus> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);

        State = state;
        _macros = Array.AsReadOnly(macros.ToArray());
    }

    internal MultiMacroRuntimeState State { get; }

    internal IReadOnlyList<RuntimeMacroStatus> Macros => _macros;
}
