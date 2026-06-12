using System.IO;
using System.Windows;
using GameTranslateOverlay.Core;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.UI;

namespace GameTranslateOverlay.Services;

public sealed class TranslationCoordinator
{
    private enum PendingRegionAction
    {
        None,
        SaveFixed,
        QuickTranslate,
    }

    private readonly SettingsService _settingsService;
    private readonly OcrEngine _ocrEngine;
    private readonly Translator _translator;
    private readonly DeepLUsageService _usageService;
    private readonly OverlayWindow _overlayWindow;
    private readonly TrayService _trayService;
    private readonly RegionSelector _regionSelector;

    private const int MaxConsecutiveEmptyOcr = 3;
    private static readonly TimeSpan MinTranslateInterval = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan EmptyOcrBlockDuration = TimeSpan.FromSeconds(5);

    private bool _busy;
    private bool _busyNotified;
    private int _consecutiveEmptyOcr;
    private DateTime _lastPipelineStartUtc = DateTime.MinValue;
    private DateTime _blockedUntilUtc = DateTime.MinValue;
    private PendingRegionAction _pendingRegionAction;

    public TranslationCoordinator(
        SettingsService settingsService,
        OcrEngine ocrEngine,
        Translator translator,
        DeepLUsageService usageService,
        OverlayWindow overlayWindow,
        TrayService trayService,
        RegionSelector regionSelector)
    {
        _settingsService = settingsService;
        _ocrEngine = ocrEngine;
        _translator = translator;
        _usageService = usageService;
        _overlayWindow = overlayWindow;
        _trayService = trayService;
        _regionSelector = regionSelector;
        _regionSelector.RegionSelected += OnRegionSelected;
    }

    public async Task CheckQuotaAtStartupAsync()
    {
        var apiKey = _settingsService.Current.DeeplApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        var usage = await _usageService.GetUsageAsync(apiKey).ConfigureAwait(true);
        if (usage is null)
            return;

        UpdateTrayQuotaDisplay(usage);
        _usageService.TryEmitWarning(usage, _trayService.ShowBalloon);
    }

    public void ApplySettingsToUi()
    {
        _overlayWindow.ApplySettings(_settingsService.Current);
    }

    public void BeginPickFixedRegion()
    {
        LogService.Instance.Info("BeginPickFixedRegion");
        _pendingRegionAction = PendingRegionAction.SaveFixed;
        _regionSelector.ShowSelector();
    }

    public void RunTranslateHotkey()
    {
        if (!TryEnterTranslatePipeline(out var blockReason))
        {
            if (blockReason is not null)
                _trayService.ShowBalloon("GameTranslateOverlay", blockReason);
            return;
        }

        var settings = _settingsService.Current;
        if (settings.UseFixedRegion && settings.CaptureRegion.IsValid)
        {
            LogService.Instance.Info("RunTranslateHotkey: mode=fixed region");
            _ = RunTranslatePipelineAsync(settings.CaptureRegion);
            return;
        }

        LogService.Instance.Info("RunTranslateHotkey: mode=quick (region select)");
        _pendingRegionAction = PendingRegionAction.QuickTranslate;
        _regionSelector.ShowSelector();
    }

    public void DismissOverlay()
    {
        if (_overlayWindow.IsVisible)
        {
            LogService.Instance.Info("Overlay dismissed via hotkey");
            _overlayWindow.Dismiss();
        }
    }

    private void OnRegionSelected(object? sender, Int32Rect region)
    {
        var action = _pendingRegionAction;
        _pendingRegionAction = PendingRegionAction.None;
        LogService.Instance.Info(
            $"Region selected: action={action}, x={region.X}, y={region.Y}, w={region.Width}, h={region.Height}");

        switch (action)
        {
            case PendingRegionAction.SaveFixed:
                _settingsService.Update(s =>
                {
                    s.CaptureRegion.X = region.X;
                    s.CaptureRegion.Y = region.Y;
                    s.CaptureRegion.W = region.Width;
                    s.CaptureRegion.H = region.Height;
                    s.UseFixedRegion = true;
                });
                _trayService.SyncMenuState(
                    _settingsService.Current.SourceLang,
                    _settingsService.Current.TargetLang,
                    _settingsService.Current.UseFixedRegion);
                _trayService.ShowBalloon(
                    "GameTranslateOverlay",
                    $"Fixed capture region saved ({region.Width}x{region.Height}). Press F10 to translate this area.");
                break;
            case PendingRegionAction.QuickTranslate:
                if (!TryEnterTranslatePipeline(out var blockReason))
                {
                    if (blockReason is not null)
                        _trayService.ShowBalloon("GameTranslateOverlay", blockReason);
                    break;
                }

                _ = RunTranslatePipelineAsync(CaptureRegionSettings.FromRect(region));
                break;
        }
    }

