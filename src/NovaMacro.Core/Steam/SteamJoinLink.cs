using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NovaMacro.Core.Steam;

/// <summary>A saved Steam "join game" link for a BOT (stored as JSON in [地图差传设置] Steam_一号BOT / Steam_二号BOT).</summary>
public sealed record SteamJoinLink(string Url, string Nickname, DateTime ObtainedAt)
{
    private static readonly Regex ConsoleLogLine = new(
        @"\[(?<time>.{19})\] ExecuteSteamURL: ""(?<url>steam://rungame/(?<app>\d+)/(?<steam>\d+)/-steamjvp\S+?)""",
        RegexOptions.Compiled);

    private static readonly Regex AnyLink = new(@"steam://rungame/(?<app>\d+)/(?<steam>\d+)/-steamjvp\S+?(==|%3D%3D)", RegexOptions.Compiled);

    public string AppId => AnyLink.Match(Url) is { Success: true } m ? m.Groups["app"].Value : string.Empty;

    public string SteamId => AnyLink.Match(Url) is { Success: true } m ? m.Groups["steam"].Value : string.Empty;

    public string EditionName => AppId == "3240220" ? "增强版战局" : "传承版战局";

    public bool IsValid => AnyLink.IsMatch(Url);

    /// <summary>
    /// URL handed to Steam: '=' in the -steamjvp argument re-encoded as %3D, exactly like the
    /// original tool ("…/-steamjvp%3D…%3D%3D"), so Steam's protocol handler keeps the token intact.
    /// </summary>
    public string LaunchUrl
    {
        get
        {
            var i = Url.IndexOf("-steamjvp", StringComparison.OrdinalIgnoreCase);
            return i < 0 ? Url : Url[..i] + Url[i..].Replace("=", "%3D", StringComparison.Ordinal);
        }
    }

    public static SteamJoinLink? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            var url = node?["链接"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var nick = node?["昵称"]?.GetValue<string>() ?? string.Empty;
            var time = DateTime.TryParse(node?["获取时间"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;
            return new SteamJoinLink(url, nick, time);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson()
    {
        var obj = new JsonObject
        {
            ["链接"] = Url,
            ["昵称"] = Nickname,
            ["获取时间"] = ObtainedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
        return obj.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Finds the most recent join link in Steam's logs\console_log.txt content.</summary>
    public static SteamJoinLink? LatestFromConsoleLog(string logText, string nickname = "")
    {
        SteamJoinLink? latest = null;
        foreach (Match m in ConsoleLogLine.Matches(logText))
        {
            var time = DateTime.TryParse(m.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;
            var url = Normalize(m.Groups["url"].Value);
            if (latest is null || time >= latest.ObtainedAt)
            {
                latest = new SteamJoinLink(url, nickname, time);
            }
        }

        return latest;
    }

    /// <summary>Extracts a link pasted from the clipboard ("steam://rungame/[......]==").</summary>
    public static SteamJoinLink? FromText(string? text, DateTime now, string nickname = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var m = AnyLink.Match(text);
        return m.Success ? new SteamJoinLink(Normalize(m.Value), nickname, now) : null;
    }

    /// <summary>Steam logs URL-encode '=' as %3D; links are stored decoded like the original tool.</summary>
    public static string Normalize(string url) => url.Replace("%3D", "=", StringComparison.OrdinalIgnoreCase).Trim('"');
}
