using NovaMacro.Core.Runtime;
using NovaMacro.Core.Scripting;

namespace NovaMacro.Core.Features;

public static class FeatureFactory
{
    /// <summary>Creates every built-in feature (custom macros are added separately).</summary>
    public static IReadOnlyList<MacroFeature> CreateBuiltIn() => new MacroFeature[]
    {
        new WeaponJamFeature(),
        new BuyAmmoFeature(),
        new MenuSnackFeature(),
        new VehicleControlFeature(),
        new ServiceVehicleFeature(),
        new QuickPerspectiveFeature(),
        new PhoneCallFeature(),
        new AutoClickFeature(),
        new WheelSnackFeature(),
        new WeaponSwapSpeedFeature(),
        new AdSpeedFeature(),
        new InstantHoverFeature(),
        new SwitchSessionFeature(),
        new SprintToggleFeature(),
        new DelayClimbFeature(),
        new EnterAlternativeFeature(),
        new AfkFeature(),
        new CasinoHeistFeature(),
        new CayoPericoFeature(),
        new MapTeleportFeature(),
        new JobTeleportFeature(),
        new ScreenshotFeature(),
        new CrosshairFeature(),
        new ToolHotkeyFeature(FeatureId.KillGame),
        new ToolHotkeyFeature(FeatureId.NetworkCut),
        new ToolHotkeyFeature(FeatureId.SoloSession),
        new ToolHotkeyFeature(FeatureId.SessionLock),
        new ToolHotkeyFeature(FeatureId.SpeedLimit),
    };

    public static IReadOnlyList<MacroFeature> CreateCustom(IEnumerable<CustomMacroDefinition> definitions) =>
        definitions.Select(d => (MacroFeature)new CustomMacroFeature(d)).ToArray();
}
