using DeepL;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.Core;

public sealed class Translator
{
    public async Task<DeepLUsageInfo> GetUsageAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DeepL API key is not configured.");

        LogService.Instance.Info("GetUsageAsync start");

        try
        {
            var client = new DeepL.Translator(apiKey);
            var usage = await client.GetUsageAsync(cancellationToken).ConfigureAwait(false);
            var detail = usage.Character;
            if (detail is null)
                throw new InvalidOperationException("DeepL usage response did not include character quota.");

            var info = new DeepLUsageInfo(
                detail.Count,
                detail.Limit,
                detail.LimitReached || usage.AnyLimitReached);

            LogService.Instance.Info(
                $"GetUsageAsync success: used={info.Used}, limit={info.Limit}, remaining={info.Remaining}");
            return info;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("GetUsageAsync failed", ex);
            throw;
        }
    }

    public async Task<string> TranslateAsync(
        string apiKey,
        string sourceLang,
        string targetLang,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DeepL API key is not configured.");

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var deeplSource = DeepLLanguage.NormalizeForDeepL(sourceLang, isTarget: false);
        var deeplTarget = DeepLLanguage.NormalizeForDeepL(targetLang, isTarget: true);

        LogService.Instance.Info(
            $"TranslateAsync start: {deeplSource}->{deeplTarget}, text length={text.Length}");

        try
        {
            var client = new DeepL.Translator(apiKey);
            var result = await client.TranslateTextAsync(
                text,
                deeplSource,
                deeplTarget,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogService.Instance.Info($"TranslateAsync success: output length={result.Text.Length}");
            return result.Text;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("TranslateAsync failed", ex);
            throw;
        }
    }
}
