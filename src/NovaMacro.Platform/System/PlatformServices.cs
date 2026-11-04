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

/// <summary>
/// 开机启动. NovaMacro requires administrator rights, and Windows silently skips elevated programs
/// listed under the Run registry key, so a logon scheduled task with highest privileges is used.
/// </summary>
public static class AutoStart
{
    private const string TaskName = "NovaMacro";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static CommandResult Set(bool enabled, string exePath)
    {
        // Remove the legacy Run entry written by earlier builds (it never worked for an elevated app).
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            key?.DeleteValue(TaskName, throwOnMissingValue: false);
        }

        if (!enabled)
        {
            var del = CommandRunner.Run("schtasks.exe", $"/Delete /TN {TaskName} /F");
            return del.Output.Contains("ERROR", StringComparison.OrdinalIgnoreCase) && !IsSet() ? new CommandResult(0, string.Empty) : del;
        }

        var tr = CommandRunner.Quote("\"" + exePath + "\"");
        return CommandRunner.Run("schtasks.exe", $"/Create /TN {TaskName} /TR {tr} /SC ONLOGON /RL HIGHEST /F");
    }

    public static bool IsSet() => CommandRunner.Run("schtasks.exe", $"/Query /TN {TaskName}").Success;
}

/// <summary>Beep tones (开启/关闭提示音) with volume control, played off the caller's thread.</summary>
public static class Beeper
{
    /// <param name="volumePercent">0..100, or -1 to use a moderate default level.</param>
    public static void Play(int frequency, int durationMs, int volumePercent = -1)
    {
        frequency = Math.Clamp(frequency, 37, 32767);
        durationMs = Math.Clamp(durationMs, 1, 5000);
        var gain = volumePercent < 0 ? 0.35f : Math.Clamp(volumePercent, 0, 100) / 100f;
        if (gain <= 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var generator = new NAudio.Wave.SampleProviders.SignalGenerator(44100, 1)
                {
                    Frequency = frequency,
                    Gain = gain,
                    Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin,
                };
                var tone = new NAudio.Wave.SampleProviders.OffsetSampleProvider(generator) { TakeSamples = 44100 * durationMs / 1000 };
                using var output = new NAudio.Wave.WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 50);
                output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(tone));
                output.Play();
                var deadline = Environment.TickCount64 + durationMs + 500;
                while (output.PlaybackState == NAudio.Wave.PlaybackState.Playing && Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(20);
                }
            }
            catch (Exception)
            {
                try
                {
                    Console.Beep(frequency, durationMs);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or InvalidOperationException)
                {
                }
            }
        });
    }
}
