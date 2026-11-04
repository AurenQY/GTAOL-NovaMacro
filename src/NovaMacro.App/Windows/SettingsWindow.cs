using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Features;
using NovaMacro.Core.Game;
using NovaMacro.Core.Input;
using NovaMacro.Core.Settings;
using NovaMacro.Core.Steam;
using NovaMacro.Platform.SystemTools;

namespace NovaMacro.App.Windows;

/// <summary>按键宏设置 — every Settings.ini section rendered from the schema.</summary>
public sealed class SettingsWindow : Window
{
    private const string KeyMapPage = "游戏键位";
    private const string HotkeyPage = "全部热键";

    private readonly AppHost _host;
    private readonly ListBox _sections = new() { Width = 170, Margin = new Thickness(6) };
    private readonly ContentControl _page = new();

    public SettingsWindow(AppHost host, string? section)
    {
        _host = host;
        Title = "按键宏设置";
        Width = 900;
        Height = 700;
        WindowPositions.Restore(this, host.Settings, "设置界面");
        LocationChanged += (_, _) => WindowPositions.Save(this, host.Settings, "设置界面");

        var pages = SettingsSchema.SectionOrder.Where(s => SettingsSchema.All.Any(d => d.Section == s && !d.Hidden)).ToList();
        pages.Insert(0, HotkeyPage);
        pages.Insert(1, KeyMapPage);
        foreach (var p in pages)
        {
            _sections.Items.Add(p);
        }

        _sections.SelectionChanged += (_, _) => ShowPage(_sections.SelectedItem as string);
        var dock = new DockPanel();
        DockPanel.SetDock(_sections, Dock.Left);
        dock.Children.Add(_sections);
        var footer = Ui.Row(
            Ui.Btn("恢复本页默认", ResetPage, tip: "删除本页在 Settings.ini 中的所有值，恢复为默认配置"),
            Ui.Btn("打开ini配置文件", () => Process.Start(new ProcessStartInfo(_host.Settings.FilePath!) { UseShellExecute = true })),
            Ui.Btn("关闭设置", Close, accent: true));
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4) });
        Content = dock;
        _sections.SelectedItem = section is not null && pages.Contains(section) ? section : pages[0];
        Closed += (_, _) => _host.Settings.Save();
    }

    private string? Current => _sections.SelectedItem as string;

    private void ResetPage()
    {
        if (Current is null || Current == HotkeyPage)
        {
            return;
        }

        var section = Current == KeyMapPage ? Sec.KeyOverrides : Current;
        if (MessageBox.Show(this, $"确定恢复【{Current}】为默认配置？", "重置", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            _host.Settings.ResetSection(section);
            ShowPage(Current);
        }
    }

    private void ShowPage(string? section)
    {
        if (section is null)
        {
            return;
        }

        var panel = new StackPanel { Margin = new Thickness(6) };
        panel.Children.Add(Ui.Text(section, size: 18, weight: FontWeights.Bold));
        switch (section)
        {
            case HotkeyPage:
                BuildHotkeys(panel);
                break;
            case KeyMapPage:
                BuildKeyMap(panel);
                break;
            default:
                var grid = Ui.TwoColumns(260);
                foreach (var d in SettingsSchema.All.Where(d => d.Section == section && !d.Hidden))
                {
                    var label = Ui.Text(d.Label);
                    label.ToolTip = d.Help;
                    var editor = Editor(d);
                    if (d.Help is not null)
                    {
                        editor.ToolTip = d.Help;
                    }

                    Ui.AddRow(grid, label, editor);
                }

                panel.Children.Add(grid);
                BuildExtras(section, panel);
                break;
        }

        _page.Content = panel;
    }

    private FrameworkElement Editor(SettingDescriptor d)
    {
        var s = _host.Settings;
        switch (d.Kind)
        {
            case SettingKind.Bool:
                var cb = new CheckBox { IsChecked = s.GetBool(d.Section, d.Key) };
                cb.Checked += (_, _) => s.SetBool(d.Section, d.Key, true);
                cb.Unchecked += (_, _) => s.SetBool(d.Section, d.Key, false);
                return cb;
            case SettingKind.Choice:
                var combo = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var c in d.Choices!)
                {
                    combo.Items.Add(c);
                }

                combo.SelectedItem = s.GetString(d.Section, d.Key);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string v)
                    {
                        s.Set(d.Section, d.Key, v);
                    }
                };
                return combo;
            case SettingKind.Key:
                var keyButton = new Button { Content = KeyNames.GetName(s.GetInt(d.Section, d.Key)), MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
                keyButton.Click += (_, _) =>
                {
                    var hk = HotkeyCaptureWindow.Capture(this, _host, d.Label, singleKey: true);
                    if (hk is { } h)
                    {
                        s.SetInt(d.Section, d.Key, h.Key);
                        keyButton.Content = KeyNames.GetName(h.Key);
                    }
                };
                return keyButton;
            case SettingKind.Modifier:
                var mods = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
                for (var i = 0; i <= 7; i++)
                {
                    mods.Items.Add(Hotkey.ModifierText((ModifierMask)i));
                }

                mods.SelectedIndex = Math.Clamp(s.GetInt(d.Section, d.Key), 0, 7);
                mods.SelectionChanged += (_, _) => s.SetInt(d.Section, d.Key, mods.SelectedIndex);
                return mods;
            case SettingKind.Color:
                var colorRef = s.GetInt(d.Section, d.Key);
                var swatch = new Border { Width = 28, Height = 20, Margin = new Thickness(3), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1) };
                var colorBox = new TextBox { Text = colorRef.ToString(), Width = 120 };
                void Paint()
                {
                    if (int.TryParse(colorBox.Text, out var v))
                    {
                        swatch.Background = new SolidColorBrush(Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF)));
                    }
                }

                Paint();
                colorBox.TextChanged += (_, _) =>
                {
                    if (int.TryParse(colorBox.Text, out var v))
                    {
                        s.SetInt(d.Section, d.Key, v);
                        Paint();
                    }
                };
                var pick = Ui.Btn("取色", () =>
                {
                    using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
                    if (int.TryParse(colorBox.Text, out var v))
                    {
                        dialog.Color = System.Drawing.ColorTranslator.FromWin32(v);
                    }

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        colorBox.Text = System.Drawing.ColorTranslator.ToWin32(dialog.Color).ToString();
                    }
                });
                return Ui.Row(swatch, colorBox, pick);
            case SettingKind.Int:
                var num = new TextBox { Text = s.GetInt(d.Section, d.Key).ToString(), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
                num.LostFocus += (_, _) =>
                {
                    if (int.TryParse(num.Text, out var v))
                    {
                        s.SetInt(d.Section, d.Key, Math.Clamp(v, d.Min, d.Max));
                    }

                    num.Text = s.GetInt(d.Section, d.Key).ToString();
                };
                return num;
            default:
                var text = new TextBox { Text = s.GetString(d.Section, d.Key), MinWidth = 300, TextWrapping = TextWrapping.Wrap };
                text.LostFocus += (_, _) => s.Set(d.Section, d.Key, text.Text);
                return text;
        }
    }

    private void BuildHotkeys(StackPanel panel)
    {
        panel.Children.Add(Ui.Text("点击按钮修改热键，右键恢复默认。小工具热键需要在【小工具配置】中启用。", dim: true));
        var grid = Ui.TwoColumns(200);
        foreach (var f in FeatureCatalog.All.Where(f => f.HotkeySection is not null))
        {
            var feature = f;
            var button = new Button { Content = _host.Settings.GetHotkey(feature).ToString(), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = feature.Help };
            button.Click += (_, _) =>
            {
                var hk = HotkeyCaptureWindow.Capture(this, _host, "设置热键 - " + feature.Name);
                if (hk is { } h)
                {
                    _host.Settings.SetHotkey(feature, h);
                    button.Content = h.ToString();
                }
            };
            button.MouseRightButtonUp += (_, _) =>
            {
                _host.Settings.SetHotkey(feature, feature.DefaultHotkey);
                button.Content = feature.DefaultHotkey.ToString();
            };
            Ui.AddRow(grid, Ui.Text(feature.Name), button);
        }

        panel.Children.Add(grid);
    }

    private void BuildKeyMap(StackPanel panel)
    {
        var map = _host.KeyMap;
        panel.Children.Add(Ui.Text(map.Source.Length > 0 ? "读取于文件夹：" + map.Source : "未找到 GTA 键位文件（control\\user.xml），当前使用默认键位", dim: true));
        panel.Children.Add(Ui.Text("如游戏内修改过键位，可在此覆盖；右键按钮取消覆盖。", dim: true));
        var grid = Ui.TwoColumns(200);
        foreach (var info in GameActions.All)
        {
            var action = info;
            var overridden = _host.Settings.Document.Get(Sec.KeyOverrides, action.Action.ToString()) is not null;
            var button = new Button
            {
                Content = KeyNames.GetName(map[action.Action]) + (overridden ? "  (覆盖)" : string.Empty),
                MinWidth = 180,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = action.GtaInput,
            };
            button.Click += (_, _) =>
            {
                var hk = HotkeyCaptureWindow.Capture(this, _host, action.DisplayName, singleKey: true);
                if (hk is { } h && !h.IsEmpty)
                {
                    _host.Settings.SetInt(Sec.KeyOverrides, action.Action.ToString(), h.Key);
                    ShowPage(KeyMapPage);
                }
            };
            button.MouseRightButtonUp += (_, _) =>
            {
                _host.Settings.Document.Remove(Sec.KeyOverrides, action.Action.ToString());
                _host.ReloadKeyMap();
                ShowPage(KeyMapPage);
            };
            Ui.AddRow(grid, Ui.Text(action.DisplayName), button);
        }

        panel.Children.Add(Ui.Row(Ui.Btn("重新读取游戏键位", () =>
        {
            _host.ReloadKeyMap();
            ShowPage(KeyMapPage);
        })));
        panel.Children.Add(grid);
    }

    private void BuildExtras(string section, StackPanel panel)
    {
        switch (section)
        {
            case Sec.MapTeleport:
                BuildBotLinks(panel);
                break;
            case Sec.Device:
                panel.Children.Add(Ui.Row(
                    Ui.Btn("选择游戏文件夹", BrowseGameFolder),
                    Ui.Btn("自动寻找", () =>
                    {
                        var found = GtaLocator.Find(_host.Game);
                        if (found is null)
                        {
                            MessageBox.Show(this, "[游戏文件夹路径] 未能找到游戏目录路径！请检查是否没有运行、下载游戏，也可手动修改路径！");
                            return;
                        }

                        _host.Settings.Set(Sec.Device, "游戏文件夹路径", found);
                        ShowPage(section);
                    })));
                break;
            case Sec.Switches:
                panel.Children.Add(Ui.Row(Ui.Btn("应用开机启动设置", () =>
                {
                    AutoStart.Set(_host.Settings.GetBool(Sec.Switches, "启用开机启动"), Environment.ProcessPath!);
                    MessageBox.Show(this, "设置完成！");
                })));
                break;
            case Sec.Crosshair:
                panel.Children.Add(new CrosshairEditor(_host));
                break;
        }
    }

    private void BuildBotLinks(StackPanel panel)
    {
        var feature = _host.Runtime.Get<MapTeleportFeature>();
        foreach (var bot in new[] { "Steam_一号BOT", "Steam_二号BOT" })
        {
            var key = bot;
            var link = SteamJoinLink.FromJson(_host.Settings.GetString(Sec.MapTeleport, key));
            var info = link is null
                ? "未读取战局链接"
                : $"{link.EditionName}  昵称：{link.Nickname}  获取时间：{link.ObtainedAt:yyyy-MM-dd HH:mm:ss}";
            var group = Ui.Col(
                Ui.Text(info, dim: true),
                Ui.Row(
                    Ui.Btn("读取最近加入的战局链接", () =>
                    {
                        MessageBox.Show(this,
                            "【请勿马上关闭本窗口，仔细阅读↓】\n该窗口正在等待读取，你需要在相应的Steam好友或组中手动加入玩家战局，之后即可点击 [确定] 来进行读取！",
                            "等待读取【" + key + "】战局链接...");
                        if (feature?.ReadLatestLink(key) is not null)
                        {
                            ShowPage(Sec.MapTeleport);
                        }
                    }),
                    Ui.Btn("从剪贴板粘贴链接", () =>
                    {
                        var parsed = SteamJoinLink.FromText(Clipboard.ContainsText() ? Clipboard.GetText() : null, DateTime.Now, key);
                        if (parsed is null)
                        {
                            MessageBox.Show(this, "剪贴版里面找不到完整战局链接\n链接格式（包含即可）：steam://rungame/[......]==", "操作失败");
                            return;
                        }

                        _host.Settings.Set(Sec.MapTeleport, key, parsed.ToJson());
                        ShowPage(Sec.MapTeleport);
                    }),
                    Ui.Btn("复制链接", () =>
                    {
                        if (link is not null)
                        {
                            Clipboard.SetText(link.Url);
                            MessageBox.Show(this, "已将战局链接复制到剪贴版中", "操作完成");
                        }
                    }),
                    Ui.Btn("测试加入", () =>
                    {
                        if (link is not null)
                        {
                            new ShellUrlLauncher().Open(link.Url);
                        }
                    })));
            panel.Children.Add(Ui.Card(group, key));
        }

        var folder = Path.Combine(_host.DataFolder, "MapTeleportBot");
        panel.Children.Add(Ui.Card(Ui.Col(
            Ui.Text("Steam点击加入需要参考图片：BOT1.BMP（好友位截图）与 JOIN.BMP（“加入游戏”按钮截图）。截图后复制，点击下方按钮保存。", dim: true),
            Ui.Row(
                Ui.Btn("剪贴板图片 → BOT1.BMP", () => SaveClipboardImage(Path.Combine(folder, "BOT1.BMP"))),
                Ui.Btn("剪贴板图片 → JOIN.BMP", () => SaveClipboardImage(Path.Combine(folder, "JOIN.BMP"))),
                Ui.Btn("打开参考图片文件夹", () => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })))), "地图差传参考图片"));
    }

    private void SaveClipboardImage(string path)
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show(this, "剪贴板中没有图片数据！\n[√] 截图后直接复制的图片数据\n[×] 桌面、文件资源管理器上的图片文件");
            return;
        }

        var image = Clipboard.GetImage()!;
        var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
        MessageBox.Show(this, "已保存至【" + path + "】");
    }

    private void BrowseGameFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "请选择Grand Theft Auto V游戏文件夹" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        if (GtaInstall.FindExecutable(dialog.SelectedPath) is null)
        {
            MessageBox.Show(this, "[游戏文件夹路径] 错误：该目录下没有游戏文件！");
            return;
        }

        _host.Settings.Set(Sec.Device, "游戏文件夹路径", dialog.SelectedPath);
        ShowPage(Sec.Device);
    }
}
