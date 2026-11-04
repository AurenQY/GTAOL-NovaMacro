using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaMacro.App.Controls;

/// <summary>Tiny helpers for building compact dark WPF layouts in code.</summary>
public static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static TextBlock Text(string text, bool dim = false, double size = 12, FontWeight? weight = null, bool wrap = true) => new()
    {
        Text = text,
        Foreground = Res(dim ? "FgDim" : "Fg"),
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2),
    };

    /// <summary>Upper-case-ish section caption with an accent bar.</summary>
    public static FrameworkElement Header(string text)
    {
        var bar = new Border { Width = 3, Height = 13, CornerRadius = new CornerRadius(1.5), Background = Res("Accent"), Margin = new Thickness(0, 0, 7, 0) };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 10, 6, 3) };
        panel.Children.Add(bar);
        panel.Children.Add(new TextBlock { Text = text, Foreground = Res("FgDim"), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    public static Button Btn(string text, Action onClick, bool accent = false, string? tip = null)
    {
        var b = new Button { Content = text, ToolTip = tip };
        if (accent)
        {
            b.Style = Style("AccentButton");
        }

        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        foreach (var c in children)
        {
            p.Children.Add(c);
        }

        return p;
    }

    public static StackPanel Col(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical };
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
            var caption = Text(title, dim: true, size: 12, weight: FontWeights.SemiBold);
            caption.Margin = new Thickness(2, 0, 2, 4);
            content = Col(caption, child);
        }

        return new Border
        {
            Background = Res("Panel"),
            BorderBrush = Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(4),
            Child = content,
        };
    }

    public static Grid TwoColumns(double leftWidth = 200)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    public static void AddRow(Grid grid, UIElement left, UIElement right)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (row % 2 == 1)
        {
            var stripe = new Border { Background = Res("Panel"), CornerRadius = new CornerRadius(4) };
            Grid.SetRow(stripe, row);
            Grid.SetColumnSpan(stripe, 2);
            grid.Children.Add(stripe);
        }

        if (left is FrameworkElement fl)
        {
            fl.Margin = new Thickness(8, 3, 6, 3);
            fl.VerticalAlignment = VerticalAlignment.Center;
        }

        if (right is FrameworkElement fr)
        {
            fr.VerticalAlignment = VerticalAlignment.Center;
            if (fr.Margin == default)
            {
                fr.Margin = new Thickness(2);
            }
        }

        Grid.SetRow(left, row);
        Grid.SetColumn(left, 0);
        Grid.SetRow(right, row);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
    }

    /// <summary>Lays out label/editor pairs column-major into <paramref name="columns"/> side-by-side columns.</summary>
    public static Grid MultiColumn(IReadOnlyList<(UIElement Label, UIElement Editor)> rows, int columns, double labelWidth)
    {
        columns = Math.Max(1, Math.Min(columns, rows.Count));
        var perColumn = (int)Math.Ceiling(rows.Count / (double)columns);
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        for (var c = 0; c < columns; c++)
        {
            if (c > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < perColumn; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var c = i / perColumn;
            var r = i % perColumn;
            var baseCol = c * 3;
            if (r % 2 == 1)
            {
                var stripe = new Border { Background = Res("Panel"), CornerRadius = new CornerRadius(3) };
                Grid.SetRow(stripe, r);
                Grid.SetColumn(stripe, baseCol);
                Grid.SetColumnSpan(stripe, 2);
                grid.Children.Add(stripe);
            }

            var (label, editor) = rows[i];
            if (label is FrameworkElement fl)
            {
                fl.Margin = new Thickness(6, 1, 4, 1);
                fl.VerticalAlignment = VerticalAlignment.Center;
                if (fl is TextBlock tb)
                {
                    tb.TextWrapping = TextWrapping.NoWrap;
                    tb.TextTrimming = TextTrimming.CharacterEllipsis;
                    tb.ToolTip ??= tb.Text;
                }
            }

            if (editor is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                fe.Margin = new Thickness(1, 1, 2, 1);
            }

            Grid.SetRow(label, r);
            Grid.SetColumn(label, baseCol);
            Grid.SetRow(editor, r);
            Grid.SetColumn(editor, baseCol + 1);
            grid.Children.Add(label);
            grid.Children.Add(editor);
        }

        return grid;
    }

    public static string? Prompt(Window owner, string title, string message, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 320 };
        string? result = null;
        var w = new ThemedWindow
        {
            Title = title,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        var ok = Btn("确定", () =>
        {
            result = box.Text;
            w.DialogResult = true;
        }, accent: true);
        ok.IsDefault = true;
        var cancel = Btn("取消", () => w.DialogResult = false);
        var buttons = Row(ok, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        w.Content = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 12),
            Children = { Text(message), box, buttons },
        };
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return w.ShowDialog() == true ? result : null;
    }
}
