using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Ini;
using NovaMacro.Core.Input;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Settings;
using NovaMacro.Core.Timing;

namespace NovaMacro.Core.Tests;

public sealed class FakeOutput : IInputOutput
{
    private readonly object _gate = new();

    public List<string> Events { get; } = new();

    public void Down(int code) => Add("D:" + KeyNames.GetName(code));

    public void Up(int code) => Add("U:" + KeyNames.GetName(code));

    public void Wheel(int notches) => Add("W:" + notches);

    public void MoveTo(int x, int y) => Add($"M:{x},{y}");

    public void MoveBy(int dx, int dy) => Add($"R:{dx},{dy}");

    public void ReleaseAll() => Add("ReleaseAll");

    /// <summary>Key names of completed taps (down followed by up).</summary>
    public List<string> Taps()
    {
        lock (_gate)
        {
            return Events.Where(e => e.StartsWith("D:", StringComparison.Ordinal)).Select(e => e[2..]).ToList();
        }
    }

    private void Add(string e)
    {
        lock (_gate)
        {
            Events.Add(e);
        }
    }
}

public sealed class FakeInputState : IInputState
{
    public HashSet<int> Down { get; } = new();

    public long IdleMilliseconds { get; set; }

    public event EventHandler<InputEvent>? InputChanged;

    public bool IsDown(int code) => Down.Contains(code);

    public bool IsModifierDown(ModifierMask mask)
    {
        if (mask == ModifierMask.None)
        {
            return false;
        }

        return (!mask.HasFlag(ModifierMask.Ctrl) || Down.Contains(InputCodes.LControl)) &&
               (!mask.HasFlag(ModifierMask.Shift) || Down.Contains(InputCodes.LShift)) &&
               (!mask.HasFlag(ModifierMask.Alt) || Down.Contains(InputCodes.LAlt));
    }

    public void Raise(int code, bool down) => InputChanged?.Invoke(this, new InputEvent(code, down, false, 0));
}

public sealed class FakeGame : IGameWindow
{
    public bool IsRunning { get; set; } = true;

    public bool IsForeground { get; set; } = true;

    public GtaEdition Edition { get; set; } = GtaEdition.Enhanced;

    public int ProcessId => 1234;

    public nint Handle => 1;

    public string? ExecutablePath => @"C:\Games\GTAV\GTA5_Enhanced.exe";

    public ScreenRect ClientBounds { get; set; } = new(0, 0, 2560, 1440);

    public bool IsWindowedMode => true;

    public bool IsUsKeyboardLayout { get; set; } = true;

    public bool Refresh() => IsRunning;

    public void Activate()
    {
    }
}

public sealed class FakeScreen : IScreenCapture
{
    public Func<ScreenRect, PixelBuffer> Source { get; set; } = r => new PixelBuffer(r.Width, r.Height);

    public ScreenRect VirtualScreen => new(0, 0, 2560, 1440);

    public PixelBuffer Capture(ScreenRect rect) => Source(rect);

    public PixelBuffer? LoadImage(string path) => null;

    public void SaveImage(PixelBuffer image, string path)
    {
    }
}

public sealed class FakeNotifier : INotifier
{
    public List<string> Messages { get; } = new();

    public void Log(string message)
    {
        lock (Messages)
        {
            Messages.Add(message);
        }
    }

    public void Tip(string message) => Log(message);

    public void Beep(bool on) => Log(on ? "beep-on" : "beep-off");

    public void ShowMessage(string title, string message) => Log(title + ":" + message);
}

public sealed class FakeTools : IGameTools
{
    public List<string> Calls { get; } = new();

    public bool IsNetworkCut { get; private set; }

    public bool IsSolo => false;

    public bool IsSessionLocked => false;

    public bool IsSpeedLimited => false;

    public ToolResult KillGame() => Record("kill");

    public ToolResult ToggleNetworkCut()
    {
        IsNetworkCut = !IsNetworkCut;
        return Record("cut");
    }

    public ToolResult SetNetworkCut(bool cut) => Record("cut:" + cut);

    public ToolResult ToggleSoloSession() => Record("solo");

    public ToolResult ToggleSessionLock() => Record("lock");

    public ToolResult ToggleSpeedLimit() => Record("speed");

    public ToolResult SetTemporaryBlock(string ruleName, bool block) => Record("temp:" + block);

    private ToolResult Record(string name)
    {
        lock (Calls)
        {
            Calls.Add(name);
        }

        return ToolResult.Ok(name);
    }
}

public sealed class FakeClipboard : IClipboardService
{
    public PixelBuffer? Image { get; private set; }

    public string? Text { get; set; }

    public void SetImage(PixelBuffer image) => Image = image;

    public void SetText(string text) => Text = text;

    public string? GetText() => Text;
}

public sealed class FakeUrls : IUrlLauncher
{
    public List<string> Opened { get; } = new();

    public void Open(string url) => Opened.Add(url);
}

/// <summary>A complete runtime wired to fakes, with zero delays.</summary>
public sealed class Harness
{
    public Harness(string ini = "")
    {
        Settings = new SettingsStore(IniDocument.Parse(ini), null, SettingsStore.LoadBundledDefaults());
        Output = new FakeOutput();
        State = new FakeInputState();
        Game = new FakeGame();
        Screen = new FakeScreen();
        Notifier = new FakeNotifier();
        Tools = new FakeTools();
        Clipboard = new FakeClipboard();
        Urls = new FakeUrls();
        Delay = new DelayProvider(() => new DelayOptions(false, false, 0));
        Services = new MacroServices
        {
            Settings = Settings,
            Input = new GameInput(Output, () => Keys, new ZeroDelay()),
            InputState = State,
            Game = Game,
            Screen = Screen,
            Notifier = Notifier,
            Tools = Tools,
            Clipboard = Clipboard,
            Urls = Urls,
            DataFolder = Path.Combine(Path.GetTempPath(), "novamacro-tests-" + Guid.NewGuid().ToString("N")),
            Keys = () => Keys,
        };
        Runtime = new MacroRuntime(Services, FeatureFactory.CreateBuiltIn());
    }

    public SettingsStore Settings { get; }

    public FakeOutput Output { get; }

    public FakeInputState State { get; }

    public FakeGame Game { get; }

    public FakeScreen Screen { get; }

    public FakeNotifier Notifier { get; }

    public FakeTools Tools { get; }

    public FakeClipboard Clipboard { get; }

    public FakeUrls Urls { get; }

    public DelayProvider Delay { get; }

    public GameKeyMap Keys { get; } = new();

    public MacroServices Services { get; }

    public MacroRuntime Runtime { get; }

    public void Press(int code, params int[] held)
    {
        foreach (var h in held)
        {
            State.Down.Add(h);
        }

        State.Down.Add(code);
        Runtime.HandleInput(code, true);
    }

    public void Release(int code, params int[] held)
    {
        State.Down.Remove(code);
        Runtime.HandleInput(code, false);
        foreach (var h in held)
        {
            State.Down.Remove(h);
        }
    }

    public void Tap(int code, params int[] held)
    {
        Press(code, held);
        Release(code, held);
    }

    public static void WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            Thread.Sleep(5);
        }
    }

    public void WaitIdle(MacroFeature feature)
    {
        Thread.Sleep(20);
        WaitFor(() => !feature.IsRunning);
    }
}

public sealed class ZeroDelay : IDelayProvider
{
    public void Wait(int milliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (milliseconds < 0 || milliseconds > 0)
        {
            Thread.Yield();
        }
    }
}
