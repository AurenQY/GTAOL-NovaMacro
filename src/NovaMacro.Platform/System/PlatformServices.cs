using System.Diagnostics;
using Microsoft.Win32;
using NovaMacro.Core.Game;
using NovaMacro.Core.Runtime;

namespace NovaMacro.Platform.SystemTools;

public sealed class SteamClientService : ISteamClient
{
    public bool IsRunning => Process.GetProcessesByName("steam").Length > 0;

    public string? InstallPath
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                using var lm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                path = lm?.GetValue("InstallPath") as string;
            }

            return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
        }
    }
}

public sealed class ShellUrlLauncher : IUrlLauncher
{
    public void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}

/// <summary>Locates the GTA V install folder (running process, registry, common Steam folder).</summary>
public static class GtaLocator
{
    public static string? Find(IGameWindow game)
    {
        if (game.ExecutablePath is { } running)
        {
            return Path.GetDirectoryName(running);
        }

        foreach (var entry in GtaInstall.InstallRegistryValues)
        {
            var parts = entry.Split('|');
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var key = hive.OpenSubKey(parts[0]);
                if (key?.GetValue(parts[1]) is string folder && GtaInstall.FindExecutable(folder) is not null)
                {
                    return folder;
                }
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            foreach (var relative in new[]
                     {
                         @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced",
                         @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
                         @"SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced",
                         @"SteamLibrary\steamapps\common\Grand Theft Auto V",
                         @"Program Files\Rockstar Games\Grand Theft Auto V Enhanced",
                         @"Program Files\Rockstar Games\Grand Theft Auto V",
                         @"Program Files\Epic Games\GTAV",
                     })
            {
                var folder = Path.Combine(drive.RootDirectory.FullName, relative);
                if (GtaInstall.FindExecutable(folder) is not null)
                {
                    return folder;
                }
            }
        }

        return null;
    }
}

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NovaMacro";

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, "\"" + exePath + "\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsSet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }
}

/// <summary>Beep tones (开启/关闭提示音) played off the caller's thread.</summary>
public static class Beeper
{
    public static void Play(int frequency, int durationMs)
    {
        frequency = Math.Clamp(frequency, 37, 32767);
        durationMs = Math.Clamp(durationMs, 1, 5000);
        _ = Task.Run(() =>
        {
            try
            {
                Console.Beep(frequency, durationMs);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or InvalidOperationException)
            {
            }
        });
    }
}
