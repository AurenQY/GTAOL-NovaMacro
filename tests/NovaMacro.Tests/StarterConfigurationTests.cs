using NovaMacro.App.Configuration;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class StarterConfigurationTests
{
    [Fact]
    public void Default_HasExpectedSemanticsAndPassesValidation()
    {
        ApplicationSettings starter = StarterConfiguration.Default;
        MacroDefinition macro = Assert.Single(starter.Macros);

        Assert.Equal(SingleMacroCompatibility.LegacyMacroId, macro.Id);
        Assert.True(macro.Enabled);
        Assert.Equal("ABC Test", macro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F8),
            macro.GetKeyboardHotkey());
        Assert.Collection(
            macro.Sequence.Steps,
            step => Assert.Equal(new KeyTapStep(KeyboardKey.A), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.B), step),
            step => Assert.Equal(
                new DelayStep(TimeSpan.FromMilliseconds(150)),
                step),
            step => Assert.Equal(new KeyTapStep(KeyboardKey.C), step));

        ConfigurationValidationResult validation =
            ConfigurationValidator.Validate(
                new ApplicationSettingsCandidate(
                    new MacroDefinitionCandidate?[]
                    {
                        new(
                            macro.Id,
                            macro.Name,
                            macro.Enabled,
                            macro.GetKeyboardHotkey(),
                            macro.Sequence)
                    }));

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Settings);
        Assert.Empty(validation.Errors);
    }
}
