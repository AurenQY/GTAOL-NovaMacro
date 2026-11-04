using NovaMacro.Core.Imaging;

namespace NovaMacro.Core.Heists;

public enum YawCommand
{
    None,
    Left,
    Right,
}

public enum PitchCommand
{
    None,
    Climb,
    Dive,
}

public sealed record AutopilotDecision(bool WaypointFound, bool Arrived, YawCommand Yaw, PitchCommand Pitch, double HeadingErrorDegrees, double? AltitudeFraction);

/// <summary>
/// 自动梅杜莎 — reads the minimap (purple waypoint + altimeter line) and decides yaw/pitch inputs.
/// The minimap rectangle is in 2560x1440 design coordinates; the player arrow sits at the
/// horizontal centre and <see cref="PlayerYFraction"/> of the minimap height.
/// </summary>
public static class MedusaAutopilot
{
    public static readonly ScreenRectD MinimapRect = new(53, 1085, 400, 262);
    public const double PlayerYFraction = 0.62;
    public const double YawDeadZoneDegrees = 6;
    public const double ArrivalRadiusFraction = 0.06;

    /// <summary>Altimeter strip at the left edge of the minimap (fraction of minimap width).</summary>
    public const double AltimeterWidthFraction = 0.06;
    public const double AltitudeBandLow = 0.35;
    public const double AltitudeBandHigh = 0.55;

    public static AutopilotDecision Decide(PixelBuffer minimap, int waypointColorRef, int horizonColorRef, int tolerance)
    {
        var waypoint = PixelBuffer.FromColorRef(waypointColorRef);
        var horizon = PixelBuffer.FromColorRef(horizonColorRef);

        var strip = Math.Max(2, (int)(minimap.Width * AltimeterWidthFraction));
        var map = minimap.Crop(strip, 0, minimap.Width - strip, minimap.Height);
        var blip = map.FindColorCentroid(waypoint, tolerance, minCount: 4);

        double? altitude = null;
        var line = minimap.Crop(0, 0, strip, minimap.Height).FindColorCentroid(horizon, tolerance, minCount: 2);
        if (line is not null)
        {
            // 0 = top of the altimeter (high), 1 = bottom (low)
            altitude = line.Value.Y / (double)Math.Max(1, minimap.Height - 1);
        }

        var pitch = PitchCommand.None;
        if (altitude is { } a)
        {
            if (a > AltitudeBandHigh)
            {
                pitch = PitchCommand.Climb;
            }
            else if (a < AltitudeBandLow)
            {
                pitch = PitchCommand.Dive;
            }
        }

        if (blip is null)
        {
            return new AutopilotDecision(false, false, YawCommand.None, pitch, 0, altitude);
        }

        var cx = map.Width / 2.0;
        var cy = map.Height * PlayerYFraction;
        var dx = blip.Value.X - cx;
        var dy = blip.Value.Y - cy;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var arrived = distance <= map.Height * ArrivalRadiusFraction;
        var heading = Math.Atan2(dx, -dy) * 180 / Math.PI;
        var yaw = arrived || Math.Abs(heading) < YawDeadZoneDegrees ? YawCommand.None : heading < 0 ? YawCommand.Left : YawCommand.Right;
        return new AutopilotDecision(true, arrived, yaw, pitch, heading, altitude);
    }
}
