using NovaMacro.Core.Imaging;

namespace NovaMacro.Core.Heists;

/// <summary>
/// Diamond Casino fingerprint hack: 8 candidate elements (2 columns x 4 rows); 4 belong to the
/// target print. Reference hashes for the 4 prints x 4 elements come from the original tool.
/// Layout is defined on a 2560x1440 client (scaled by height, centred horizontally).
/// </summary>
public static class CasinoFingerprintSolver
{
    public const double GridX = 643;
    public const double GridY = 366;
    public const double CellSize = 150;
    public const double CellGap = 42;
    public const int Columns = 2;
    public const int Rows = 4;

    /// <summary>16 reference hashes (4 fingerprints x 4 correct elements).</summary>
    public static IReadOnlyList<ulong> References { get; } = new[]
    {
        "1011000010101001100011011001111110110101100100001010010010000011",
        "1110010010100110110111001001110010100100101001001110010011010001",
        "1100110011001100101101001000001010100010101001011010010011000000",
        "1101101010011010101010111000010110001001101100011010101011001000",
        "1010001010000010100001001000100110101011100111001001100110000110",
        "1011010011100100111001001000100011010000100010011011000111100010",
        "1111001110111001100100011010111010001001110100110110101010101001",
        "1100010010000000100110001100100011001100101001001001010010100100",
        "1100010011100110110111111101001111111000111111001110100110111011",
        "1011101011100110111010101101110111010110110100111110000011010000",
        "1000000010011000110001001110010010100010101000101001100110010100",
        "1010101011111001111000101110001111010011110110001110101010110101",
        "1100010011000100100010001000000010100100101101001001000011010001",
        "1010101010000100100101101001001010110011100110001011000010100011",
        "1001000110110010110010011100110010001101100110011010000111101001",
        "1010010010001010101010001011100010001011111001011100100110100100",
    }.Select(ImageHash.FromBinary).ToArray();

    /// <summary>Design-space rectangle (2560x1440) of the element at <paramref name="index"/> (row-major).</summary>
    public static (double X, double Y, double W, double H) CellRect(int index)
    {
        var col = index % Columns;
        var row = index / Columns;
        return (GridX + (col * (CellSize + CellGap)), GridY + (row * (CellSize + CellGap)), CellSize, CellSize);
    }

    /// <summary>
    /// Picks the fingerprint group whose references best match the captured elements.
    /// Returns the selected element indices (row-major) and the worst distance used.
    /// </summary>
    public static FingerprintSolution Solve(IReadOnlyList<ulong> elementHashes, int maxDistance, IReadOnlyList<ulong>? references = null)
    {
        references ??= References;
        if (elementHashes.Count != Columns * Rows)
        {
            throw new ArgumentException("Expected 8 element hashes.", nameof(elementHashes));
        }

        FingerprintSolution? best = null;
        for (var group = 0; group < references.Count / 4; group++)
        {
            var refs = references.Skip(group * 4).Take(4).ToArray();
            var assignment = Assign(refs, elementHashes);
            if (best is null || assignment.Total < best.Total)
            {
                best = assignment with { Group = group };
            }
        }

        if (best is null || best.Worst > maxDistance)
        {
            return best is null ? FingerprintSolution.Failed : best with { Success = false };
        }

        return best with { Success = true };
    }

    /// <summary>Optimal assignment of 4 references to distinct elements (brute force over 8P4).</summary>
    private static FingerprintSolution Assign(ulong[] refs, IReadOnlyList<ulong> elements)
    {
        var bestTotal = int.MaxValue;
        var bestWorst = int.MaxValue;
        int[] bestPick = Array.Empty<int>();
        var pick = new int[refs.Length];
        var used = new bool[elements.Count];

        void Recurse(int depth, int total, int worst)
        {
            if (total >= bestTotal)
            {
                return;
            }

            if (depth == refs.Length)
            {
                bestTotal = total;
                bestWorst = worst;
                bestPick = (int[])pick.Clone();
                return;
            }

            for (var i = 0; i < elements.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                var d = ImageHash.Distance(refs[depth], elements[i]);
                used[i] = true;
                pick[depth] = i;
                Recurse(depth + 1, total + d, Math.Max(worst, d));
                used[i] = false;
            }
        }

        Recurse(0, 0, 0);
        return new FingerprintSolution(false, -1, bestPick.OrderBy(i => i).ToArray(), bestTotal, bestWorst);
    }

    /// <summary>Key presses that select the chosen elements (Enter) moving with Right, then Tab to confirm.</summary>
    public static IReadOnlyList<int> KeySequence(IReadOnlyList<int> selected)
    {
        var keys = new List<int>();
        for (var i = 0; i < Columns * Rows; i++)
        {
            if (selected.Contains(i))
            {
                keys.Add(Input.InputCodes.Enter);
            }

            if (i < (Columns * Rows) - 1)
            {
                keys.Add(i % Columns == Columns - 1 ? Input.InputCodes.Down : Input.InputCodes.Right);
                if (i % Columns == Columns - 1)
                {
                    // move back to the first column of the next row
                    for (var c = 0; c < Columns - 1; c++)
                    {
                        keys.Add(Input.InputCodes.Left);
                    }
                }
            }
        }

        keys.Add(Input.InputCodes.Tab);
        return keys;
    }

    public static ulong HashElement(PixelBuffer cell)
    {
        // Ignore a thin border to reduce highlight-frame noise.
        var inset = Math.Max(1, cell.Width / 12);
        return ImageHash.Average(cell.Crop(inset, inset, cell.Width - (2 * inset), cell.Height - (2 * inset)));
    }
}

public sealed record FingerprintSolution(bool Success, int Group, IReadOnlyList<int> Selected, int Total, int Worst)
{
    public static FingerprintSolution Failed { get; } = new(false, -1, Array.Empty<int>(), int.MaxValue, int.MaxValue);
}
