using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace UltraShoot.Core;

public record ScreenCaptureResult(
    BitmapSource FullScreenBitmap,
    int VirtualX,
    int VirtualY,
    int VirtualWidth,
    int VirtualHeight
);

public static class ScreenCaptureService
{
    public static ScreenCaptureResult CaptureVirtualScreen()
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, vw, vh);
        IntPtr hOldBitmap = NativeMethods.SelectObject(hdcMem, hBitmap);

        // Blit the virtual screen (SRCCOPY | CAPTUREBLT to also capture layered/semi-transparent windows)
        NativeMethods.BitBlt(hdcMem, 0, 0, vw, vh, hdcScreen, vx, vy, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

        NativeMethods.SelectObject(hdcMem, hOldBitmap);

        BitmapSource bitmapSource;
        try
        {
            bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            
            bitmapSource.Freeze();
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(hdcMem);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        }

        return new ScreenCaptureResult(bitmapSource, vx, vy, vw, vh);
    }

    public static BitmapSource CropBitmap(BitmapSource source, int x, int y, int width, int height)
    {
        // Clamp bounds to source bitmap dimensions
        if (x < 0)
        {
            width += x;
            x = 0;
        }
        if (y < 0)
        {
            height += y;
            y = 0;
        }
        if (x + width > source.PixelWidth)
        {
            width = source.PixelWidth - x;
        }
        if (y + height > source.PixelHeight)
        {
            height = source.PixelHeight - y;
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Kırpılacak alan geçerli değil.");
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }
}
