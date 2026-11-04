using System.IO;
using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Link;
using NovaMacro.Core.Logging;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Scripting;
using NovaMacro.Core.Settings;
using NovaMacro.Core.Timing;
using NovaMacro.Platform.Game;
using NovaMacro.Platform.Input;
using NovaMacro.Platform.Screen;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Services;

/// <summary>Composition root: builds and owns every runtime service.</summary>
public sealed class AppHost : IDisposable
{
    private readonly System.Threading.Timer _tickTimer;
    private readonly System.Threading.Timer _watchTimer;
    private readonly System.Threading.Timer _saveTimer;
    private bool _gameWasRunning;

    public AppHost()
    {
        DataFolder = Path.Combine(AppContext.BaseDirectory, "NovaMacro Config");
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(Path.Combine(DataFolder, "MapTeleportBot"));

        Log = new DebugLog();
        Settings = SettingsStore.Open(Path.Combine(DataFolder, "Settings.ini"));
        Settings.SetInt(Sec.Device, "已使用", Settings.GetInt(Sec.Device, "已使用") + 1);
        UpdateLogFile();

        Hook = new GlobalInputHook();
        Output = new WindowsInputOutput();
        Game = new WindowsGameWindow();
        Screen = new WindowsScreenCapture();
        Notifier = new AppNotifier(this);
        Tools = new GameToolsService(Game, Settings, Notifier);
        Fps = new FpsMonitor(() => Game.ProcessId);
        Delay = new DelayProvider(
            () => new DelayOptions(
                Settings.GetBool(Sec.Other, "高精度延时"),
                Settings.GetBool(Sec.Other, "自适应延时"),
                Settings.GetInt(Sec.Other, "自适应延时容错")),
            Fps);
        Overlays = new OverlayService(this);
        Steam = new SteamClientService();
        Audio = new AudioLevelMonitor();
        Link = new LinkRoom();
        ReloadKeyMap();

        Services = new MacroServices
        {
            Settings = Settings,
            Input = new GameInput(Output, () => KeyMap, Delay),
            InputState = Hook,
            Game = Game,
            Screen = Screen,
            Notifier = Notifier,
            Tools = Tools,
            Clipboard = new WpfClipboard(),
            Urls = new ShellUrlLauncher(),
            Audio = Audio,
            Overlays = Overlays,
            Steam = Steam,
            DataFolder = DataFolder,
            Keys = () => KeyMap,
        };

        EnsureStarterMacros();
        Runtime = new MacroRuntime(Services, FeatureFactory.CreateBuiltIn().Concat(LoadCustomFeatures()));

        Hook.Handler = (code, down, repeat) => !CaptureMode && Runtime.HandleInput(code, down, repeat);
        Hook.BlockWinKey = () => Settings.GetBool(Sec.Switches, "释放WIN键") && Game.IsForeground;
        Link.LinkTriggered += (_, m) => ExecuteLink(m.Kind);
        Settings.Changed += OnSettingChanged;
        Game.GameExited += (_, _) => OnGameExited();
        Game.GameStarted += (_, _) => OnGameStarted();

        _tickTimer = new System.Threading.Timer(_ => SafeTick(), null, 15, 15);
        _watchTimer = new System.Threading.Timer(_ => WatchGame(), null, 0, 1000);
        _saveTimer = new System.Threading.Timer(_ => Settings.Save(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string DataFolder { get; }

    public DebugLog Log { get; }

    public SettingsStore Settings { get; }

    public GlobalInputHook Hook { get; }

    public WindowsInputOutput Output { get; }

    public WindowsGameWindow Game { get; }

    public WindowsScreenCapture Screen { get; }

    public AppNotifier Notifier { get; }

    public GameToolsService Tools { get; }

    public FpsMonitor Fps { get; }

    public DelayProvider Delay { get; }

    public OverlayService Overlays { get; }

    public SteamClientService Steam { get; }

    public AudioLevelMonitor Audio { get; }

    public LinkRoom Link { get; }

    public MacroServices Services { get; }

    public MacroRuntime Runtime { get; }

    public GameKeyMap KeyMap { get; private set; } = new();

    /// <summary>While true, physical input is not routed to features (hotkey capture dialogs).</summary>
    public bool CaptureMode { get; set; }

    public event EventHandler? GameStateChanged;

    public void Start()
    {
        Tools.CleanupAll();
        Hook.Start();
        if (Settings.GetBool(Sec.Other, "自适应延时"))
        {
            StartFps();
        }

        Log.Write(CommandRunner.IsElevated
            ? "* 已使用管理员权限运行"
            : "* 你当前为非管理员运行，按键模拟可能对游戏无效，断网/卡单/限速等小工具不可用");
        Log.Write("* 配置文件夹：程序目录\\NovaMacro Config");
        if (KeyMap.Source.Length == 0)
        {
            Log.Write("* 未找到 GTA 键位文件，使用默认键位");
        }
    }

    public void StartFps()
    {
        var error = Fps.Start();
        if (error is not null)
        {
            Notifier.ShowMessage("温馨提示", error);
        }
    }

    public void ReloadKeyMap()
    {
        var map = new GameKeyMap();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var preferSecondary = Settings.GetBool(Sec.Switches, "优先读取游戏辅助键位");
        foreach (var edition in new[] { Game.Edition == GtaEdition.Unknown ? GtaEdition.Enhanced : Game.Edition, GtaEdition.Legacy, GtaEdition.Enhanced }.Distinct())
        {
            var file = GtaControlFile.FindControlFile(documents, edition);
            if (file is null)
            {
                continue;
            }

            try
            {
                map.Apply(GtaControlFile.Parse(File.ReadAllText(file), preferSecondary), $@"文档\Rockstar Games\{(edition == GtaEdition.Enhanced ? "GTA V Enhanced" : "GTA V")}\Profiles\***\control");
                break;
            }
            catch (IOException)
            {
            }
        }

        // User overrides from [游戏键位覆盖] (动作名=键码).
        var overrides = Settings.Document.Find(Sec.KeyOverrides);
        if (overrides is not null)
        {
            foreach (var (name, value) in overrides.Entries)
            {
                if (GameActions.TryParse(name, out var action) && int.TryParse(value, out var code) && code != InputCodes.None)
                {
                    map[action] = code;
                }
            }
        }

        KeyMap = map;
    }

    public void ReloadCustomMacros()
    {
        Runtime.ReplaceFeatures(f => f is CustomMacroFeature, LoadCustomFeatures());
    }

    public IReadOnlyList<CustomMacroDefinition> CustomMacros { get; private set; } = Array.Empty<CustomMacroDefinition>();

    public void ExecuteLink(string? kind)
    {
        var result = kind switch
        {
            LinkKinds.NetworkCut => Tools.ToggleNetworkCut(),
            LinkKinds.Solo => Tools.ToggleSoloSession(),
            LinkKinds.KillProcess => Tools.KillGame(),
            _ => ToolResult.Fail("[联动功能] 未知联动类型"),
        };
        Notifier.Tip(result.Message);
    }

    public void SaveSoon() => _saveTimer.Change(400, Timeout.Infinite);

    public void Dispose()
    {
        _tickTimer.Dispose();
        _watchTimer.Dispose();
        _saveTimer.Dispose();
        Runtime.StopAll();
        Hook.Dispose();
        Output.ReleaseAll();
        Link.Dispose();
        Fps.Dispose();
        Audio.Dispose();
        Tools.Dispose();
        Settings.Save();
    }

    private IEnumerable<MacroFeature> LoadCustomFeatures()
    {
        CustomMacros = CustomMacroDefinition.LoadAll(DataFolder, Log.Write);
        var features = FeatureFactory.CreateCustom(CustomMacros);
        foreach (var f in features.OfType<CustomMacroFeature>().Where(f => f.ParseError is not null))
        {
            Log.Write($"[自定义宏] “{f.Macro.Name}” {f.ParseError}");
        }

        return features;
    }

    private void EnsureStarterMacros()
    {
        var root = CustomMacroDefinition.RootFolder(DataFolder);
        if (Directory.Exists(root))
        {
            return;
        }

        foreach (var starter in CustomMacroDefinition.Starters)
        {
            starter.Save(DataFolder);
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        SaveSoon();
        if (e.Section == Sec.Other && e.Key == "自适应延时")
        {
            if (Settings.GetBool(Sec.Other, "自适应延时"))
            {
                StartFps();
            }
            else
            {
                Fps.Stop();
            }
        }
        else if (e.Section == Sec.Other && e.Key == "保存日志")
        {
            UpdateLogFile();
        }
        else if (e.Section == Sec.KeyOverrides || (e.Section == Sec.Switches && e.Key == "优先读取游戏辅助键位"))
        {
            ReloadKeyMap();
        }
    }

    private void UpdateLogFile() =>
        Log.SaveFile = Settings.GetBool(Sec.Other, "保存日志") ? Path.Combine(DataFolder, "调试日志.txt") : null;

    private int _tickCount;

    private void SafeTick()
    {
        try
        {
            Runtime.Tick();
            if (++_tickCount % 16 == 0)
            {
                Hook.ReconcileStuckKeys();
            }
        }
        catch (Exception ex)
        {
            Log.Write("[运行时] " + ex.Message);
        }
    }

    private void WatchGame()
    {
        try
        {
            var running = Game.Refresh();
            if (running != _gameWasRunning)
            {
                _gameWasRunning = running;
                GameStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Write("[游戏检测] " + ex.Message);
        }
    }

    private void OnGameStarted()
    {
        ReloadKeyMap();
        if (string.IsNullOrWhiteSpace(Settings.GetString(Sec.Device, "游戏文件夹路径")) && Game.ExecutablePath is { } exe)
        {
            Settings.Set(Sec.Device, "游戏文件夹路径", Path.GetDirectoryName(exe)!);
            Log.Write("[游戏文件夹路径] 已找到游戏目录路径：" + Path.GetDirectoryName(exe));
        }

        Log.Write($"[游戏] 已检测到 GTAV {GtaInstall.DisplayName(Game.Edition)}（PID {Game.ProcessId}）");
    }

    private void OnGameExited()
    {
        Log.Write("[游戏] 游戏已退出");
        Runtime.StopAll();
        Tools.CleanupAll();
        if (Settings.GetBool(Sec.Switches, "启用同步退出"))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ((App)System.Windows.Application.Current).ExitApp());
        }
    }
}
