namespace NovaMacro.Core.Imaging;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public ScreenRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>32-bit image buffer, pixels stored as 0xAARRGGBB.</summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height, int[]? pixels = null)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        Width = width;
        Height = height;
        Pixels = pixels ?? new int[width * height];
        if (Pixels.Length < width * height)
        {
            throw new ArgumentException("Pixel array too small.", nameof(pixels));
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int[] Pixels { get; }

    public int this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    public static int Rgb(int r, int g, int b) => unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;

    public static (int R, int G, int B) Split(int argb) => ((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);

    /// <summary>Converts a Win32 COLORREF (0x00BBGGRR, as stored by the original tool) to ARGB.</summary>
    public static int FromColorRef(int colorRef) => Rgb(colorRef & 0xFF, (colorRef >> 8) & 0xFF, (colorRef >> 16) & 0xFF);

    public static int ToColorRef(int argb)
    {
        var (r, g, b) = Split(argb);
        return r | (g << 8) | (b << 16);
    }

    public static int Gray(int argb)
    {
        var (r, g, b) = Split(argb);
        return ((r * 299) + (g * 587) + (b * 114)) / 1000;
    }

    public static bool ColorNear(int a, int b, int tolerance)
    {
        var (ar, ag, ab) = Split(a);
        var (br, bg, bb) = Split(b);
        return Math.Abs(ar - br) <= tolerance && Math.Abs(ag - bg) <= tolerance && Math.Abs(ab - bb) <= tolerance;
    }

    public PixelBuffer Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width);
        y = Math.Clamp(y, 0, Height);
        width = Math.Clamp(width, 0, Width - x);
        height = Math.Clamp(height, 0, Height - y);
        var result = new PixelBuffer(width, height);
        for (var row = 0; row < height; row++)
        {
            Array.Copy(Pixels, ((y + row) * Width) + x, result.Pixels, row * width, width);
        }

        return result;
    }

    /// <summary>Area-average downscale to an exact size.</summary>
    public PixelBuffer Resize(int width, int height)
    {
        var result = new PixelBuffer(width, height);
        for (var ty = 0; ty < height; ty++)
        {
            var y0 = ty * Height / height;
            var y1 = Math.Max(y0 + 1, (ty + 1) * Height / height);
            for (var tx = 0; tx < width; tx++)
            {
                var x0 = tx * Width / width;
                var x1 = Math.Max(x0 + 1, (tx + 1) * Width / width);
                long r = 0, g = 0, b = 0, n = 0;
                for (var y = y0; y < y1 && y < Height; y++)
                {
                    for (var x = x0; x < x1 && x < Width; x++)
                    {
                        var (pr, pg, pb) = Split(this[x, y]);
                        r += pr;
                        g += pg;
                        b += pb;
                        n++;
                    }
                }

                result[tx, ty] = n == 0 ? 0 : Rgb((int)(r / n), (int)(g / n), (int)(b / n));
            }
        }

        return result;
    }

    /// <summary>Finds pixels near <paramref name="argb"/>; returns their centroid, or null.</summary>
    public (int X, int Y, int Count)? FindColorCentroid(int argb, int tolerance, int minCount = 3)
    {
        long sx = 0, sy = 0;
        var count = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (ColorNear(this[x, y], argb, tolerance))
                {
                    sx += x;
                    sy += y;
                    count++;
                }
            }
        }

        return count >= minCount ? ((int)(sx / count), (int)(sy / count), count) : null;
    }
}
