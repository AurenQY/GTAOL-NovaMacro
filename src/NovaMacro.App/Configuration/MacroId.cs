namespace NovaMacro.App.Configuration;

internal readonly record struct MacroId
{
    internal MacroId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A macro ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    internal Guid Value { get; }

    internal bool IsEmpty => Value == Guid.Empty;

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
