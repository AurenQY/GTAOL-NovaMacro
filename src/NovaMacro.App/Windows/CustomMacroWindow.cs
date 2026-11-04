using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaMacro.App.Controls;
using NovaMacro.App.Services;
using NovaMacro.Core.Input;
using NovaMacro.Core.Scripting;

namespace NovaMacro.App.Windows;

/// <summary>自定义宏 — create, edit, categorise and save script macros.</summary>
public sealed class CustomMacroWindow : ThemedWindow
{
    private const string Syntax =
        "语法（每行一条）：\n" +
        "键 A键 [点击|按下|弹起] [间隔=50] [延迟=10] [次数=1]\n" +
        "游戏 武器射击 [点击|按下|弹起] …   （使用 GTA 中该动作的键位）\n" +
        "鼠标 左键|右键|中键|侧键上|侧键下|滚轮向上|滚轮向下 …\n" +
        "移动 960,540   （游戏窗口内坐标）\n" +
        "等待 500 / 等待 2秒\n" +
        "路径 M,↓4,确认   （菜单路径，同 M菜单布局 语法）\n" +
        "定位 生命值与弹药\n" +
        "// 注释";

    private readonly AppHost _host;
    private readonly ListBox _list = new() { Width = 230, Margin = new Thickness(6) };
    private readonly TextBox _name = new();
    private readonly TextBox _category = new();
    private readonly CheckBox _enabled = new() { Content = Ui.Text("启用") };
    private readonly CheckBox _loop = new() { Content = Ui.Text("循环执行") };
    private readonly CheckBox _unlocked = new() { Content = Ui.Text("锁定时仍可用") };
    private readonly ComboBox _trigger = new() { MinWidth = 120 };
    private readonly Button _hotkey = new() { MinWidth = 180 };
    private readonly TextBox _script = new()
    {
        AcceptsReturn = true,
        VerticalContentAlignment = VerticalAlignment.Top,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
        MinHeight = 260,
    };

    private readonly TextBlock _status = Ui.Text(string.Empty, dim: true);
    private CustomMacroDefinition? _current;
    private Hotkey _currentHotkey;
    private string? _originalPath;

    public CustomMacroWindow(AppHost host)
    {
        _host = host;
        Title = "自定义宏";
        Width = 960;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _trigger.Items.Add("点击");
        _trigger.Items.Add("按住");
        _trigger.Items.Add("开关");
        _hotkey.Click += (_, _) =>
        {
            var hk = HotkeyCaptureWindow.Capture(this, _host, "自定义宏热键");
            if (hk is { } h)
            {
                _currentHotkey = h;
                _hotkey.Content = h.ToString();
            }
        };

        var form = Ui.TwoColumns(110);
        Ui.AddRow(form, Ui.Text("名称"), _name);
        Ui.AddRow(form, Ui.Text("分类"), _category);
        Ui.AddRow(form, Ui.Text("热键"), _hotkey);
        Ui.AddRow(form, Ui.Text("触发方式"), Ui.Row(_trigger, _loop, _enabled, _unlocked));

        var editor = new DockPanel { Margin = new Thickness(6) };
        var top = Ui.Col(form, Ui.Text(Syntax, dim: true, size: 11));
        DockPanel.SetDock(top, Dock.Top);
        editor.Children.Add(top);
        var buttons = Ui.Row(
            Ui.Btn("保存", Save, accent: true),
            Ui.Btn("检查语法", () => Validate()),
            Ui.Btn("删除宏", Delete),
            _status);
        DockPanel.SetDock(buttons, Dock.Bottom);
        editor.Children.Add(buttons);
        editor.Children.Add(_script);

        var left = new DockPanel();
        var leftButtons = Ui.Row(Ui.Btn("新增自定义宏", New), Ui.Btn("刷新", Reload));
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        left.Children.Add(_list);

        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(editor);
        Content = root;

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is ListBoxItem { Tag: CustomMacroDefinition def })
            {
                Load(def);
            }
        };
        Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var def in CustomMacroDefinition.LoadAll(_host.DataFolder, _host.Log.Write))
        {
            var label = (def.Category.Length > 0 ? def.Category + " / " : string.Empty) + def.Name + (def.Enabled ? string.Empty : "（未启用）");
            _list.Items.Add(new ListBoxItem { Content = label, Tag = def, Foreground = Ui.Res(def.Enabled ? "Fg" : "FgDim") });
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
        else
        {
            New();
        }
    }

    private void Load(CustomMacroDefinition def)
    {
        _current = def;
        _originalPath = def.FilePath(_host.DataFolder);
        _name.Text = def.Name;
        _category.Text = def.Category;
        _enabled.IsChecked = def.Enabled;
        _loop.IsChecked = def.Loop;
        _unlocked.IsChecked = def.Unlocked;
        _trigger.SelectedIndex = (int)def.Trigger;
        _currentHotkey = def.Hotkey;
        _hotkey.Content = def.Hotkey.ToString();
        _script.Text = def.Script;
        _status.Text = string.Empty;
    }

    private void New()
    {
        var name = Ui.Prompt(this, "新增自定义宏", "输入新的宏名称。", "新的自定义宏");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Load(new CustomMacroDefinition { Name = name.Trim(), Script = "// 在这里编写宏\n键 E键 点击 间隔=30 延迟=30\n" });
        _originalPath = null;
    }

    private bool Validate()
    {
        try
        {
            var commands = MacroScript.Parse(_script.Text);
            _status.Text = $"语法正确：{commands.Count} 条命令";
            _status.Foreground = Ui.Res("Good");
            return true;
        }
        catch (ScriptParseException ex)
        {
            _status.Text = ex.Message;
            _status.Foreground = Ui.Res("Bad");
            return false;
        }
    }

    private void Save()
    {
        if (!Validate())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            Dialog.Show(this, "名称不能为空");
            return;
        }

        var def = new CustomMacroDefinition
        {
            Name = _name.Text.Trim(),
            Category = string.Join("\\", _category.Text.Split('/', '\\').Select(p => CustomMacroDefinition.SanitizeFileName(p)).Where(p => p.Length > 0 && p != "未命名")),
            Enabled = _enabled.IsChecked == true,
            Loop = _loop.IsChecked == true,
            Unlocked = _unlocked.IsChecked == true,
            Trigger = (CustomTrigger)Math.Max(0, _trigger.SelectedIndex),
            Hotkey = _currentHotkey,
            Script = _script.Text,
        };

        var path = def.FilePath(_host.DataFolder);
        if (!string.Equals(path, _originalPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
            !Dialog.Confirm(this, "发现有重复的文件，是否覆盖？", "发现文件重复！"))
        {
            return;
        }

        def.Save(_host.DataFolder);
        if (_originalPath is not null && !string.Equals(path, _originalPath, StringComparison.OrdinalIgnoreCase) && File.Exists(_originalPath))
        {
            File.Delete(_originalPath);
        }

        _host.ReloadCustomMacros();
        _status.Text = "已保存至【" + path + "】";
        _status.Foreground = Ui.Res("Good");
        _originalPath = path;
        var index = _list.SelectedIndex;
        Reload();
        _list.SelectedIndex = Math.Min(Math.Max(0, index), _list.Items.Count - 1);
    }

    private void Delete()
    {
        if (_current is null || _originalPath is null)
        {
            return;
        }

        if (!Dialog.Confirm(this, $"确定删除自定义宏？\n【{_current.Name}】", "删除宏！"))
        {
            return;
        }

        if (File.Exists(_originalPath))
        {
            File.Delete(_originalPath);
        }

        _host.ReloadCustomMacros();
        Reload();
    }
}
