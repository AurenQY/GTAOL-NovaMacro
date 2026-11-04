using System.Text;

namespace NovaMacro.Core.Ini;

/// <summary>
/// Decodes configuration files written either by NovaMacro (UTF-8 with BOM) or by the
/// original tool (system ANSI code page, normally GBK / code page 936).
/// </summary>
public static class IniEncoding
{
    private static readonly Lazy<Encoding> Gbk = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    });

    public static Encoding LegacyEncoding => Gbk.Value;

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (IsValidUtf8(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return Gbk.Value.GetString(bytes);
    }

    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            int extra;
            if (b < 0x80)
            {
                i++;
                continue;
            }

            if ((b & 0xE0) == 0xC0)
            {
                extra = 1;
                if (b < 0xC2)
                {
                    return false;
                }
            }
            else if ((b & 0xF0) == 0xE0)
            {
                extra = 2;
            }
            else if ((b & 0xF8) == 0xF0 && b <= 0xF4)
            {
                extra = 3;
            }
            else
            {
                return false;
            }

            if (i + extra >= bytes.Length)
            {
                return false;
            }

            for (var k = 1; k <= extra; k++)
            {
                if ((bytes[i + k] & 0xC0) != 0x80)
                {
                    return false;
                }
            }

            i += extra + 1;
        }

        return true;
    }
}
