using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using GameTranslateOverlay.Services;
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;

namespace GameTranslateOverlay.Core;

public sealed class OcrEngine
{
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

    public async Task<string> RecognizeAsync(string imagePath, string sourceLang, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LogService.Instance.Info($"RecognizeAsync start: lang={sourceLang}, image={imagePath}");

        var tag = MapSourceLangToWindowsTag(sourceLang);
        var language = new Language(tag);
        var engine = WinOcrEngine.TryCreateFromLanguage(language);
        if (engine is null)
        {
            LogService.Instance.Warn($"OCR language unavailable: {sourceLang} (tag={tag})");
            throw new InvalidOperationException(GetAvailabilityMessage(sourceLang));
        }

        using var fileStream = File.OpenRead(imagePath);
        using var randomAccessStream = fileStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken).ConfigureAwait(false);

        var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        LogService.Instance.Info(
            $"OCR decoded SoftwareBitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}, " +
            $"format={softwareBitmap.BitmapPixelFormat}, alpha={softwareBitmap.BitmapAlphaMode}");

        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Ignore)
        {
            softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            LogService.Instance.Info(
                $"OCR converted SoftwareBitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}, " +
                $"format={softwareBitmap.BitmapPixelFormat}, alpha={softwareBitmap.BitmapAlphaMode}");
        }

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
}
