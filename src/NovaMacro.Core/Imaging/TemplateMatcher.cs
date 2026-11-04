namespace NovaMacro.Core.Imaging;

/// <summary>Simple sum-of-absolute-differences template search with a coarse pass and local refinement.</summary>
public static class TemplateMatcher
{
    public sealed record Match(int X, int Y, double Score);

    /// <param name="maxScore">Maximum mean absolute grayscale difference (0..255) accepted as a match.</param>
    public static Match? Find(PixelBuffer haystack, PixelBuffer needle, double maxScore = 20, int coarseStep = 4)
    {
        if (needle.Width == 0 || needle.Height == 0 || needle.Width > haystack.Width || needle.Height > haystack.Height)
        {
            return null;
        }

        var hay = haystack.Pixels.Select(PixelBuffer.Gray).ToArray();
        var ndl = needle.Pixels.Select(PixelBuffer.Gray).ToArray();
        var sampleStep = Math.Max(1, Math.Min(needle.Width, needle.Height) / 12);

        Match? best = null;
        for (var y = 0; y <= haystack.Height - needle.Height; y += coarseStep)
        {
            for (var x = 0; x <= haystack.Width - needle.Width; x += coarseStep)
            {
                var score = Score(hay, haystack.Width, ndl, needle.Width, needle.Height, x, y, sampleStep, best?.Score ?? double.MaxValue);
                if (best is null || score < best.Score)
                {
                    best = new Match(x, y, score);
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        var refined = best;
        for (var y = Math.Max(0, best.Y - coarseStep); y <= Math.Min(haystack.Height - needle.Height, best.Y + coarseStep); y++)
        {
            for (var x = Math.Max(0, best.X - coarseStep); x <= Math.Min(haystack.Width - needle.Width, best.X + coarseStep); x++)
            {
                var score = Score(hay, haystack.Width, ndl, needle.Width, needle.Height, x, y, 1, refined.Score);
                if (score < refined.Score)
                {
                    refined = new Match(x, y, score);
                }
            }
        }

        return refined.Score <= maxScore ? refined : null;
    }

    private static double Score(int[] hay, int hayWidth, int[] ndl, int w, int h, int ox, int oy, int step, double cutoff)
    {
        long sum = 0;
        var n = 0;
        for (var y = 0; y < h; y += step)
        {
            var hRow = (oy + y) * hayWidth;
            var nRow = y * w;
            for (var x = 0; x < w; x += step)
            {
                sum += Math.Abs(hay[hRow + ox + x] - ndl[nRow + x]);
                n++;
            }

            if (n > 0 && sum / (double)n > cutoff * 4)
            {
                return double.MaxValue;
            }
        }

        return n == 0 ? double.MaxValue : sum / (double)n;
    }
}
