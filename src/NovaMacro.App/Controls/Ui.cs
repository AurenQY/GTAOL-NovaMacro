using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaMacro.App.Controls;

/// <summary>Tiny helpers for building WPF layouts in code.</summary>
public static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static TextBlock Text(string text, bool dim = false, double size = 13, FontWeight? weight = null) => new()
    {
        Text = text,
        Foreground = Res(dim ? "FgDim" : "Fg"),
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(3),
    };

    public static Button Btn(string text, Action onClick, bool accent = false, string? tip = null)
    {
        var b = new Button { Content = text, ToolTip = tip };
        if (accent)
        {
            b.Style = (Style)Application.Current.Resources["AccentButton"];
        }

        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        foreach (var c in children)
        {
            p.Children.Add(c);
        }

        return p;
    }

    public static StackPanel Col(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2) };
        foreach (var c in children)
        {
            p.Children.Add(c);
        }

        return p;
    }

    public static Border Card(UIElement child, string? title = null)
    {
        var content = child;
        if (title is not null)
        {
            content = Col(Text(title, dim: true, size: 12, weight: FontWeights.SemiBold), child);
        }

        return new Border
        {
            Background = Res("Panel"),
            BorderBrush = Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(5),
            Child = content,
        };
    }

    public static Grid TwoColumns(double leftWidth = 200)
    {
        var g = new Grid { Margin = new Thickness(2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    public static void AddRow(Grid grid, UIElement left, UIElement right)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(left, row);
        Grid.SetColumn(left, 0);
        Grid.SetRow(right, row);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
    }

    public static string? Prompt(Window owner, string title, string message, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 320 };
        string? result = null;
        var w = new Window
        {
            Title = title,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        var ok = Btn("确定", () =>
        {
            result = box.Text;
            w.DialogResult = true;
        }, accent: true);
        ok.IsDefault = true;
        w.Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children = { Text(message), box, Row(ok, Btn("取消", () => w.DialogResult = false)) },
        };
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return w.ShowDialog() == true ? result : null;
    }
}
