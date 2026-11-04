using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace NovaMacro.Platform.SystemTools;

public readonly record struct CommandResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Runs hidden console tools (netsh, powershell) and captures their output.</summary>
public static class CommandRunner
{
    private static readonly Lazy<bool> Elevated = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    /// <summary>Process elevation never changes while running; computed once.</summary>
    public static bool IsElevated => Elevated.Value;

    public static CommandResult Run(string file, string arguments, int timeoutMs = 15000)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (file.Equals("netsh", StringComparison.OrdinalIgnoreCase))
        {
            // netsh prints with the OEM code page; force UTF-8 console output.
            psi.FileName = "cmd.exe";
            psi.Arguments = "/d /c chcp 65001 >nul & netsh " + arguments;
        }

        try
        {
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }

                return new CommandResult(-1, "操作超时");
            }

            return new CommandResult(process.ExitCode, (output.Result + error.Result).Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CommandResult(-1, ex.Message);
        }
    }

    public static CommandResult PowerShell(string script, int timeoutMs = 20000)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script));
        return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, timeoutMs);
    }

    public static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    public static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