    private bool TryEnterTranslatePipeline(out string? blockReason)
    {
        blockReason = null;
        var now = DateTime.UtcNow;

        if (now < _blockedUntilUtc)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((_blockedUntilUtc - now).TotalSeconds));
            blockReason = $"Too many empty OCR attempts. Wait {seconds}s before retrying.";
            LogService.Instance.Info($"Translate pipeline blocked: cooldown ({seconds}s remaining)");
            return false;
        }

        if (_busy)
        {
            LogService.Instance.Info("Translate pipeline skipped: busy");
            if (!_busyNotified)
            {
                _busyNotified = true;
                blockReason = "Still translating…";
            }

            return false;
        }

        if (now - _lastPipelineStartUtc < MinTranslateInterval)
        {
            LogService.Instance.Info("Translate pipeline skipped: rate limit");
            return false;
        }

        return true;
    }

    private async Task RunTranslatePipelineAsync(CaptureRegionSettings region)
    {

        if (!region.IsValid)
        {
            LogService.Instance.Warn("Translate pipeline aborted: invalid region");
            _trayService.ShowBalloon("GameTranslateOverlay", "Draw a valid capture region to translate.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settingsService.Current.DeeplApiKey))
        {
            LogService.Instance.Warn("Translate pipeline aborted: API key missing");
            _trayService.ShowBalloon("GameTranslateOverlay", "Configure your DeepL API key in Settings.");
            return;
        }

        if (!await EnsureQuotaAllowsTranslateAsync().ConfigureAwait(true))
            return;

        var sourceLang = _settingsService.Current.SourceLang;
        if (!OcrEngine.IsLanguageAvailable(sourceLang))
        {
            LogService.Instance.Warn($"Translate pipeline aborted: OCR language unavailable ({sourceLang})");
            _trayService.ShowBalloon("GameTranslateOverlay", OcrEngine.GetAvailabilityMessage(sourceLang));
            return;
        }

        LogService.Instance.Info(
            $"Translate pipeline start: region x={region.X}, y={region.Y}, w={region.W}, h={region.H}");
        _busy = true;
        _busyNotified = false;
        _lastPipelineStartUtc = DateTime.UtcNow;
        _overlayWindow.ShowAtCaptureRegion(region);
        _overlayWindow.ShowLoadingState();

        string? capturePath = null;
        string? ocrImagePath = null;
        try
        {
            capturePath = ScreenCapture.CaptureRegionToTempFile(region.X, region.Y, region.W, region.H);
            LogService.Instance.Info($"Screen capture saved: {capturePath}");

            ocrImagePath = OcrImagePreprocessor.Prepare(capturePath);
            var ocrRaw = await _ocrEngine.RecognizeAsync(ocrImagePath, sourceLang).ConfigureAwait(true);
            var rawPreview = PreviewText(ocrRaw, 80);
            LogService.Instance.Info($"OCR raw: length={ocrRaw.Length}, preview=\"{rawPreview}\"");

            var ocrText = OcrTextNormalizer.Normalize(ocrRaw, sourceLang);
            var normalizedPreview = PreviewText(ocrText, 80);
            LogService.Instance.Info($"OCR normalized: length={ocrText.Length}, preview=\"{normalizedPreview}\"");

            if (string.IsNullOrWhiteSpace(ocrText))
            {
                SaveDebugCapture(capturePath, "last_capture.png");
                if (ocrImagePath is not null && ocrImagePath != capturePath)
                    SaveDebugCapture(ocrImagePath, "last_capture_prep.png");

                LogService.Instance.Warn("Translate pipeline: no text detected after OCR");
                _consecutiveEmptyOcr++;
                if (_consecutiveEmptyOcr >= MaxConsecutiveEmptyOcr)
                {
                    _consecutiveEmptyOcr = 0;
                    _blockedUntilUtc = DateTime.UtcNow.Add(EmptyOcrBlockDuration);
                    ShowOverlayMessage(
                        $"No text detected. Wait {(int)EmptyOcrBlockDuration.TotalSeconds}s before retrying.");
                }
                else
                {
                    ShowOverlayMessage("No text detected in capture region.");
                }

                return;
            }

            _consecutiveEmptyOcr = 0;
            _overlayWindow.SetContent(ocrText, null);

            var translated = await _translator.TranslateAsync(
                _settingsService.Current.DeeplApiKey,
                _settingsService.Current.SourceLang,
                _settingsService.Current.TargetLang,
                ocrText).ConfigureAwait(true);

            LogService.Instance.Info($"Translation success: output length={translated.Length}");
            _overlayWindow.SetContent(ocrText, translated);
            _overlayWindow.ShowOverlay();
            await RefreshQuotaAfterTranslateAsync().ConfigureAwait(true);
        }
        catch (DeepL.QuotaExceededException ex)
        {
            LogService.Instance.Error("Translate pipeline failed: quota exceeded", ex);
            _usageService.InvalidateCache();
            _trayService.ShowBalloon(
                "DeepL quota",
                "DeepL quota exhausted. Upgrade your plan or wait for the next billing period.");
            ShowOverlayMessage("DeepL quota exhausted.", notifyTray: false);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Translate pipeline failed", ex);
            ShowOverlayMessage($"Error: {ex.Message}");
        }
        finally
        {
            _overlayWindow.SetLoading(false);
            _busy = false;
            _busyNotified = false;
            TryDeleteTempFile(capturePath);
            if (ocrImagePath is not null && ocrImagePath != capturePath)
                TryDeleteTempFile(ocrImagePath);
        }
    }

    private async Task<bool> EnsureQuotaAllowsTranslateAsync()
    {
        var usage = await _usageService.GetUsageAsync(_settingsService.Current.DeeplApiKey).ConfigureAwait(true);
        if (usage is null)
            return true;

        UpdateTrayQuotaDisplay(usage);

        if (DeepLUsageService.GetWarningLevel(usage) == DeepLQuotaWarningLevel.Exceeded)
        {
            LogService.Instance.Warn("Translate pipeline aborted: DeepL quota exhausted");
            _trayService.ShowBalloon(
                "DeepL quota",
                _usageService.GetWarningMessage(usage) ?? "DeepL quota exhausted.");
            return false;
        }

        _usageService.TryEmitWarning(usage, _trayService.ShowBalloon);
        return true;
    }

    private async Task RefreshQuotaAfterTranslateAsync()
    {
        _usageService.InvalidateCache();
        var usage = await _usageService.GetUsageAsync(_settingsService.Current.DeeplApiKey, forceRefresh: true)
            .ConfigureAwait(true);
        if (usage is null)
            return;

        UpdateTrayQuotaDisplay(usage);
        _usageService.TryEmitWarning(usage, _trayService.ShowBalloon);
    }

    private void UpdateTrayQuotaDisplay(DeepLUsageInfo usage)
    {
        _trayService.UpdateQuotaDisplay(DeepLUsageService.FormatSummary(usage));
    }

    private void ShowOverlayMessage(string message, bool notifyTray = true)
    {
        _overlayWindow.SetContent(null, message);
        _overlayWindow.ShowOverlay();
        if (notifyTray)
            _trayService.ShowBalloon("GameTranslateOverlay", message);
    }

    private static void SaveDebugCapture(string sourcePath, string fileName)
    {
        var debugPath = Path.Combine(LogService.Instance.DebugDirectory, fileName);
        LogService.Instance.EnsureDebugDirectory();
        try
        {
            File.Copy(sourcePath, debugPath, overwrite: true);
            LogService.Instance.Warn($"OCR empty; debug capture saved: {debugPath}");
        }
        catch (Exception copyEx)
        {
            LogService.Instance.Warn($"OCR empty; failed to save debug capture ({fileName}): {copyEx.Message}");
        }
    }

    private static void TryDeleteTempFile(string? path)
    {
        if (path is null || !File.Exists(path))
            return;

        try { File.Delete(path); } catch { /* ignore */ }
    }

    private static string PreviewText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxChars)
            return normalized;

        return normalized[..maxChars] + "…";
    }
}
