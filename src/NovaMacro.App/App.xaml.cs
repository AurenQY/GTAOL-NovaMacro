using System.Windows;
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
            MessageBox.Show("已经有一个 [NovaMacro] 程序正在运行", "NovaMacro", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Host?.Log.Write("[界面] 未处理的异常：" + args.Exception.Message);
            args.Handled = true;
        };

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
        var menu = new Forms.ContextMenuStrip();
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
