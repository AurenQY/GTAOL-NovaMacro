using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MultiMacroEditorControlTests
{
    [Fact]
    public void EmptySurface_AddAndDeleteFollowDraftSelectionRules()
    {
        RunInSta(() =>
        {
            var draft = MultiMacroEditorDraft.From(
                new ApplicationSettings(Array.Empty<MacroDefinition>()));
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            Button deleteButton = Assert.IsType<Button>(
                control.FindName("DeleteMacroButton"));
            ListBox list = FindVisualDescendant<ListBox>(control);
            ContentControl editor = FindVisualDescendant<ContentControl>(
                control,
                content => content.GetType() == typeof(ContentControl) &&
                    content.ContentTemplate is not null);

            Assert.Empty(list.Items);
            Assert.Null(draft.SelectedMacro);
            Assert.False(deleteButton.IsEnabled);
            Assert.Null(editor.Content);

            MacroEditorDraft first = draft.AddMacro();
            Arrange(control);

            Assert.Single(list.Items);
            Assert.Same(first, draft.SelectedMacro);
            Assert.Same(first, list.SelectedItem);
            Assert.Same(first, editor.Content);
            Assert.True(deleteButton.IsEnabled);

            deleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(control);

            Assert.Empty(draft.Macros);
            Assert.Empty(list.Items);
            Assert.Null(draft.SelectedMacro);
            Assert.Null(editor.Content);
            Assert.False(deleteButton.IsEnabled);
        });
    }

    [Fact]
    public void SurfaceSelectionAndDeleteStaySynchronizedWithDraft()
    {
        RunInSta(() =>
        {
            MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings());
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            ListBox list = FindVisualDescendant<ListBox>(control);
            ContentControl editor = FindVisualDescendant<ContentControl>(
                control,
                content => content.GetType() == typeof(ContentControl) &&
                    content.ContentTemplate is not null);
            Button deleteButton = Assert.IsType<Button>(
                control.FindName("DeleteMacroButton"));
            MacroEditorDraft second = draft.Macros[1];
            MacroEditorDraft third = draft.Macros[2];

            list.SelectedItem = second;
            BindingExpression binding = Assert.IsType<BindingExpression>(
                list.GetBindingExpression(ListBox.SelectedItemProperty));
            binding.UpdateSource();
            Arrange(control);

            Assert.Same(second, draft.SelectedMacro);
            Assert.Same(second, editor.Content);
            second.Name = "Edited second";

            list.SelectedItem = third;
            binding.UpdateSource();
            Arrange(control);

            Assert.Same(third, draft.SelectedMacro);
            Assert.Same(third, editor.Content);
            Assert.Equal("Edited second", second.Name);

            list.SelectedItem = second;
            binding.UpdateSource();
            deleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(control);

            Assert.Equal(new[] { "First", "Third" },
                draft.Macros.Select(macro => macro.Name));
            Assert.Same(third, draft.SelectedMacro);
            Assert.Same(third, editor.Content);
            Assert.DoesNotContain(second, draft.Macros);
        });
    }

    [Fact]
    public void SurfaceEnabledBindingAndValidationDisplayUseTheSelectedDraft()
    {
        RunInSta(() =>
        {
            MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings());
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            ListBox list = FindVisualDescendant<ListBox>(control);
            ItemsControl validationList = Assert.IsType<ItemsControl>(
                control.FindName("ValidationErrorsList"));
            MacroEditorDraft second = draft.Macros[1];

            list.SelectedItem = second;
            Assert.IsType<BindingExpression>(
                list.GetBindingExpression(ListBox.SelectedItemProperty)).UpdateSource();
            Arrange(control);

            CheckBox enabled = FindVisualDescendant<CheckBox>(
                control,
                checkBox => Equals(
                    checkBox.Content,
                    "Register this macro when integrated"));
            Assert.False(enabled.IsChecked);
            enabled.IsChecked = true;
            Assert.IsType<BindingExpression>(
                enabled.GetBindingExpression(ToggleButton.IsCheckedProperty)).UpdateSource();

            Assert.True(second.Enabled);
            Assert.Equal(second.Id, draft.Macros[1].Id);

            second.Name = "   ";
            Arrange(control);

            Assert.Same(second, draft.SelectedMacro);
            Assert.Equal(
                new[] { "macros[1].name: Macro name is required." },
                validationList.Items.Cast<string>());
        });
    }

    [Fact]
    public void SurfaceExposesOnlyExistingHotkeyAndOutputVocabularies()
    {
        RunInSta(() =>
        {
            var control = new MultiMacroEditorControl();

            Assert.Equal(Enum.GetValues<HotkeyKey>(), control.HotkeyKeys);
            Assert.Contains(HotkeyKey.A, control.HotkeyKeys);
            Assert.Contains(HotkeyKey.F11, control.HotkeyKeys);
            Assert.Contains(HotkeyKey.Numpad0, control.HotkeyKeys);
            Assert.Contains(HotkeyKey.NumpadDivide, control.HotkeyKeys);
            Assert.DoesNotContain(
                control.HotkeyKeys,
                key => key.ToString() == "NumpadEnter");
            Assert.DoesNotContain((HotkeyKey)999, control.HotkeyKeys);
            Assert.Equal(Enum.GetValues<KeyboardKey>(), control.OutputKeys);
            Assert.Contains(KeyboardKey.ArrowUp, control.OutputKeys);
            Assert.Contains(KeyboardKey.NumpadEnter, control.OutputKeys);
            Assert.DoesNotContain(control.OutputKeys,
                key => IsFunctionKeyToken(key.ToString()));
            Assert.Equal(
                new[] { MacroTriggerEditorKind.Keyboard, MacroTriggerEditorKind.MouseButton },
                control.TriggerKinds);
            Assert.Equal(
                new[] { MouseButton.XButton1, MouseButton.XButton2 },
                control.MouseTriggerButtons);
            Assert.Equal(Enum.GetValues<MacroExecutionMode>(), control.ExecutionModes);
        });
    }

    [Fact]
    public void SurfaceExposesTriggerExecutionAndLeftMouseTapControls()
    {
        RunInSta(() =>
        {
            var control = new MultiMacroEditorControl();
            Arrange(control);

            Assert.Contains(
                FindVisualDescendants<TextBlock>(control),
                text => Equals(text.Text, "Trigger"));
            Assert.Contains(
                FindVisualDescendants<TextBlock>(control),
                text => Equals(text.Text, "Execution"));
            Assert.Contains(
                FindVisualDescendants<Button>(control),
                button => Equals(button.Content, "+ Left Mouse Tap"));
        });
    }

    [Fact]
    public void MainSurface_DoesNotExposePresetInsertionControls()
    {
        RunInSta(() =>
        {
            var draft = MultiMacroEditorDraft.From(
                new ApplicationSettings(Array.Empty<MacroDefinition>()));
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            StackPanel actions = Assert.IsType<StackPanel>(
                control.FindName("MacroActionsPanel"));

            Assert.Null(control.FindName("PresetSelector"));
            Assert.Null(control.FindName("AddFromPresetButton"));
            Assert.Equal(new[] { "Add Macro", "Delete Macro" },
                FindVisualDescendants<Button>(actions)
                    .Select(button => button.Content));
            Assert.DoesNotContain(FindVisualDescendants<Button>(control),
                button => Equals(button.Content, "Add from preset"));
        });
    }

    [Fact]
    public void FactoryMacroSurface_HasNoOriginOrTemplateBadges()
    {
        RunInSta(() =>
        {
            var control = new MultiMacroEditorControl(
                MultiMacroEditorDraft.From(StarterConfiguration.Default));
            Arrange(control);

            Assert.DoesNotContain(FindVisualDescendants<TextBlock>(control),
                text => text.Text is "Built-in" or "Factory" or "Preset" or
                    "Template");
        });
    }

    [Fact]
    public void FirstRunFactoryMacros_AppearDirectlyAndRemainOrdinaryEditableDeletable()
    {
        RunInSta(() =>
        {
            MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(
                StarterConfiguration.Default);
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            ListBox list = FindVisualDescendant<ListBox>(control);
            ContentControl editor = FindVisualDescendant<ContentControl>(
                control,
                content => content.GetType() == typeof(ContentControl) &&
                    content.ContentTemplate is not null);
            Button delete = Assert.IsType<Button>(
                control.FindName("DeleteMacroButton"));
            Button add = FindVisualDescendant<Button>(
                control,
                button => Equals(button.Content, "Add Macro"));

            Assert.Equal(
                new[]
                {
                    "Buy All Ammo",
                    "Drop Bull Shark",
                    "Request Sparrow",
                    "Heavy Weapon Rapid Fire"
                },
                draft.Macros.Select(macro => macro.Name));
            Assert.Equal(4, list.Items.Count);
            Assert.Same(draft.Macros[0], draft.SelectedMacro);
            Assert.Same(draft.Macros[0], list.SelectedItem);
            Assert.Same(draft.Macros[0], editor.Content);

            list.SelectedItem = draft.Macros[1];
            Assert.IsType<BindingExpression>(
                list.GetBindingExpression(ListBox.SelectedItemProperty)).UpdateSource();
            Arrange(control);
            draft.SelectedMacro!.Name = "Edited Bull Shark";

            Assert.Same(draft.Macros[1], editor.Content);
            Assert.Equal("Edited Bull Shark", draft.Macros[1].Name);

            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(control);

            Assert.Equal(3, draft.Macros.Count);
            Assert.DoesNotContain(draft.Macros,
                macro => macro.Name == "Drop Bull Shark");
            Assert.Equal(3, list.Items.Count);

            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(control);

            MacroEditorDraft added = draft.Macros[^1];
            Assert.Equal("New Macro", added.Name);
            Assert.False(added.Enabled);
            Assert.Same(added, draft.SelectedMacro);
        });
    }

    [Fact]
    public void CompactLayout_KeepsPrimaryEditingControlsAvailableAcrossSupportedSizes()
    {
        RunInSta(() =>
        {
            foreach ((double width, double height) in new[]
            {
                (728d, 380d),
                (868d, 520d),
                (1200d, 760d)
            })
            {
                var control = new MultiMacroEditorControl(
                    MultiMacroEditorDraft.From(StarterConfiguration.Default));
                Arrange(control, width, height);

                ListBox list = FindVisualDescendant<ListBox>(control);
                Button add = FindVisualDescendant<Button>(control,
                    button => Equals(button.Content, "Add Macro"));
                Button delete = FindVisualDescendant<Button>(control,
                    button => Equals(button.Content, "Delete Macro"));
                Button addKeyTap = FindVisualDescendant<Button>(control,
                    button => Equals(button.Content, "+ Key Tap"));
                Button addMouseTap = FindVisualDescendant<Button>(control,
                    button => Equals(button.Content, "+ Left Mouse Tap"));
                Button addDelay = FindVisualDescendant<Button>(control,
                    button => Equals(button.Content, "+ Delay"));

                Assert.True(list.ActualHeight > 0);
                AssertInside(control, add);
                AssertInside(control, delete);
                AssertInside(control, addKeyTap);
                AssertInside(control, addMouseTap);
                AssertInside(control, addDelay);
            }
        });
    }

    private static ApplicationSettings Settings()
    {
        return new ApplicationSettings(new[]
        {
            Definition("11111111-1111-1111-1111-111111111111", "First", true, HotkeyKey.F8),
            Definition("22222222-2222-2222-2222-222222222222", "Second", false, HotkeyKey.F9),
            Definition("33333333-3333-3333-3333-333333333333", "Third", false, HotkeyKey.F10)
        });
    }

    private static MacroDefinition Definition(
        string id,
        string name,
        bool enabled,
        HotkeyKey hotkey)
    {
        return new MacroDefinition(
            new MacroId(new Guid(id)),
            name,
            enabled,
            new KeyboardHotkey(hotkey),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }));
    }

    private static void Arrange(
        FrameworkElement element,
        double width = 900,
        double height = 700)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        DrainDispatcher();
    }

    private static void AssertInside(
        FrameworkElement root,
        FrameworkElement element)
    {
        Rect bounds = element.TransformToAncestor(root).TransformBounds(
            new Rect(element.RenderSize));

        Assert.True(bounds.Left >= -0.01);
        Assert.True(bounds.Top >= -0.01);
        Assert.True(bounds.Right <= root.ActualWidth + 0.01);
        Assert.True(bounds.Bottom <= root.ActualHeight + 0.01);
        Assert.True(element.ActualWidth > 0);
        Assert.True(element.ActualHeight > 0);
    }

    private static bool IsFunctionKeyToken(string token)
    {
        return token.Length > 1 &&
            token[0] == 'F' &&
            int.TryParse(token[1..], out _);
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T FindVisualDescendant<T>(
        DependencyObject root,
        Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T found && (predicate is null || predicate(found)))
            {
                return found;
            }

            try
            {
                return FindVisualDescendant(child, predicate);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException(
            $"Visual descendant '{typeof(T).Name}' was not found.");
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T found)
            {
                yield return found;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
