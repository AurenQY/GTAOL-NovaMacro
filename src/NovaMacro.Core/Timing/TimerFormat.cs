using System.Text;

namespace NovaMacro.Core.Timing;

/// <summary>
/// Formats elapsed time with the original HUD timer syntax, e.g. "hh:mm:ss|:iii":
/// hh/mm/ss/iii are replaced; the part after '|' is rendered as a smaller suffix.
/// </summary>
public static class TimerFormat
{
    public static (string Main, string Suffix) Format(TimeSpan elapsed, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            pattern = "hh:mm:ss|:iii";
        }

        var bar = pattern.IndexOf('|');
        var main = bar >= 0 ? pattern[..bar] : pattern;
        var suffix = bar >= 0 ? pattern[(bar + 1)..] : string.Empty;
        return (Apply(elapsed, main), Apply(elapsed, suffix));
    }

    public static string FormatFlat(TimeSpan elapsed, string pattern)
    {
        var (main, suffix) = Format(elapsed, pattern);
        return main + suffix;
    }

    private static string Apply(TimeSpan t, string pattern)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            if (Match(pattern, i, "iii"))
            {
                sb.Append(t.Milliseconds.ToString("000"));
                i += 3;
            }
            else if (Match(pattern, i, "hh"))
            {
                sb.Append(((int)t.TotalHours).ToString("00"));
                i += 2;
            }
            else if (Match(pattern, i, "mm"))
            {
                sb.Append(t.Minutes.ToString("00"));
                i += 2;
            }
            else if (Match(pattern, i, "ss"))
            {
                sb.Append(t.Seconds.ToString("00"));
                i += 2;
            }
            else
            {
                sb.Append(pattern[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool Match(string s, int i, string token) =>
        i + token.Length <= s.Length && string.CompareOrdinal(s, i, token, 0, token.Length) == 0;
}
