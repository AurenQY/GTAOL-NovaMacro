namespace NovaMacro.Core.Game;

public enum GtaEdition
{
    Unknown,

    /// <summary>传承版 — GTA5.exe.</summary>
    Legacy,

    /// <summary>增强版 — GTA5_Enhanced.exe.</summary>
    Enhanced,
}

public static class GtaInstall
{
    public const string LegacyExe = "GTA5.exe";
    public const string EnhancedExe = "GTA5_Enhanced.exe";
    public const int SteamAppIdLegacy = 271590;
    public const int SteamAppIdEnhanced = 3240220;

    public static readonly string[] ProcessNames = { "GTA5_Enhanced", "GTA5" };

    /// <summary>Launcher processes that may be killed together with the game ("同时结束R星登录器").</summary>
    public static readonly string[] LauncherProcessNames = { "RockstarService", "Launcher", "SocialClubHelper", "PlayGTAV" };

    /// <summary>Registry values (HKLM, WOW6432Node) searched to locate the game folder.</summary>
    public static readonly string[] InstallRegistryValues =
    {
        @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V|InstallFolderSteam",
        @"SOFTWARE\WOW6432Node\Rockstar Games\GTA V Enhanced|InstallFolderSteam",
        @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V|InstallFolderEpic",
        @"SOFTWARE\WOW6432Node\Rockstar Games\GTA V Enhanced|InstallFolderEpic",
        @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V|InstallFolder",
        @"SOFTWARE\WOW6432Node\Rockstar Games\GTA V Enhanced|InstallFolder",
    };

    public static GtaEdition EditionFromExe(string? exeName)
    {
        if (exeName is null)
        {
            return GtaEdition.Unknown;
        }

        var file = exeName[(exeName.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        var name = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        if (name.Equals("GTA5_Enhanced", StringComparison.OrdinalIgnoreCase))
        {
            return GtaEdition.Enhanced;
        }

        return name.Equals("GTA5", StringComparison.OrdinalIgnoreCase) ? GtaEdition.Legacy : GtaEdition.Unknown;
    }

    public static string DisplayName(GtaEdition edition) => edition switch
    {
        GtaEdition.Enhanced => "增强版",
        GtaEdition.Legacy => "传承版",
        _ => "未知版本",
    };

    /// <summary>Returns the game executable inside a folder, if any.</summary>
    public static string? FindExecutable(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        foreach (var exe in new[] { EnhancedExe, LegacyExe })
        {
            var path = Path.Combine(folder, exe);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
