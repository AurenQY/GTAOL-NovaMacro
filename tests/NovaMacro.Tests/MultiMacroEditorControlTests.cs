using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.App.Presets;
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
            Assert.DoesNotContain((HotkeyKey)999, control.HotkeyKeys);
            Assert.Equal(Enum.GetValues<KeyboardKey>(), control.OutputKeys);
            Assert.Contains(KeyboardKey.ArrowUp, control.OutputKeys);
            Assert.DoesNotContain(control.OutputKeys,
                key => IsFunctionKeyToken(key.ToString()));
        });
    }

    [Fact]
    public void EmptySurface_AddFromPresetAddsAndSelectsFirstOrdinaryMacro()
    {
        RunInSta(() =>
        {
            var draft = MultiMacroEditorDraft.From(
                new ApplicationSettings(Array.Empty<MacroDefinition>()));
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            ComboBox selector = Assert.IsType<ComboBox>(
                control.FindName("PresetSelector"));
            Button addFromPreset = Assert.IsType<Button>(
                control.FindName("AddFromPresetButton"));
            BuiltInPresetDefinition preset = Assert.IsType<BuiltInPresetDefinition>(
                selector.SelectedItem);
            ContentControl editor = FindVisualDescendant<ContentControl>(
                control,
                content => content.GetType() == typeof(ContentControl) &&
                    content.ContentTemplate is not null);
            Assert.Equal("ABC Test", preset.ToString());

            addFromPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(control);

            MacroEditorDraft added = Assert.Single(draft.Macros);
            Assert.Same(added, draft.SelectedMacro);
            Assert.Same(added, editor.Content);
            Assert.Equal(preset.Name, added.Name);
            Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, added.Id);
        });
    }

    [Fact]
    public void AddFromPreset_AppendsRepeatedCopiesAndKeepsOrdinaryAddSeparate()
    {
        RunInSta(() =>
        {
            MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings());
            MacroId[] originalIds = draft.Macros.Select(macro => macro.Id).ToArray();
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            Button addMacro = FindVisualDescendant<Button>(
                control,
                button => Equals(button.Content, "Add Macro"));
            Button addFromPreset = Assert.IsType<Button>(
                control.FindName("AddFromPresetButton"));

            addFromPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addFromPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addMacro.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(control);

            Assert.Equal(originalIds, draft.Macros.Take(3).Select(macro => macro.Id));
            Assert.Equal(
                new[] { "ABC Test", "ABC Test", "New Macro" },
                draft.Macros.Skip(3).Select(macro => macro.Name));
            Assert.False(draft.Macros[^1].Enabled);
            Assert.Same(draft.Macros[^1], draft.SelectedMacro);
        });
    }

    [Fact]
    public void AddFromPreset_NoSelectionIsASafeNoOp()
    {
        RunInSta(() =>
        {
            var draft = MultiMacroEditorDraft.From(
                new ApplicationSettings(Array.Empty<MacroDefinition>()));
            var control = new MultiMacroEditorControl(draft);
            Arrange(control);
            ComboBox selector = Assert.IsType<ComboBox>(
                control.FindName("PresetSelector"));
            Button addFromPreset = Assert.IsType<Button>(
                control.FindName("AddFromPresetButton"));
            selector.SelectedItem = null;

            addFromPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Empty(draft.Macros);
            Assert.Null(draft.SelectedMacro);
        });
    }

    [Fact]
    public void PresetActions_FitInsideTheMacroPane()
    {
        RunInSta(() =>
        {
            var control = new MultiMacroEditorControl();
            Arrange(control);
            StackPanel actions = Assert.IsType<StackPanel>(
                control.FindName("MacroActionsPanel"));
            ComboBox selector = Assert.IsType<ComboBox>(
                control.FindName("PresetSelector"));
            Button addFromPreset = Assert.IsType<Button>(
                control.FindName("AddFromPresetButton"));
            Rect selectorBounds = selector.TransformToAncestor(actions)
                .TransformBounds(new Rect(selector.RenderSize));
            Rect buttonBounds = addFromPreset.TransformToAncestor(actions)
                .TransformBounds(new Rect(addFromPreset.RenderSize));

            Assert.True(selectorBounds.Left >= 0);
            Assert.True(buttonBounds.Right <= actions.ActualWidth + 0.01);
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

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(900, 700));
        element.Arrange(new Rect(0, 0, 900, 700));
        element.UpdateLayout();
        DrainDispatcher();
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
