using DeepL;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.Core;

public sealed class Translator : IDisposable
{
    private string? _clientApiKey;
    private DeepL.Translator? _client;

    public void InvalidateClient()
    {
        _client?.Dispose();
        _client = null;
        _clientApiKey = null;
    }

    public async Task<DeepLUsageInfo> GetUsageAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DeepL API key is not configured.");

        LogService.Instance.Info("GetUsageAsync start");

        try
        {
            var client = GetClient(apiKey);
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
            var client = GetClient(apiKey);
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

    private DeepL.Translator GetClient(string apiKey)
    {
        if (_client is not null && string.Equals(_clientApiKey, apiKey, StringComparison.Ordinal))
            return _client;

        _client?.Dispose();
        _clientApiKey = apiKey;
        _client = new DeepL.Translator(apiKey);
        return _client;
    }

    public void Dispose() => InvalidateClient();
}
