using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.Core;

public static class ScreenCapture
{
    public static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Capture region must have positive size.");

        LogService.Instance.Info(
            $"ScreenCapture CopyFromScreen: x={x}, y={y}, width={width}, height={height}, pixelFormat={PixelFormat.Format32bppArgb}");

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        LogService.Instance.Info($"ScreenCapture bitmap created: {bitmap.Width}x{bitmap.Height}, format={bitmap.PixelFormat}");
        return bitmap;
    }

    public static string CaptureRegionToTempFile(int x, int y, int width, int height)
    {
        using var bitmap = CaptureRegion(x, y, width, height);

        var path = Path.Combine(Path.GetTempPath(), $"gto_capture_{Guid.NewGuid():N}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
