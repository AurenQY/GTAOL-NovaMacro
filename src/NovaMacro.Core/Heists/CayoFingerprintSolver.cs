using NovaMacro.Core.Imaging;

namespace NovaMacro.Core.Heists;

/// <summary>
/// Cayo Perico fingerprint scanner: the left print is split into 8 horizontal slices that rotate
/// through 8 variants (Left/Right), Down moves to the next slice. Each slice is aligned by
/// comparing it with the matching band of the target print on the right.
/// </summary>
public static class CayoFingerprintSolver
{
    public const int Slices = 8;
    public const int Variants = 8;

    /// <summary>Mean absolute grayscale difference between two equally sized images (0..255).</summary>
    public static double Difference(PixelBuffer a, PixelBuffer b)
    {
        var w = Math.Min(a.Width, b.Width);
        var h = Math.Min(a.Height, b.Height);
        if (w == 0 || h == 0)
        {
            return 255;
        }

        long sum = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                sum += Math.Abs(PixelBuffer.Gray(a[x, y]) - PixelBuffer.Gray(b[x, y]));
            }
        }

        return sum / (double)(w * h);
    }

    /// <summary>Band <paramref name="slice"/> of an image split into <see cref="Slices"/> rows, normalised to a small size.</summary>
    public static PixelBuffer Band(PixelBuffer image, int slice)
    {
        var bandHeight = image.Height / Slices;
        return image.Crop(0, slice * bandHeight, image.Width, bandHeight).Resize(48, 6);
    }

    /// <summary>
    /// Given the difference of the current variant measured after each Right press
    /// (index 0 = before pressing), returns how many Right presses reach the best variant.
    /// </summary>
    public static int BestRotation(IReadOnlyList<double> differences)
    {
        var best = 0;
        for (var i = 1; i < differences.Count; i++)
        {
            if (differences[i] < differences[best])
            {
                best = i;
            }
        }

        return best;
    }

    public static (ScreenRectD Target, ScreenRectD Puzzle) ParseLayout(string target, string puzzle) =>
        (ScreenRectD.Parse(target, new ScreenRectD(1340, 330, 545, 780)), ScreenRectD.Parse(puzzle, new ScreenRectD(640, 330, 545, 780)));
}

/// <summary>Rectangle in 2560x1440 design coordinates.</summary>
public readonly record struct ScreenRectD(double X, double Y, double W, double H)
{
    public static ScreenRectD Parse(string? text, ScreenRectD fallback)
    {
        var parts = (text ?? string.Empty).Split(',', '，', '|');
        if (parts.Length == 4 && parts.All(p => double.TryParse(p, out _)))
        {
            var v = parts.Select(double.Parse).ToArray();
            return new ScreenRectD(v[0], v[1], v[2], v[3]);
        }

        return fallback;
    }

    public override string ToString() => $"{X},{Y},{W},{H}";
}
