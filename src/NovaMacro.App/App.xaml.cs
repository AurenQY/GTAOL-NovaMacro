using System.Windows;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.App.Windows;
using NovaMacro.Core.Settings;
using Forms = System.Windows.Forms;

namespace NovaMacro.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;

    public AppHost Host { get; private set; } = null!;

    public MainWindow Shell { get; private set; } = null!;

    public static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "NovaMacro.SingleInstance", out var created);
        if (!created)
        {
            Dialog.Show(null, "已经有一个 [NovaMacro] 程序正在运行", "NovaMacro");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Host?.Log.Write("[界面] 未处理的异常：" + args.Exception.Message);
            args.Handled = true;
        };

        // Avoid long blocking GCs: the keyboard hook thread must never be paused for long.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Host = new AppHost();
        Host.Start();
        Shell = new MainWindow(Host);
        Shell.Show();
        CreateTray();

        if (Host.Settings.GetBool(Sec.Tools, "默认启动"))
        {
            Shell.OpenTools();
        }
    }

    public void ExitApp()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        Host?.Dispose();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }

    public void ShowMain()
    {
        Shell.Show();
        if (Shell.WindowState == WindowState.Minimized)
        {
            Shell.WindowState = WindowState.Normal;
        }

        Shell.Activate();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()),
            BackColor = System.Drawing.Color.FromArgb(0x22, 0x26, 0x2E),
            ForeColor = System.Drawing.Color.FromArgb(0xE8, 0xEA, 0xF0),
            ShowImageMargin = false,
        };
        menu.Items.Add("主界面", null, (_, _) => ShowMain());
        menu.Items.Add("小工具", null, (_, _) => Shell.OpenTools());
        menu.Items.Add("按键宏设置", null, (_, _) => Shell.OpenSettings(null));
        menu.Items.Add("调试输出窗口", null, (_, _) => Shell.OpenDebug());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray = new Forms.NotifyIcon
        {
            Text = "NovaMacro",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMain();
    }
}

/// <summary>Dark colours for the tray context menu (WinForms).</summary>
internal sealed class DarkMenuColors : Forms.ProfessionalColorTable
{
    private static readonly System.Drawing.Color Back = System.Drawing.Color.FromArgb(0x22, 0x26, 0x2E);
    private static readonly System.Drawing.Color Hot = System.Drawing.Color.FromArgb(0x26, 0x3A, 0x5E);
    private static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(0x3D, 0x43, 0x50);

    public override System.Drawing.Color ToolStripDropDownBackground => Back;

    public override System.Drawing.Color MenuBorder => Line;

    public override System.Drawing.Color MenuItemBorder => Hot;

    public override System.Drawing.Color MenuItemSelected => Hot;

    public override System.Drawing.Color MenuItemSelectedGradientBegin => Hot;

    public override System.Drawing.Color MenuItemSelectedGradientEnd => Hot;

    public override System.Drawing.Color ImageMarginGradientBegin => Back;

    public override System.Drawing.Color ImageMarginGradientMiddle => Back;

    public override System.Drawing.Color ImageMarginGradientEnd => Back;

    public override System.Drawing.Color SeparatorDark => Line;

    public override System.Drawing.Color SeparatorLight => Back;
}
