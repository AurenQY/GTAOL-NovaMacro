namespace NovaMacro.Core;

public sealed class MacroSequence
{
    private readonly IReadOnlyList<MacroStep> _steps;

    public MacroSequence(IEnumerable<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        MacroStep[] copiedSteps = steps.ToArray();

        if (copiedSteps.Any(step => step is null))
        {
            throw new ArgumentException(
                "A macro sequence cannot contain a null step.",
                nameof(steps));
        }

        _steps = Array.AsReadOnly(copiedSteps);
    }

    public IReadOnlyList<MacroStep> Steps => _steps;
}
