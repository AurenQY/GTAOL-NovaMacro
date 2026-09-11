using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class MultiMacroConfigurationTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void MacroCount_EnforcesZeroThroughThirtyTwoBoundary(
        int count,
        bool expectedValid)
    {
        MacroDefinitionCandidate?[] macros = Enumerable
            .Range(0, count)
            .Select(index => (MacroDefinitionCandidate?)CreateCandidate(
                index,
                enabled: false))
            .ToArray();

        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            new ApplicationSettingsCandidate(macros));

        if (expectedValid)
        {
            ApplicationSettings settings = AssertValid(result);
            Assert.Equal(count, settings.Macros.Count);
        }
        else
        {
            AssertInvalid(
                result,
                (ConfigurationValidationErrorCode.MacroCountOutOfRange, "macros"));
        }
    }

    [Fact]
    public void MacroId_GuidEmptyIsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new MacroId(Guid.Empty));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void DefaultMacroIdCandidate_IsRejectedWithCollectionPath()
    {
        MacroDefinitionCandidate candidate = CreateCandidate(
            index: 0,
            id: default(MacroId));

        AssertInvalid(
            ConfigurationValidator.Validate(
                SettingsCandidate(candidate)),
            (ConfigurationValidationErrorCode.MacroIdEmpty, "macros[0].id"));
    }

    [Fact]
    public void TwoDifferentMacroIds_AreValid()
    {
        MacroDefinitionCandidate first = CreateCandidate(0, enabled: false);
        MacroDefinitionCandidate second = CreateCandidate(1, enabled: false);

        ApplicationSettings settings = AssertValid(
            ConfigurationValidator.Validate(
                SettingsCandidate(first, second)));

        Assert.NotEqual(settings.Macros[0].Id, settings.Macros[1].Id);
    }

    [Fact]
    public void DuplicateMacroId_IsRejectedAtSecondMacroPath()
    {
        MacroId duplicateId = Id(1);
        MacroDefinitionCandidate first = CreateCandidate(
            index: 0,
            id: duplicateId,
            hotkey: Ctrl(HotkeyKey.F8));
        MacroDefinitionCandidate second = CreateCandidate(
            index: 1,
            id: duplicateId,
            hotkey: Ctrl(HotkeyKey.F9));

        AssertInvalid(
            ConfigurationValidator.Validate(
                SettingsCandidate(first, second)),
            (ConfigurationValidationErrorCode.DuplicateMacroId, "macros[1].id"));
    }

    [Fact]
    public void DuplicateMacroId_IsRejectedEvenWhenSecondMacroIsDisabled()
    {
        MacroId duplicateId = Id(1);
        MacroDefinitionCandidate first = CreateCandidate(
            index: 0,
            id: duplicateId,
            enabled: true,
            hotkey: Ctrl(HotkeyKey.F8));
        MacroDefinitionCandidate second = CreateCandidate(
            index: 1,
            id: duplicateId,
            enabled: false,
            hotkey: Ctrl(HotkeyKey.F8));

        AssertInvalid(
            ConfigurationValidator.Validate(
                SettingsCandidate(first, second)),
            (ConfigurationValidationErrorCode.DuplicateMacroId, "macros[1].id"));
    }

    [Fact]
    public void DuplicateNames_WithDifferentIdentityAndHotkeysAreValid()
    {
        MacroDefinitionCandidate first = CreateCandidate(
            index: 0,
            name: "Shared Name",
            hotkey: Ctrl(HotkeyKey.F8));
        MacroDefinitionCandidate second = CreateCandidate(
            index: 1,
            name: "Shared Name",
            hotkey: Ctrl(HotkeyKey.F9));

        ApplicationSettings settings = AssertValid(
            ConfigurationValidator.Validate(
                SettingsCandidate(first, second)));

        Assert.Equal(
            new[] { "Shared Name", "Shared Name" },
            settings.Macros.Select(macro => macro.Name));
        Assert.NotEqual(settings.Macros[0].Id, settings.Macros[1].Id);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public void DuplicateHotkey_UniquenessAppliesOnlyToEnabledMacros(
        bool firstEnabled,
        bool secondEnabled,
        bool expectedValid)
    {
        KeyboardHotkey duplicateHotkey = Ctrl(HotkeyKey.F8);
        MacroDefinitionCandidate first = CreateCandidate(
            index: 0,
            enabled: firstEnabled,
            hotkey: duplicateHotkey);
        MacroDefinitionCandidate second = CreateCandidate(
            index: 1,
            enabled: secondEnabled,
            hotkey: duplicateHotkey);

        ConfigurationValidationResult result = ConfigurationValidator.Validate(
            SettingsCandidate(first, second));

        if (expectedValid)
        {
            ApplicationSettings settings = AssertValid(result);
            Assert.Equal(2, settings.Macros.Count);
        }
        else
        {
            AssertInvalid(
                result,
                (
                    ConfigurationValidationErrorCode.DuplicateEnabledHotkey,
                    "macros[1].hotkey"));
        }
    }

    [Fact]
    public void DisabledMacro_WithInvalidNameIsStillRejected()
    {
        MacroDefinitionCandidate disabled = CreateCandidate(
            index: 0,
            enabled: false,
            name: "   ");

        AssertInvalid(
            ConfigurationValidator.Validate(SettingsCandidate(disabled)),
            (ConfigurationValidationErrorCode.MacroNameRequired, "macros[0].name"));
    }

    [Fact]
    public void InvalidSecondMacro_UsesNestedCollectionAwareStepPath()
    {
        MacroDefinitionCandidate valid = CreateCandidate(
            index: 0,
            hotkey: Ctrl(HotkeyKey.F8));
        MacroDefinitionCandidate invalid = CreateCandidate(
            index: 1,
            hotkey: Ctrl(HotkeyKey.F9),
            sequence: new MacroSequence(
                new MacroStep[]
                {
                    new KeyTapStep(KeyboardKey.B),
                    new DelayStep(TimeSpan.Zero)
                }));

        AssertInvalid(
            ConfigurationValidator.Validate(
                SettingsCandidate(valid, invalid)),
            (
                ConfigurationValidationErrorCode.DelayOutOfRange,
                "macros[1].steps[1].duration"));
    }

    [Fact]
    public void ApplicationSettings_DefensivelyCopiesAndExposesReadOnlyMacros()
    {
        MacroDefinition original = CreateDefinition(0);
        var source = new List<MacroDefinition> { original };
        var settings = new ApplicationSettings(source);

        source.Clear();

        Assert.Same(original, Assert.Single(settings.Macros));
        IList<MacroDefinition> exposed = Assert.IsAssignableFrom<IList<MacroDefinition>>(
            settings.Macros);
        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => exposed.Add(CreateDefinition(1)));
        Assert.Same(original, Assert.Single(settings.Macros));
    }

    [Fact]
    public void ApplicationSettings_RejectsNullEnumerableAndNullElements()
    {
        ArgumentNullException nullEnumerable = Assert.Throws<ArgumentNullException>(
            () => new ApplicationSettings(null!));
        Assert.Equal("macros", nullEnumerable.ParamName);

        ArgumentException nullElement = Assert.Throws<ArgumentException>(
            () => new ApplicationSettings(
                new MacroDefinition[] { CreateDefinition(0), null! }));
        Assert.Equal("macros", nullElement.ParamName);
    }

    [Fact]
    public void Candidate_NullCollectionReturnsClearValidationFailure()
    {
        AssertInvalid(
            ConfigurationValidator.Validate(
                new ApplicationSettingsCandidate(null)),
            (ConfigurationValidationErrorCode.MacrosRequired, "macros"));
    }

    [Fact]
    public void Candidate_NullMacroItemReturnsClearIndexedValidationFailure()
    {
        MacroDefinitionCandidate valid = CreateCandidate(0, enabled: false);

        AssertInvalid(
            ConfigurationValidator.Validate(
                new ApplicationSettingsCandidate(
                    new MacroDefinitionCandidate?[] { valid, null })),
            (ConfigurationValidationErrorCode.MacroRequired, "macros[1]"));
    }

    [Fact]
    public void RequireEnabled_ExactlyOneEnabledMacroReturnsSameMacro()
    {
        MacroDefinition macro = CreateDefinition(0, enabled: true);
        var settings = new ApplicationSettings(new[] { macro });

        MacroDefinition result = SingleMacroCompatibility.RequireEnabled(settings);

        Assert.Same(macro, result);
    }

    [Fact]
    public void RequireEnabled_RejectsZeroMultipleAndSoleDisabledMacro()
    {
        Assert.Throws<InvalidOperationException>(
            () => SingleMacroCompatibility.RequireEnabled(
                new ApplicationSettings(Array.Empty<MacroDefinition>())));
        Assert.Throws<InvalidOperationException>(
            () => SingleMacroCompatibility.RequireEnabled(
                new ApplicationSettings(
                    new[] { CreateDefinition(0), CreateDefinition(1) })));
        Assert.Throws<InvalidOperationException>(
            () => SingleMacroCompatibility.RequireEnabled(
                new ApplicationSettings(
                    new[] { CreateDefinition(0, enabled: false) })));
    }

    [Fact]
    public void RequireEnabled_AcceptsSoleEnabledMacroWithNonLegacyId()
    {
        MacroDefinition macro = CreateDefinition(17, enabled: true);
        Assert.NotEqual(SingleMacroCompatibility.LegacyMacroId, macro.Id);
        var settings = new ApplicationSettings(new[] { macro });

        MacroDefinition result = SingleMacroCompatibility.RequireEnabled(settings);

        Assert.Same(macro, result);
    }

    private static ApplicationSettingsCandidate SettingsCandidate(
        params MacroDefinitionCandidate?[] macros)
    {
        return new ApplicationSettingsCandidate(macros);
    }

    private static MacroDefinitionCandidate CreateCandidate(
        int index,
        MacroId? id = null,
        string? name = null,
        bool enabled = true,
        KeyboardHotkey? hotkey = null,
        MacroSequence? sequence = null)
    {
        return new MacroDefinitionCandidate(
            id ?? Id(index + 1),
            name ?? $"Macro {index}",
            enabled,
            hotkey ?? new KeyboardHotkey(HotkeyKey.F8),
            sequence ?? ValidSequence());
    }

    private static MacroDefinition CreateDefinition(
        int index,
        bool enabled = true)
    {
        return new MacroDefinition(
            Id(index + 100),
            $"Macro {index}",
            enabled,
            new KeyboardHotkey(HotkeyKey.F8),
            ValidSequence());
    }

    private static MacroId Id(int value)
    {
        return new MacroId(new Guid(value, 0, 0, new byte[8]));
    }

    private static KeyboardHotkey Ctrl(HotkeyKey key)
    {
        return new KeyboardHotkey(key, HotkeyModifiers.Control);
    }

    private static MacroSequence ValidSequence()
    {
        return new MacroSequence(
            new MacroStep[] { new KeyTapStep(KeyboardKey.A) });
    }

    private static ApplicationSettings AssertValid(
        ConfigurationValidationResult result)
    {
        Assert.True(result.IsValid);
        ApplicationSettings settings = Assert.IsType<ApplicationSettings>(
            result.Settings);
        Assert.Empty(result.Errors);
        return settings;
    }

    private static void AssertInvalid(
        ConfigurationValidationResult result,
        params (ConfigurationValidationErrorCode Code, string Path)[] expected)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Settings);
        Assert.Equal(
            expected,
            result.Errors.Select(error => (error.Code, error.Path)));
        Assert.All(
            result.Errors,
            error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }
}
