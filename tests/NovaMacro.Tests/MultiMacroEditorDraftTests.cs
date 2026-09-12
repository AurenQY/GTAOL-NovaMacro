using NovaMacro.App.Configuration;
using NovaMacro.App.Editor;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MultiMacroEditorDraftTests
{
    [Fact]
    public void From_LoadsOrderedMacrosAndSelectsFirst()
    {
        ApplicationSettings settings = Settings(1, 2, 3);

        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(settings);

        Assert.Equal(
            settings.Macros.Select(macro => macro.Id),
            draft.Macros.Select(macro => macro.Id));
        Assert.Equal(
            settings.Macros.Select(macro => macro.Enabled),
            draft.Macros.Select(macro => macro.Enabled));
        Assert.Same(draft.Macros[0], draft.SelectedMacro);
    }

    [Fact]
    public void EditingSelectedMacro_ChangesOnlyThatMacro()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2, 3));
        MacroEditorDraft first = draft.Macros[0];
        MacroEditorDraft second = draft.Macros[1];
        MacroEditorDraft third = draft.Macros[2];

        draft.Select(second);
        second.Name = "Edited";
        second.HotkeyKey = HotkeyKey.F10;
        second.AddDelay();

        Assert.Equal("Macro 1", first.Name);
        Assert.Equal(HotkeyKey.F8, first.HotkeyKey);
        Assert.Single(first.Steps);
        Assert.Equal("Edited", second.Name);
        Assert.Equal(HotkeyKey.F10, second.HotkeyKey);
        Assert.Equal(2, second.Steps.Count);
        Assert.Equal("Macro 3", third.Name);
        Assert.Equal(HotkeyKey.F10, third.HotkeyKey);
        Assert.Single(third.Steps);
    }

    [Fact]
    public void Select_AllowsSwitchingAwayFromInvalidDraft()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2));
        MacroEditorDraft first = draft.Macros[0];
        MacroEditorDraft second = draft.Macros[1];

        first.AddDelay();
        first.Steps[1].MillisecondsText = "invalid";

        draft.Select(second);

        Assert.Same(second, draft.SelectedMacro);
        Assert.Equal("invalid", first.Steps[1].MillisecondsText);
        Assert.False(draft.Validate().IsValid);
    }

    [Fact]
    public void AddMacro_GeneratesOneNewNonLegacyIdAndSelectsIt()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2));
        MacroId[] originalIds = draft.Macros.Select(macro => macro.Id).ToArray();

        MacroEditorDraft added = draft.AddMacro();
        MacroId addedId = added.Id;
        added.Name = "Edited added macro";
        added.Enabled = true;

        Assert.Equal(3, draft.Macros.Count);
        Assert.Same(added, draft.SelectedMacro);
        Assert.False(addedId.IsEmpty);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, addedId);
        Assert.DoesNotContain(addedId, originalIds);
        Assert.Equal(originalIds, draft.Macros.Take(2).Select(macro => macro.Id));
        Assert.Equal(addedId, added.Id);
    }

    [Fact]
    public void DeleteSelectedMiddleMacro_PreservesOrderAndSelectsNext()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2, 3));
        MacroEditorDraft middle = draft.Macros[1];
        MacroId firstId = draft.Macros[0].Id;
        MacroId thirdId = draft.Macros[2].Id;

        draft.Select(middle);

        Assert.True(draft.DeleteSelectedMacro());
        Assert.Equal(new[] { firstId, thirdId }, draft.Macros.Select(macro => macro.Id));
        Assert.Same(draft.Macros[1], draft.SelectedMacro);
    }

    [Fact]
    public void DeleteFinalMacro_AllowsEmptyCollectionWithoutStarterInsertion()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1));

        Assert.True(draft.DeleteSelectedMacro());

        Assert.Empty(draft.Macros);
        Assert.Null(draft.SelectedMacro);
        Assert.False(draft.HasMacros);
        Assert.False(draft.DeleteSelectedMacro());
        Assert.True(draft.Validate().IsValid);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void Validate_UsesEnabledHotkeyConflictMatrix(
        bool firstEnabled,
        bool secondEnabled,
        bool isValid)
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2));
        draft.Macros[0].Enabled = firstEnabled;
        draft.Macros[1].Enabled = secondEnabled;
        draft.Macros[1].HotkeyKey = draft.Macros[0].HotkeyKey;

        MacroEditorValidationResult result = draft.Validate();

        Assert.Equal(isValid, result.IsValid);
        if (!isValid)
        {
            ConfigurationValidationError error = Assert.Single(
                result.ConfigurationErrors);
            Assert.Equal(ConfigurationValidationErrorCode.DuplicateEnabledHotkey, error.Code);
            Assert.Equal("macros[1].hotkey", error.Path);
        }
    }

    [Fact]
    public void TryBuildSettings_PreservesExactIdsOrderAndContent()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2, 3));
        draft.Select(draft.Macros[1]);
        draft.SelectedMacro!.Name = "Second edited";
        draft.SelectedMacro.AddDelay();

        bool built = draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation);

        Assert.True(built);
        Assert.True(validation.IsValid);
        ApplicationSettings result = Assert.IsType<ApplicationSettings>(settings);
        Assert.Equal(
            draft.Macros.Select(macro => macro.Id),
            result.Macros.Select(macro => macro.Id));
        Assert.Equal(
            new[] { "Macro 1", "Second edited", "Macro 3" },
            result.Macros.Select(macro => macro.Name));
        Assert.Equal(2, result.Macros[1].Sequence.Steps.Count);
    }

    [Fact]
    public void TryBuildSettings_InvalidCollectionReturnsNoPartialSettings()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2));
        draft.Macros[0].Name = "   ";

        bool built = draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation);

        Assert.False(built);
        Assert.Null(settings);
        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.ConfigurationErrors,
            error => error.Path == "macros[0].name");
    }

    [Fact]
    public void From_LegacyMacroPreservesLegacyIdDuringEditing()
    {
        var settings = new ApplicationSettings(
            new[]
            {
                Definition(
                    SingleMacroCompatibility.LegacyMacroId,
                    "Legacy",
                    enabled: true,
                    HotkeyKey.F8)
            });

        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(settings);
        draft.SelectedMacro!.Name = "Legacy edited";

        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? rebuilt,
            out _));
        Assert.Equal(
            SingleMacroCompatibility.LegacyMacroId,
            Assert.Single(rebuilt!.Macros).Id);
    }

    [Fact]
    public void AddMacro_UsesDisabledNonLegacyDefaultAndKeepsItsIdentityThroughBuild()
    {
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        MacroEditorDraft added = draft.AddMacro();
        MacroId id = added.Id;

        Assert.False(added.Enabled);
        Assert.Equal("New Macro", added.Name);
        Assert.Equal(HotkeyKey.F8, added.HotkeyKey);
        Assert.Equal(new MacroStepEditorKind[] { MacroStepEditorKind.KeyTap },
            added.Steps.Select(step => step.Kind));
        Assert.Equal(KeyboardKey.A, Assert.Single(added.Steps).Key);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, id);

        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));
        Assert.True(validation.IsValid);
        Assert.Equal(id, Assert.Single(settings!.Macros).Id);
    }

    [Fact]
    public void AddAfterLegacyEdit_DoesNotReuseLegacyIdentity()
    {
        var draft = MultiMacroEditorDraft.From(new ApplicationSettings(new[]
        {
            Definition(
                SingleMacroCompatibility.LegacyMacroId,
                "Legacy",
                enabled: true,
                HotkeyKey.F8)
        }));

        draft.SelectedMacro!.Name = "Legacy edited";
        MacroEditorDraft added = draft.AddMacro();

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, draft.Macros[0].Id);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, added.Id);
        Assert.NotEqual(draft.Macros[0].Id, added.Id);
    }

    [Fact]
    public void Validate_Allows32DraftsButReports33WithoutTruncation()
    {
        var draft = MultiMacroEditorDraft.From(
            new ApplicationSettings(Array.Empty<MacroDefinition>()));

        for (int index = 0; index < 32; index++)
        {
            draft.AddMacro();
        }

        Assert.True(draft.Validate().IsValid);
        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? thirtyTwo,
            out _));
        Assert.Equal(32, thirtyTwo!.Macros.Count);

        MacroEditorDraft thirtyThird = draft.AddMacro();
        bool built = draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation);

        Assert.False(built);
        Assert.Null(settings);
        Assert.Equal(33, draft.Macros.Count);
        Assert.Same(thirtyThird, draft.SelectedMacro);
        ConfigurationValidationError error = Assert.Single(
            validation.ConfigurationErrors);
        Assert.Equal(ConfigurationValidationErrorCode.MacroCountOutOfRange, error.Code);
        Assert.Equal("macros", error.Path);
    }

    [Fact]
    public void TryBuildSettings_InvalidCollectionRetainsEveryDraftAndOrder()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2, 3));
        MacroId[] beforeIds = draft.Macros.Select(macro => macro.Id).ToArray();
        string[] beforeNames = draft.Macros.Select(macro => macro.Name).ToArray();
        draft.Macros[1].Name = "   ";

        Assert.False(draft.TryBuildSettings(
            out ApplicationSettings? settings,
            out MacroEditorValidationResult validation));

        Assert.Null(settings);
        Assert.Equal(beforeIds, draft.Macros.Select(macro => macro.Id));
        Assert.Equal(new[] { beforeNames[0], "   ", beforeNames[2] },
            draft.Macros.Select(macro => macro.Name));
        Assert.Contains(validation.ConfigurationErrors,
            error => error.Path == "macros[1].name");
    }

    [Fact]
    public void DeleteSelectedFinalItem_SelectsPreviousWithoutChangingItsContent()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2));
        MacroEditorDraft first = draft.Macros[0];
        MacroEditorDraft last = draft.Macros[1];
        MacroId firstId = first.Id;
        string firstName = first.Name;

        draft.Select(last);

        Assert.True(draft.DeleteSelectedMacro());
        Assert.Single(draft.Macros);
        Assert.Same(first, draft.SelectedMacro);
        Assert.Equal(firstId, first.Id);
        Assert.Equal(firstName, first.Name);
    }

    [Fact]
    public void FromThenBuild_PreservesFullMultiMacroContentAndDeliberateOrder()
    {
        var original = new ApplicationSettings(new[]
        {
            new MacroDefinition(
                Id(30), "Zulu", false,
                new KeyboardHotkey(HotkeyKey.F11, HotkeyModifiers.Shift),
                new MacroSequence(new MacroStep[]
                {
                    new KeyTapStep(KeyboardKey.Digit0),
                    new DelayStep(TimeSpan.FromMilliseconds(150)),
                    new KeyTapStep(KeyboardKey.ArrowUp)
                })),
            new MacroDefinition(
                Id(10), "Alpha", true,
                new KeyboardHotkey(HotkeyKey.F1),
                new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.Enter) })),
            new MacroDefinition(
                Id(20), "Middle", false,
                new KeyboardHotkey(HotkeyKey.B, HotkeyModifiers.Control),
                new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.Space) }))
        });

        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(original);

        Assert.True(draft.TryBuildSettings(
            out ApplicationSettings? rebuilt,
            out _));
        Assert.Equal(original.Macros.Select(macro => macro.Id),
            rebuilt!.Macros.Select(macro => macro.Id));
        Assert.Equal(original.Macros.Select(macro => macro.Name),
            rebuilt.Macros.Select(macro => macro.Name));
        Assert.Equal(original.Macros.Select(macro => macro.Enabled),
            rebuilt.Macros.Select(macro => macro.Enabled));
        Assert.Equal(original.Macros.Select(macro => macro.Hotkey),
            rebuilt.Macros.Select(macro => macro.Hotkey));

        for (int index = 0; index < original.Macros.Count; index++)
        {
            Assert.Equal(
                original.Macros[index].Sequence.Steps,
                rebuilt.Macros[index].Sequence.Steps);
        }
    }

    [Fact]
    public void ValidationDisplay_IdentifiesNonSelectedInvalidMacroWithPathAndMessage()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2));
        MacroEditorDraft first = draft.Macros[0];
        MacroEditorDraft second = draft.Macros[1];
        second.Name = "   ";

        MacroEditorValidationResult validation = draft.Validate();
        IReadOnlyList<string> messages =
            MultiMacroEditorControl.FormatValidationMessages(validation);

        Assert.Same(first, draft.SelectedMacro);
        Assert.Equal(
            "macros[1].name: Macro name is required.",
            Assert.Single(messages));
    }

    [Fact]
    public void ValidationDisplay_PreservesMultipleIndexedErrorsInOrder()
    {
        MultiMacroEditorDraft draft = MultiMacroEditorDraft.From(Settings(1, 2, 3));
        draft.Macros[1].Name = "   ";
        draft.Macros[2].Name = "\t";

        IReadOnlyList<string> messages =
            MultiMacroEditorControl.FormatValidationMessages(draft.Validate());

        Assert.Equal(
            new[]
            {
                "macros[1].name: Macro name is required.",
                "macros[2].name: Macro name is required."
            },
            messages);
    }

    [Fact]
    public void ValidationDisplay_ShowsPathlessMessageWithoutPrefix()
    {
        MacroEditorValidationResult validation =
            MacroEditorValidationResult.InvalidParse(
                new[] { new EditorParseError(string.Empty, "General issue") });

        IReadOnlyList<string> messages =
            MultiMacroEditorControl.FormatValidationMessages(validation);

        Assert.Equal("General issue", Assert.Single(messages));
    }

    [Fact]
    public void ValidationDisplay_ShowsNullPathMessageWithoutPrefix()
    {
        string message = MultiMacroEditorControl.FormatValidationMessage(
            null,
            "Example error.");

        Assert.Equal("Example error.", message);
    }

    [Fact]
    public void ValidationDisplay_ShowsWhitespacePathMessageWithoutPrefix()
    {
        string message = MultiMacroEditorControl.FormatValidationMessage(
            " \t ",
            "Example error.");

        Assert.Equal("Example error.", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ValidationDisplay_ShowsBlankPathVariantsWithoutPrefix(string path)
    {
        Assert.Equal(
            "Example error.",
            MultiMacroEditorControl.FormatValidationMessage(path, "Example error."));
    }

    [Fact]
    public void WpfSurface_DefaultConstructionRequiresNoSettingsOrRuntime()
    {
        Exception? exception = null;
        MultiMacroEditorControl? control = null;
        bool hasDataContext = false;
        var thread = new Thread(() =>
        {
            try
            {
                control = new MultiMacroEditorControl();
                hasDataContext = control.DataContext is not null;
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.NotNull(control);
        Assert.True(hasDataContext);
    }

    private static ApplicationSettings Settings(params int[] ids)
    {
        return new ApplicationSettings(
            ids.Select((id, index) => Definition(
                Id(id),
                $"Macro {id}",
                enabled: index != 1,
                (HotkeyKey)((int)HotkeyKey.F8 + index))));
    }

    private static MacroDefinition Definition(
        MacroId id,
        string name,
        bool enabled,
        HotkeyKey hotkey)
    {
        return new MacroDefinition(
            id,
            name,
            enabled,
            new KeyboardHotkey(hotkey),
            new MacroSequence(new MacroStep[] { new KeyTapStep(KeyboardKey.A) }));
    }

    private static MacroId Id(int value)
    {
        return new MacroId(new Guid(value, 0, 0, new byte[8]));
    }
}
