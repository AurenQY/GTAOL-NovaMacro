using NovaMacro.Core.Heists;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Input;

namespace NovaMacro.Core.Tests;

public class ImagingTests
{
    private static PixelBuffer Pattern(int w, int h, int seed)
    {
        var rnd = new Random(seed);
        var p = new PixelBuffer(w, h);
        for (var i = 0; i < p.Pixels.Length; i++)
        {
            var v = rnd.Next(256);
            p.Pixels[i] = PixelBuffer.Rgb(v, v, v);
        }

        return p;
    }

    [Fact]
    public void ColorRefConversion()
    {
        var argb = PixelBuffer.FromColorRef(15879332);
        Assert.Equal((164, 76, 242), PixelBuffer.Split(argb));
        Assert.Equal(15879332, PixelBuffer.ToColorRef(argb));
    }

    [Fact]
    public void CropAndResize()
    {
        var p = Pattern(40, 30, 1);
        var c = p.Crop(10, 5, 8, 6);
        Assert.Equal((8, 6), (c.Width, c.Height));
        Assert.Equal(p[10, 5], c[0, 0]);
        Assert.Equal((4, 3), (p.Resize(4, 3).Width, p.Resize(4, 3).Height));
    }

    [Fact]
    public void TemplateMatcherFindsPatch()
    {
        var hay = new PixelBuffer(200, 120);
        for (var y = 0; y < 120; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                hay[x, y] = PixelBuffer.Rgb(x, y * 2, 60);
            }
        }

        for (var y = 75; y < 85; y++)
        {
            for (var x = 138; x < 150; x++)
            {
                hay[x, y] = PixelBuffer.Rgb(255, 255, 255);
            }
        }

        var needle = hay.Crop(133, 71, 24, 18);
        var match = TemplateMatcher.Find(hay, needle);
        Assert.NotNull(match);
        Assert.Equal((133, 71), (match!.X, match.Y));
        Assert.Null(TemplateMatcher.Find(Pattern(200, 120, 3), needle, maxScore: 5));
    }

    [Fact]
    public void HashesAreStable()
    {
        var p = Pattern(64, 64, 4);
        Assert.Equal(ImageHash.Average(p), ImageHash.Average(p));
        Assert.Equal(0, ImageHash.Distance(ImageHash.Difference(p), ImageHash.Difference(p)));
        Assert.Equal(64, ImageHash.ToBinary(0x8000000000000001UL).Length);
        Assert.Equal(0x8000000000000001UL, ImageHash.FromBinary(ImageHash.ToBinary(0x8000000000000001UL)));
    }
}

public class CasinoTests
{
    [Fact]
    public void ReferencesComeFromOriginalTool()
    {
        Assert.Equal(16, CasinoFingerprintSolver.References.Count);
        Assert.Equal(ImageHash.FromBinary("1011000010101001100011011001111110110101100100001010010010000011"), CasinoFingerprintSolver.References[0]);
    }

    [Fact]
    public void SolvesWhenFourElementsMatchOneGroup()
    {
        var refs = CasinoFingerprintSolver.References;
        // Fingerprint #3 (refs 8..11) at element positions 1, 2, 5, 6 with small noise; others random.
        var elements = new ulong[8];
        var rnd = new Random(7);
        for (var i = 0; i < 8; i++)
        {
            elements[i] = (ulong)rnd.NextInt64() ^ 0x5555555555555555UL;
        }

        elements[1] = refs[8] ^ 0b1;
        elements[2] = refs[9];
        elements[5] = refs[10] ^ 0b11;
        elements[6] = refs[11];
        var solution = CasinoFingerprintSolver.Solve(elements, 10);
        Assert.True(solution.Success);
        Assert.Equal(2, solution.Group);
        Assert.Equal(new[] { 1, 2, 5, 6 }, solution.Selected);
        Assert.True(solution.Worst <= 2);
    }

    [Fact]
    public void FailsWhenNothingMatches()
    {
        var elements = Enumerable.Range(0, 8).Select(i => (ulong)new Random(i).NextInt64()).ToArray();
        Assert.False(CasinoFingerprintSolver.Solve(elements, 3).Success);
    }

