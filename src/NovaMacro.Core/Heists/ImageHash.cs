using System.Numerics;
using NovaMacro.Core.Imaging;

namespace NovaMacro.Core.Heists;

/// <summary>64-bit perceptual hashes (8x8 grayscale).</summary>
public static class ImageHash
{
    /// <summary>Average hash: bit = pixel brighter than the mean (row-major, MSB first).</summary>
    public static ulong Average(PixelBuffer image)
    {
        var small = image.Resize(8, 8);
        var gray = small.Pixels.Select(PixelBuffer.Gray).ToArray();
        var mean = gray.Average();
        ulong hash = 0;
        for (var i = 0; i < 64; i++)
        {
            hash <<= 1;
            if (gray[i] >= mean)
            {
                hash |= 1;
            }
        }

        return hash;
    }

    /// <summary>Difference hash: bit = pixel brighter than its right neighbour (9x8 sample).</summary>
    public static ulong Difference(PixelBuffer image)
    {
        var small = image.Resize(9, 8);
        ulong hash = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                hash <<= 1;
                if (PixelBuffer.Gray(small[x, y]) > PixelBuffer.Gray(small[x + 1, y]))
                {
                    hash |= 1;
                }
            }
        }

        return hash;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    public static ulong FromBinary(string bits)
    {
        ulong value = 0;
        foreach (var c in bits.Trim())
        {
            value = (value << 1) | (c == '1' ? 1UL : 0UL);
        }

        return value;
    }

    public static string ToBinary(ulong value) => Convert.ToString((long)value, 2).PadLeft(64, '0');
}
