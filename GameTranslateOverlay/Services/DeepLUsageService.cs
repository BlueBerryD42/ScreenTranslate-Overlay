using GameTranslateOverlay.Core;
using GameTranslateOverlay.Models;

namespace GameTranslateOverlay.Services;

public sealed class DeepLUsageService
{
    public const double LowUsedFraction = 0.85;
    public const double CriticalUsedFraction = 0.95;

    private readonly Translator _translator;
    private DeepLUsageInfo? _cached;
    private DateTime _cachedAt = DateTime.MinValue;
    private DeepLQuotaWarningLevel _lastWarnedLevel = DeepLQuotaWarningLevel.None;

    public DeepLUsageService(Translator translator) => _translator = translator;

    public async Task<DeepLUsageInfo?> GetUsageAsync(
        string apiKey,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        if (!forceRefresh && _cached is not null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1))
            return _cached;

        try
        {
            var usage = await _translator.GetUsageAsync(apiKey, cancellationToken).ConfigureAwait(false);
            _cached = usage;
            _cachedAt = DateTime.UtcNow;
            return usage;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"DeepL usage fetch failed: {ex.Message}");
            return _cached;
        }
    }

    public void InvalidateCache() => _cached = null;

    public static DeepLQuotaWarningLevel GetWarningLevel(DeepLUsageInfo usage)
    {
        if (usage.LimitReached || usage.Remaining <= 0)
            return DeepLQuotaWarningLevel.Exceeded;

        if (!usage.HasQuota)
            return DeepLQuotaWarningLevel.None;

        if (usage.UsedFraction >= CriticalUsedFraction)
            return DeepLQuotaWarningLevel.Critical;

        if (usage.UsedFraction >= LowUsedFraction)
            return DeepLQuotaWarningLevel.Low;

        return DeepLQuotaWarningLevel.None;
    }

    public static string FormatSummary(DeepLUsageInfo usage)
    {
        if (!usage.HasQuota)
            return "DeepL quota unavailable for this account.";

        var remaining = usage.Remaining;
        var percentLeft = (1 - usage.UsedFraction) * 100;
        return $"{FormatCount(remaining)} of {FormatCount(usage.Limit)} characters remaining ({percentLeft:0.#}%)";
    }

    public static string FormatDetailed(DeepLUsageInfo usage)
    {
        if (!usage.HasQuota)
            return "Quota information is not available for this DeepL account.";

        return
            $"{FormatCount(usage.Used)} used / {FormatCount(usage.Limit)} limit\n" +
            $"{FormatSummary(usage)}";
    }

    public string? GetWarningMessage(DeepLUsageInfo usage)
    {
        return GetWarningLevel(usage) switch
        {
            DeepLQuotaWarningLevel.Exceeded =>
                "DeepL quota exhausted. Upgrade your plan or wait for the next billing period.",
            DeepLQuotaWarningLevel.Critical =>
                $"DeepL quota almost exhausted: {FormatSummary(usage)}.",
            DeepLQuotaWarningLevel.Low =>
                $"DeepL quota running low: {FormatSummary(usage)}.",
            _ => null,
        };
    }

    public bool TryEmitWarning(DeepLUsageInfo usage, Action<string, string> showBalloon)
    {
        var level = GetWarningLevel(usage);
        if (level == DeepLQuotaWarningLevel.None || level <= _lastWarnedLevel)
            return false;

        var message = GetWarningMessage(usage);
        if (message is null)
            return false;

        _lastWarnedLevel = level;
        showBalloon("DeepL quota", message);
        LogService.Instance.Warn($"DeepL quota warning ({level}): {message}");
        return true;
    }

    public void ResetWarnings() => _lastWarnedLevel = DeepLQuotaWarningLevel.None;

    private static string FormatCount(long count) => count.ToString("N0");
}
