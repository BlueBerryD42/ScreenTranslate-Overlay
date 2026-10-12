using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using GameTranslateOverlay.Services;
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;

namespace GameTranslateOverlay.Core;

public sealed class OcrEngine
{
    private string? _cachedSourceLang;
    private WinOcrEngine? _cachedEngine;

    public static string MapSourceLangToWindowsTag(string sourceLang) =>
        sourceLang.ToUpperInvariant() switch
        {
            "JA" => "ja",
            "EN" => "en",
            "DE" => "de",
            "FR" => "fr",
            "ES" => "es",
            "ZH-HANS" => "zh-Hans",
            "ZH-HANT" => "zh-Hant",
            "KO" => "ko",
            "VI" => "vi",
            _ => "en",
        };

    public static string GetLanguageDisplayName(string sourceLang) =>
        sourceLang.ToUpperInvariant() switch
        {
            "JA" => "Japanese",
            "EN" => "English",
            "DE" => "German",
            "FR" => "French",
            "ES" => "Spanish",
            "ZH-HANS" => "Chinese (Simplified)",
            "ZH-HANT" => "Chinese (Traditional)",
            "KO" => "Korean",
            "VI" => "Vietnamese",
            _ => sourceLang,
        };

    public static bool IsLanguageAvailable(string sourceLang)
    {
        var tag = MapSourceLangToWindowsTag(sourceLang);
        var language = new Language(tag);
        return WinOcrEngine.TryCreateFromLanguage(language) is not null;
    }

    public static string GetAvailabilityMessage(string sourceLang)
    {
        var name = GetLanguageDisplayName(sourceLang);
        if (IsLanguageAvailable(sourceLang))
            return $"{name} OCR is ready.";

        return
            $"Install the {name} language pack with \"Optical character recognition\" enabled: " +
            "Windows Settings → Time & language → Language & region → your language → Language options → Language features.";
    }

    public void InvalidateEngine()
    {
        _cachedEngine = null;
        _cachedSourceLang = null;
    }

    public async Task<string> RecognizeAsync(System.Drawing.Bitmap bitmap, string sourceLang, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LogService.Instance.Info($"RecognizeAsync start (in-memory): lang={sourceLang}, size={bitmap.Width}x{bitmap.Height}");

        var engine = GetEngine(sourceLang);

        using var softwareBitmap = ToSoftwareBitmap(bitmap);
        LogService.Instance.Info(
            $"OCR in-memory SoftwareBitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}, " +
            $"format={softwareBitmap.BitmapPixelFormat}, alpha={softwareBitmap.BitmapAlphaMode}");

        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);
        var text = result.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            LogService.Instance.Warn(
                $"RecognizeAsync returned empty text; image dimensions={softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}");
        }
        else
        {
            LogService.Instance.Info($"RecognizeAsync complete: char count={text.Length}");
        }

        return text;
    }

    public static SoftwareBitmap ToSoftwareBitmap(System.Drawing.Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        var bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = bmpData.Stride;
            const int bytesPerPixel = 4;
            var byteCount = width * height * bytesPerPixel;
            byte[] pixels;

            if (stride == width * bytesPerPixel)
            {
                pixels = new byte[byteCount];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixels, 0, byteCount);
            }
            else
            {
                pixels = new byte[byteCount];
                for (var y = 0; y < height; y++)
                {
                    var rowSource = IntPtr.Add(bmpData.Scan0, y * stride);
                    System.Runtime.InteropServices.Marshal.Copy(rowSource, pixels, y * width * bytesPerPixel, width * bytesPerPixel);
                }
            }

            var buffer = pixels.AsBuffer();
            return SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                BitmapPixelFormat.Bgra8,
                width,
                height,
                BitmapAlphaMode.Ignore);
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }
    }

    public async Task<string> RecognizeAsync(string imagePath, string sourceLang, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LogService.Instance.Info($"RecognizeAsync start: lang={sourceLang}, image={imagePath}");

        var engine = GetEngine(sourceLang);

        using var fileStream = File.OpenRead(imagePath);
        using var randomAccessStream = fileStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken).ConfigureAwait(false);

        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        LogService.Instance.Info(
            $"OCR decoded SoftwareBitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}, " +
            $"format={softwareBitmap.BitmapPixelFormat}, alpha={softwareBitmap.BitmapAlphaMode}");

        var targetBitmap = softwareBitmap;
        SoftwareBitmap? convertedBitmap = null;
        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Ignore)
        {
            convertedBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            targetBitmap = convertedBitmap;
            LogService.Instance.Info(
                $"OCR converted SoftwareBitmap: {targetBitmap.PixelWidth}x{targetBitmap.PixelHeight}, " +
                $"format={targetBitmap.BitmapPixelFormat}, alpha={targetBitmap.BitmapAlphaMode}");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(targetBitmap).AsTask(cancellationToken).ConfigureAwait(false);
            var text = result.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                LogService.Instance.Warn(
                    $"RecognizeAsync returned empty text; image dimensions={targetBitmap.PixelWidth}x{targetBitmap.PixelHeight}");
            }
            else
            {
                LogService.Instance.Info($"RecognizeAsync complete: char count={text.Length}");
            }

            return text;
        }
        finally
        {
            convertedBitmap?.Dispose();
        }
    }

    private WinOcrEngine GetEngine(string sourceLang)
    {
        if (_cachedEngine is not null &&
            string.Equals(_cachedSourceLang, sourceLang, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedEngine;
        }

        var tag = MapSourceLangToWindowsTag(sourceLang);
        var language = new Language(tag);
        var engine = WinOcrEngine.TryCreateFromLanguage(language);
        if (engine is null)
        {
            LogService.Instance.Warn($"OCR language unavailable: {sourceLang} (tag={tag})");
            throw new InvalidOperationException(GetAvailabilityMessage(sourceLang));
        }

        _cachedSourceLang = sourceLang;
        _cachedEngine = engine;
        return engine;
    }
}