    [Fact]
    public void KeySequenceSelectsAndSubmits()
    {
        var keys = CasinoFingerprintSolver.KeySequence(new[] { 0, 3 });
        Assert.Equal(InputCodes.Enter, keys[0]);
        Assert.Equal(InputCodes.Tab, keys[^1]);
        Assert.Equal(2, keys.Count(k => k == InputCodes.Enter));
        Assert.Equal(3, keys.Count(k => k == InputCodes.Down));
    }

    [Fact]
    public void CellLayoutIs2x4()
    {
        Assert.Equal((643d, 366d), (CasinoFingerprintSolver.CellRect(0).X, CasinoFingerprintSolver.CellRect(0).Y));
        Assert.Equal(643 + 192d, CasinoFingerprintSolver.CellRect(1).X);
        Assert.Equal(366 + (3 * 192d), CasinoFingerprintSolver.CellRect(7).Y);
    }
}

public class CayoTests
{
    [Fact]
    public void BestRotationPicksMinimumDifference()
    {
        Assert.Equal(3, CayoFingerprintSolver.BestRotation(new[] { 40.0, 30, 20, 2, 25, 33, 41, 50 }));
        Assert.Equal(0, CayoFingerprintSolver.BestRotation(new[] { 1.0, 30, 20 }));
    }

    [Fact]
    public void DifferenceIsZeroForIdenticalBands()
    {
        var img = new PixelBuffer(80, 80);
        for (var i = 0; i < img.Pixels.Length; i++)
        {
            img.Pixels[i] = PixelBuffer.Rgb(i % 255, 0, 0);
        }

        Assert.Equal(0, CayoFingerprintSolver.Difference(CayoFingerprintSolver.Band(img, 2), CayoFingerprintSolver.Band(img, 2)));
    }

    [Fact]
    public void LayoutParsing()
    {
        var (t, p) = CayoFingerprintSolver.ParseLayout("1,2,3,4", "bad");
        Assert.Equal(new ScreenRectD(1, 2, 3, 4), t);
        Assert.Equal(640, p.X);
    }
}

public class MedusaTests
{
    private const int Purple = 15879332;
    private const int Horizon = 789516;

    private static PixelBuffer Minimap(int? blipX, int? blipY, int? altitudeY)
    {
        var map = new PixelBuffer(400, 262);
        Array.Fill(map.Pixels, PixelBuffer.Rgb(90, 110, 90));
        if (blipX is { } bx && blipY is { } by)
        {
            for (var y = by - 3; y <= by + 3; y++)
            {
                for (var x = bx - 3; x <= bx + 3; x++)
                {
                    map[x, y] = PixelBuffer.FromColorRef(Purple);
                }
            }
        }

        if (altitudeY is { } ay)
        {
            for (var x = 0; x < 20; x++)
            {
                map[x, ay] = PixelBuffer.FromColorRef(Horizon);
            }
        }

        return map;
    }

    [Fact]
    public void TurnsTowardsWaypoint()
    {
        var left = MedusaAutopilot.Decide(Minimap(80, 60, 120), Purple, Horizon, 20);
        Assert.True(left.WaypointFound);
        Assert.Equal(YawCommand.Left, left.Yaw);
        var right = MedusaAutopilot.Decide(Minimap(330, 60, 120), Purple, Horizon, 20);
        Assert.Equal(YawCommand.Right, right.Yaw);
    }

    [Fact]
    public void ArrivesWhenBlipAtPlayer()
    {
        var d = MedusaAutopilot.Decide(Minimap(212, 162, 120), Purple, Horizon, 20);
        Assert.True(d.Arrived);
    }

    [Fact]
    public void AltitudeBand()
    {
        Assert.Equal(PitchCommand.Climb, MedusaAutopilot.Decide(Minimap(200, 20, 230), Purple, Horizon, 20).Pitch);
        Assert.Equal(PitchCommand.Dive, MedusaAutopilot.Decide(Minimap(200, 20, 20), Purple, Horizon, 20).Pitch);
        Assert.Equal(PitchCommand.None, MedusaAutopilot.Decide(Minimap(200, 20, 120), Purple, Horizon, 20).Pitch);
    }

    [Fact]
    public void NoWaypoint()
    {
        var d = MedusaAutopilot.Decide(Minimap(null, null, 120), Purple, Horizon, 20);
        Assert.False(d.WaypointFound);
        Assert.NotNull(d.AltitudeFraction);
    }
}
