namespace NovaMacro.Core.Settings;

public enum SettingKind
{
    Bool,
    Int,
    Text,
    Choice,
    /// <summary>A single input code (stored as the numeric code).</summary>
    Key,
    /// <summary>Win32 COLORREF (0x00BBGGRR) stored as decimal.</summary>
    Color,
    /// <summary>Modifier mask (功能键) stored as number 0..7.</summary>
    Modifier,
}

public sealed record SettingDescriptor(
    string Section,
    string Key,
    SettingKind Kind,
    string Default,
    string Label,
    string? Help = null,
    IReadOnlyList<string>? Choices = null,
    int Min = int.MinValue,
    int Max = int.MaxValue,
    bool Hidden = false);

public static class SettingValues
{
    public const string True = "真";
    public const string False = "假";

    public static string FromBool(bool value) => value ? True : False;

    public static bool ToBool(string? value, bool fallback = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim() switch
        {
            "真" or "1" or "true" or "True" or "TRUE" or "是" or "开" => true,
            "假" or "0" or "false" or "False" or "FALSE" or "否" or "关" => false,
            _ => fallback,
        };
    }
}
