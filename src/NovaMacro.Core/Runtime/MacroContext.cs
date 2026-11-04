using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Input;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Runtime;

/// <summary>Everything a feature needs, shared by all features of one runtime.</summary>
public sealed class MacroServices
{
    public required SettingsStore Settings { get; init; }

    public required GameInput Input { get; init; }

    public required IInputState InputState { get; init; }

    public required IGameWindow Game { get; init; }

    public required IScreenCapture Screen { get; init; }

    public required INotifier Notifier { get; init; }

    public required IGameTools Tools { get; init; }

    public required IClipboardService Clipboard { get; init; }

    public required IUrlLauncher Urls { get; init; }

    public IAudioLevelMonitor? Audio { get; init; }

    public IOverlayService? Overlays { get; init; }

    public ISteamClient? Steam { get; init; }

    /// <summary>True while the 小工具 window is open ([小工具配置] 热键仅在窗口存在时可用).</summary>
    public Func<bool> ToolsWindowOpen { get; init; } = () => true;

    /// <summary>Called with true/false when a macro starts/stops executing (timer resolution).</summary>
    public Action<bool>? ExecutionActive { get; init; }

    /// <summary>Folder that holds Settings.ini, CustomMacro\, MapTeleportBot\ …</summary>
    public required string DataFolder { get; init; }

    public required Func<GameKeyMap> Keys { get; init; }

    /// <summary>Current special/aux trigger key (辅助触发键).</summary>
    public int AuxKey => Settings.GetInt(Sec.Other, "辅助触发键");

    public bool IsAuxDown => InputState.IsDown(AuxKey);

    /// <summary>Scales a coordinate designed for a 2560x1440 client area (as the original does) to the game window.</summary>
    public (int X, int Y) Scale1440(double x, double y)
    {
        var b = Game.ClientBounds;
        var factor = b.Height / 1440.0;
        var designWidth = 2560 * factor;
        var offsetX = (b.Width - designWidth) / 2.0;
        return ((int)Math.Round(b.X + offsetX + (x * factor)), (int)Math.Round(b.Y + (y * factor)));
    }

    public ScreenRect Scale1440(double x, double y, double width, double height)
    {
        var (sx, sy) = Scale1440(x, y);
        var factor = Game.ClientBounds.Height / 1440.0;
        return new ScreenRect(sx, sy, Math.Max(1, (int)Math.Round(width * factor)), Math.Max(1, (int)Math.Round(height * factor)));
    }
}

/// <summary>Per-execution context passed to running macros.</summary>
public sealed class MacroContext
{
    public MacroContext(MacroServices services, FeatureDefinition feature, CancellationToken token)
    {
        Services = services;
        Feature = feature;
        Token = token;
    }

    public MacroServices Services { get; }

    public FeatureDefinition Feature { get; }

    public CancellationToken Token { get; }

    public GameInput Input => Services.Input;

    public SettingsStore Settings => Services.Settings;

    public void Press(GameAction action, KeyTiming timing, int times = 1) => Input.Press(action, timing, Token, times);

    public void Tap(int code, KeyTiming timing) => Input.Tap(code, timing, Token);

    public void Wait(int ms) => Input.Wait(ms, Token);

    public void Log(string message) => Services.Notifier.Log($"[{Feature.Name}] {message}");

    public void Tip(string message)
    {
        Services.Notifier.Tip(message);
        Log(message);
    }

    public bool IsDown(int code) => Services.InputState.IsDown(code);

    public bool IsDown(GameAction action) => Services.InputState.IsDown(Input.Code(action));
}
