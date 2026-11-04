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

    /// <summary>[其他设置] 退出方式 — closing the main window keeps NovaMacro running in the tray.</summary>
    public bool KeepInTray
    {
        get => Host.Settings.GetString(Sec.Other, "退出方式") != "直接退出";
        set => Host.Settings.Set(Sec.Other, "退出方式", value ? "最小化到托盘" : "直接退出");
    }

    private bool _trayHintShown;

    /// <summary>Hides the main window to the tray (first time: shows a short balloon hint).</summary>
    public void HideToTray()
    {
        Shell.Hide();
        if (!_trayHintShown && _tray is not null)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(2500, "NovaMacro 仍在运行", "已最小化到托盘，热键依然有效。双击托盘图标重新打开主界面。", Forms.ToolTipIcon.Info);
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/Assets/NovaMacro.ico", UriKind.Absolute));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException)
        {
        }

        return System.Drawing.SystemIcons.Application;
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
            ShowCheckMargin = true,
        };
        var keepInTray = new Forms.ToolStripMenuItem("关闭后保留在托盘") { CheckOnClick = true };
        keepInTray.Click += (_, _) => KeepInTray = keepInTray.Checked;
        menu.Opening += (_, _) => keepInTray.Checked = KeepInTray;
        menu.Items.Add("主界面", null, (_, _) => ShowMain());
        menu.Items.Add("小工具", null, (_, _) => Shell.OpenTools());
        menu.Items.Add("按键宏设置", null, (_, _) => Shell.OpenSettings(null));
        menu.Items.Add("调试输出窗口", null, (_, _) => Shell.OpenDebug());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(keepInTray);
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray = new Forms.NotifyIcon
        {
            Text = "NovaMacro",
            Icon = LoadTrayIcon(),
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
