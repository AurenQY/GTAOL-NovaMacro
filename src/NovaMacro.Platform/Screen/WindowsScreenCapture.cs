using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NovaMacro.Core.Imaging;
using NovaMacro.Core.Runtime;
using NovaMacro.Platform.Native;

namespace NovaMacro.Platform.Screen;

/// <summary>GDI BitBlt screen capture into <see cref="PixelBuffer"/>.</summary>
public sealed class WindowsScreenCapture : IScreenCapture
{
    public ScreenRect VirtualScreen => new(
        User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN),
        User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN),
        User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN),
        User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN));

    public PixelBuffer Capture(ScreenRect rect)
    {
        if (rect.IsEmpty)
        {
            return new PixelBuffer(0, 0);
        }

        var screenDc = User32.GetDC(0);
        var memDc = Gdi32.CreateCompatibleDC(screenDc);
        var header = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
            biWidth = rect.Width,
            biHeight = -rect.Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = Gdi32.CreateDIBSection(screenDc, ref header, 0, out var bits, 0, 0);
        var old = Gdi32.SelectObject(memDc, bitmap);
        try
        {
            Gdi32.BitBlt(memDc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y, Gdi32.SRCCOPY | Gdi32.CAPTUREBLT);
            var pixels = new int[rect.Width * rect.Height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] |= unchecked((int)0xFF000000);
            }

            return new PixelBuffer(rect.Width, rect.Height, pixels);
        }
        finally
        {
            Gdi32.SelectObject(memDc, old);
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memDc);
            User32.ReleaseDC(0, screenDc);
        }
    }

    public PixelBuffer? LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var bmp = new Bitmap(path);
            return FromBitmap(bmp);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    public void SaveImage(PixelBuffer image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bmp = ToBitmap(image);
        var format = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Png : ImageFormat.Bmp;
        bmp.Save(path, format);
    }

    public static PixelBuffer FromBitmap(Bitmap source)
    {
        using var bmp = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new PixelBuffer(bmp.Width, bmp.Height);
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), buffer.Pixels, y * bmp.Width, bmp.Width);
            }

            return buffer;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public static Bitmap ToBitmap(PixelBuffer image)
    {
        var bmp = new Bitmap(Math.Max(1, image.Width), Math.Max(1, image.Height), PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.Pixels, y * image.Width, data.Scan0 + (y * data.Stride), image.Width);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return bmp;
    }
}
