namespace NovaMacro.Core;

public sealed record MouseButtonTapStep : MacroStep
{
    public MouseButtonTapStep(MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                "Only left mouse-button output is supported.");
        }

        Button = button;
    }

    public MouseButton Button { get; }
}
