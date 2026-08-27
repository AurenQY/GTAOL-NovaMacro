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

        Assert.Equal("ABC Test", starter.Macro.Name);
        Assert.Equal(
            new KeyboardHotkey(HotkeyKey.F8),
            starter.Macro.Hotkey);
        Assert.Collection(
            starter.Macro.Sequence.Steps,
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
                    new MacroDefinitionCandidate(
                        starter.Macro.Name,
                        starter.Macro.Hotkey,
                        starter.Macro.Sequence)));

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Settings);
        Assert.Empty(validation.Errors);
    }
}
