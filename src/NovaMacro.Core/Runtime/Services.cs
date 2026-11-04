using NovaMacro.Core.Game;
using NovaMacro.Core.Imaging;

namespace NovaMacro.Core.Runtime;

/// <summary>The running game process/window as seen by the macros.</summary>
public interface IGameWindow
{
    bool IsRunning { get; }

    bool IsForeground { get; }

    GtaEdition Edition { get; }

    int ProcessId { get; }

    nint Handle { get; }

    string? ExecutablePath { get; }

    /// <summary>Client area in screen coordinates.</summary>
    ScreenRect ClientBounds { get; }

    /// <summary>True when the window looks like a bordered window (not exclusive full screen / borderless).</summary>
    bool IsWindowedMode { get; }

    /// <summary>True when the game's thread uses the US keyboard layout (00000409).</summary>
    bool IsUsKeyboardLayout { get; }

    /// <summary>Re-scans for the game process; returns <see cref="IsRunning"/>.</summary>
    bool Refresh();

    /// <summary>Brings the game window to the foreground.</summary>
    void Activate();
}

public interface IScreenCapture
{
    PixelBuffer Capture(ScreenRect rect);

    /// <summary>Bounds of the whole virtual desktop.</summary>
    ScreenRect VirtualScreen { get; }

    /// <summary>Loads a BMP/PNG reference image, or null when missing/invalid.</summary>
    PixelBuffer? LoadImage(string path);

    void SaveImage(PixelBuffer image, string path);
}

/// <summary>Steam client integration.</summary>
public interface ISteamClient
{
    bool IsRunning { get; }

    /// <summary>Steam install folder, or null.</summary>
    string? InstallPath { get; }
}

/// <summary>User feedback channel: debug log, HUD tips, beeps, message boxes.</summary>
public interface INotifier
{
    void Log(string message);

    void Tip(string message);

    void Beep(bool on);

    void ShowMessage(string title, string message);
}

public interface IClipboardService
{
    void SetImage(PixelBuffer image);

    void SetText(string text);

    string? GetText();
}

/// <summary>System-level game tools (firewall, process control, QoS…) implemented on Windows.</summary>
public interface IGameTools
{
    bool IsNetworkCut { get; }

    bool IsSolo { get; }

    bool IsSessionLocked { get; }

    bool IsSpeedLimited { get; }

    ToolResult KillGame();

    ToolResult ToggleNetworkCut();

    ToolResult SetNetworkCut(bool cut);

    ToolResult ToggleSoloSession();

    ToolResult ToggleSessionLock();

    ToolResult ToggleSpeedLimit();

    /// <summary>Blocks/unblocks the game temporarily (used by 断网悬停).</summary>
    ToolResult SetTemporaryBlock(string ruleName, bool block);
}

public readonly record struct ToolResult(bool Success, string Message)
{
    public static ToolResult Ok(string message) => new(true, message);

    public static ToolResult Fail(string message) => new(false, message);
}

public interface IUrlLauncher
{
    void Open(string url);
}

/// <summary>On-screen overlays (external crosshair, HUD timer).</summary>
public interface IOverlayService
{
    bool CrosshairVisible { get; }

    void ToggleCrosshair();

    void ToggleTimer();
}

public interface IAudioLevelMonitor
{
    /// <summary>Starts sampling system output audio. Returns false when unavailable.</summary>
    bool Start();

    void Stop();

    /// <summary>Current peak level 0..1.</summary>
    float Level { get; }
}
