using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.Core;

/// <summary>
/// Prepares captured screenshots for Windows OCR (upscale, contrast).
/// </summary>
public static class OcrImagePreprocessor
{
    private const int NarrowWidthThreshold = 200;
    private const int ShortHeightThreshold = 40;
    private const int LowPixelCountThreshold = 80_000;
    private const int MinTargetWidth = 320;
    private const int MaxDimension = 4096;
    private const double ContrastFactor = 1.2;

    public static string Prepare(string capturePath)
    {
        using var source = new Bitmap(capturePath);
        if (!ShouldPreprocess(source.Width, source.Height))
        {
            LogService.Instance.Info($"OCR prep: skipped ({source.Width}x{source.Height})");
            return capturePath;
        }

        using var processed = ProcessBitmap(source);
        var outputPath = Path.Combine(Path.GetTempPath(), $"gto_prep_{Guid.NewGuid():N}.png");
        processed.Save(outputPath, ImageFormat.Png);

        var scale = processed.Width / (double)source.Width;
        LogService.Instance.Info(
            $"OCR prep: {source.Width}x{source.Height} → {processed.Width}x{processed.Height} " +
            $"(scale={scale:F2}), contrast=applied");

        return outputPath;
    }

    internal static bool ShouldPreprocess(int width, int height)
    {
        if (width < NarrowWidthThreshold || height < ShortHeightThreshold)
            return true;

        return width * height < LowPixelCountThreshold;
    }

    private static Bitmap ProcessBitmap(Bitmap source)
    {
        var scale = Math.Max(1.0, MinTargetWidth / (double)source.Width);
        var newWidth = (int)Math.Round(source.Width * scale);
        var newHeight = (int)Math.Round(source.Height * scale);

        if (newWidth > MaxDimension || newHeight > MaxDimension)
        {
            var capScale = Math.Min(MaxDimension / (double)newWidth, MaxDimension / (double)newHeight);
            newWidth = Math.Max(1, (int)Math.Round(newWidth * capScale));
            newHeight = Math.Max(1, (int)Math.Round(newHeight * capScale));
        }

        using var scaled = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, newWidth, newHeight);
        }

        return ApplyGrayscaleContrast(scaled);
    }

    private static Bitmap ApplyGrayscaleContrast(Bitmap input)
    {
        var output = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, input.Width, input.Height);

        var sourceData = input.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var destData = output.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var sourceBytes = new byte[sourceData.Stride * input.Height];
            var destBytes = new byte[destData.Stride * output.Height];
            System.Runtime.InteropServices.Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);

            var luminance = new byte[input.Width * input.Height];
            for (var y = 0; y < input.Height; y++)
            {
                for (var x = 0; x < input.Width; x++)
                {
                    var i = y * sourceData.Stride + x * 4;
                    var b = sourceBytes[i];
                    var g = sourceBytes[i + 1];
                    var r = sourceBytes[i + 2];
                    luminance[y * input.Width + x] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                }
            }

            var (minL, maxL) = GetContrastRange(luminance);
            var range = Math.Max(1, maxL - minL);

            for (var y = 0; y < input.Height; y++)
            {
                for (var x = 0; x < input.Width; x++)
                {
                    var lum = luminance[y * input.Width + x];
                    var stretched = (lum - minL) / (double)range;
                    stretched = ((stretched - 0.5) * ContrastFactor) + 0.5;
                    var value = (byte)Math.Clamp((int)Math.Round(stretched * 255), 0, 255);

                    var di = y * destData.Stride + x * 4;
                    destBytes[di] = value;
                    destBytes[di + 1] = value;
                    destBytes[di + 2] = value;
                    destBytes[di + 3] = 255;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(destBytes, 0, destData.Scan0, destBytes.Length);
        }
        finally
        {
            input.UnlockBits(sourceData);
            output.UnlockBits(destData);
        }

        return output;
    }

    private static (byte Min, byte Max) GetContrastRange(byte[] luminance)
    {
        var sorted = luminance.OrderBy(v => v).ToArray();
        var lowIndex = (int)Math.Floor(sorted.Length * 0.02);
        var highIndex = (int)Math.Ceiling(sorted.Length * 0.98) - 1;
        lowIndex = Math.Clamp(lowIndex, 0, sorted.Length - 1);
        highIndex = Math.Clamp(highIndex, 0, sorted.Length - 1);
        return (sorted[lowIndex], sorted[highIndex]);
    }
}
