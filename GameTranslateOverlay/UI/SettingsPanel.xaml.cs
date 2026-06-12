using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using GameTranslateOverlay.Core;
using GameTranslateOverlay.Helpers;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.Services;
using WinForms = System.Windows.Forms;

namespace GameTranslateOverlay.UI;

public partial class SettingsPanel : Window
{
    private static readonly SolidColorBrush OcrReadyBrush = new(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly SolidColorBrush OcrMissingBrush = new(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly SolidColorBrush QuotaOkBrush = new(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly SolidColorBrush QuotaLowBrush = new(System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x06));
    private static readonly SolidColorBrush QuotaCriticalBrush = new(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));

    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly Translator _translator;
    private readonly DeepLUsageService _usageService;
    private readonly Action _onSaved;

    private string _textColorHex = "#FFFFFF";
    private string _backgroundColorHex = "#000000";

    public SettingsPanel(
        SettingsService settingsService,
        StartupService startupService,
        Translator translator,
        DeepLUsageService usageService,
        Action onSaved)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _translator = translator;
        _usageService = usageService;
        _onSaved = onSaved;
        InitializeComponent();
        PopulateLanguageCombos();
        LoadFromSettings();
        FontSizeSlider.ValueChanged += (_, _) => FontSizeLabel.Text = FontSizeSlider.Value.ToString("0");
        OpacitySlider.ValueChanged += (_, _) => OpacityLabel.Text = $"{OpacitySlider.Value * 100:0}%";
        Loaded += async (_, _) => await RefreshQuotaDisplayAsync();
    }

    private void PopulateLanguageCombos()
    {
        foreach (var lang in LanguageCatalog.SourceLanguages)
            SourceLangCombo.Items.Add(new LangItem(lang.Code, lang.Label));

        foreach (var lang in LanguageCatalog.TargetLanguages)
            TargetLangCombo.Items.Add(new LangItem(lang.Code, lang.Label));
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;
        ApiKeyBox.Password = s.DeeplApiKey;
        SelectLang(SourceLangCombo, s.SourceLang);
        SelectLang(TargetLangCombo, s.TargetLang);
        TranslateHotkeyBox.Text = s.Hotkeys.Translate;
        PickRegionHotkeyBox.Text = s.Hotkeys.PickRegion;
        DismissHotkeyBox.Text = s.Hotkeys.Dismiss;
        SetTextColor(s.Overlay.TextColor);
        SetBackgroundColor(s.Overlay.BackgroundColor);
        OpacitySlider.Value = s.Overlay.BackgroundOpacity;
        OpacityLabel.Text = $"{s.Overlay.BackgroundOpacity * 100:0}%";
        FontSizeSlider.Value = s.Overlay.FontSize;
        FontSizeLabel.Text = s.Overlay.FontSize.ToString("0");
        AutoHideBox.Text = s.Overlay.AutoHideSeconds.ToString();
        RememberOverlayPositionBox.IsChecked = s.Overlay.RememberOverlayPosition;
        StartWithWindowsBox.IsChecked = s.StartWithWindows;
        LogPathText.Text = LogService.Instance.GetTodayLogPath();
        UpdateOcrStatus();
        SetQuotaStatus("Enter an API key to view quota.", QuotaOkBrush, null);
    }

    private async void OnRefreshQuotaClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        await RefreshQuotaDisplayAsync(forceRefresh: true);

    private async Task RefreshQuotaDisplayAsync(bool forceRefresh = false)
    {
        var key = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetQuotaStatus("Enter an API key to view quota.", QuotaOkBrush, null);
            return;
        }

        SetQuotaStatus("Checking quota...", QuotaOkBrush, null);
        RefreshQuotaLink.IsEnabled = false;

        try
        {
            var usage = await _usageService.GetUsageAsync(key, forceRefresh);
            if (usage is null)
            {
                SetQuotaStatus("Could not load quota.", QuotaLowBrush, null);
                return;
            }

            ApplyQuotaDisplay(usage);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Settings quota refresh failed", ex);
            SetQuotaStatus(ex.Message, QuotaCriticalBrush, null);
        }
        finally
        {
            RefreshQuotaLink.IsEnabled = true;
        }
    }

    private void ApplyQuotaDisplay(DeepLUsageInfo usage)
    {
        var level = DeepLUsageService.GetWarningLevel(usage);
        var brush = level switch
        {
            DeepLQuotaWarningLevel.Exceeded or DeepLQuotaWarningLevel.Critical => QuotaCriticalBrush,
            DeepLQuotaWarningLevel.Low => QuotaLowBrush,
            _ => QuotaOkBrush,
        };

        var suffix = level switch
        {
            DeepLQuotaWarningLevel.Exceeded => " — quota exhausted",
            DeepLQuotaWarningLevel.Critical => " — almost exhausted",
            DeepLQuotaWarningLevel.Low => " — running low",
            _ => null,
        };

        SetQuotaStatus(DeepLUsageService.FormatSummary(usage), brush, suffix);
    }

    private void SetQuotaStatus(string text, SolidColorBrush brush, string? warningSuffix)
    {
        QuotaStatusText.Foreground = brush;
        QuotaStatusText.Text = warningSuffix is null ? text : $"{text}{warningSuffix}";
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = LogService.Instance.LogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Failed to open log folder", ex);
            MessageBox.Show(this, ex.Message, "Log folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetTextColor(string hex)
    {
        _textColorHex = NormalizeHex(hex);
        TextColorHexLabel.Text = _textColorHex;
        TextColorSwatch.Background = TryBrushFromHex(_textColorHex);
    }

    private void SetBackgroundColor(string hex)
    {
        _backgroundColorHex = NormalizeHex(hex);
        BackgroundColorHexLabel.Text = _backgroundColorHex;
        BackgroundColorSwatch.Background = TryBrushFromHex(_backgroundColorHex);
    }

    private void PickTextColor(object sender, RoutedEventArgs e)
    {
        if (!TryPickColor(_textColorHex, out var picked))
            return;

        SetTextColor(picked);
    }

    private void PickBackgroundColor(object sender, RoutedEventArgs e)
    {
        if (!TryPickColor(_backgroundColorHex, out var picked))
            return;

        SetBackgroundColor(picked);
    }

    private static bool TryPickColor(string currentHex, out string hex)
    {
        hex = NormalizeHex(currentHex);
        using var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = DrawingColorFromHex(hex)
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return false;

        hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        return true;
    }

    private static System.Drawing.Color DrawingColorFromHex(string hex)
    {
        hex = NormalizeHex(hex);
        return System.Drawing.Color.FromArgb(
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16));
    }

    private static string NormalizeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "#FFFFFF";

        var trimmed = hex.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;

        if (trimmed.Length == 7)
            return trimmed.ToUpperInvariant();

        return "#FFFFFF";
    }

    private static System.Windows.Media.Brush TryBrushFromHex(string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            return new System.Windows.Media.SolidColorBrush(color);
        }
        catch
        {
            return System.Windows.Media.Brushes.White;
        }
    }

    private void OnSourceLangChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateOcrStatus();

    private void UpdateOcrStatus()
    {
        var source = GetSelectedLang(SourceLangCombo) ?? _settingsService.Current.SourceLang;
        var displayName = OcrEngine.GetLanguageDisplayName(source);

        if (OcrEngine.IsLanguageAvailable(source))
        {
            OcrStatusText.Foreground = OcrReadyBrush;
            OcrStatusText.Text = $"{displayName} OCR ready";
            OcrSettingsLink.Visibility = Visibility.Collapsed;
            return;
        }

        OcrStatusText.Foreground = OcrMissingBrush;
        OcrStatusText.Text = $"Install {displayName} language pack (OCR)";
        OcrSettingsLink.Visibility = Visibility.Visible;
    }

    private void OnOcrSettingsLinkClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:regionlanguage")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Language settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnTestApiClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = ApiKeyBox.Password;
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show(this, "Enter an API key first.", "Test API", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = await _translator.TranslateAsync(key, "EN", "DE", "Hello");
            LogService.Instance.Info($"Settings Test API success: \"{result}\"");
            _usageService.InvalidateCache();
            var usage = await _usageService.GetUsageAsync(key, forceRefresh: true);
            if (usage is not null)
                ApplyQuotaDisplay(usage);
            MessageBox.Show(this, $"OK: {result}", "Test API", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Settings Test API failed", ex);
            MessageBox.Show(this, ex.Message, "Test API", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset all settings to defaults?", "Reset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _settingsService.ResetToDefaults();
        LoadFromSettings();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AutoHideBox.Text.Trim(), out var autoHide) || autoHide < 0)
        {
            MessageBox.Show(this, "Auto-hide must be a non-negative integer.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HotkeyParser.TryParse(TranslateHotkeyBox.Text.Trim(), out _, out var translateError))
        {
            MessageBox.Show(this, translateError, "Hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HotkeyParser.TryParse(PickRegionHotkeyBox.Text.Trim(), out _, out var pickError))
        {
            MessageBox.Show(this, pickError, "Hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HotkeyParser.TryParse(DismissHotkeyBox.Text.Trim(), out _, out var dismissError))
        {
            MessageBox.Show(this, dismissError, "Hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var startWithWindows = StartWithWindowsBox.IsChecked == true;
        var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;

        _settingsService.Update(s =>
        {
            s.DeeplApiKey = ApiKeyBox.Password;
            s.SourceLang = GetSelectedLang(SourceLangCombo) ?? s.SourceLang;
            s.TargetLang = GetSelectedLang(TargetLangCombo) ?? s.TargetLang;
            s.Hotkeys.Translate = TranslateHotkeyBox.Text.Trim();
            s.Hotkeys.PickRegion = PickRegionHotkeyBox.Text.Trim();
            s.Hotkeys.Dismiss = DismissHotkeyBox.Text.Trim();
            s.Overlay.TextColor = _textColorHex;
            s.Overlay.BackgroundColor = _backgroundColorHex;
            s.Overlay.BackgroundOpacity = OpacitySlider.Value;
            s.Overlay.FontSize = FontSizeSlider.Value;
            s.Overlay.AutoHideSeconds = autoHide;
            s.Overlay.RememberOverlayPosition = RememberOverlayPositionBox.IsChecked == true;
            s.StartWithWindows = startWithWindows;
        });

        _startupService.SetEnabled(startWithWindows, exePath);
        LogService.Instance.Info("Settings panel Save clicked");
        _onSaved();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private static void SelectLang(System.Windows.Controls.ComboBox combo, string code)
    {
        foreach (LangItem item in combo.Items)
        {
            if (item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static string? GetSelectedLang(System.Windows.Controls.ComboBox combo) =>
        (combo.SelectedItem as LangItem)?.Code;

    private sealed class LangItem
    {
        public LangItem(string code, string label)
        {
            Code = code;
            Label = label;
        }

        public string Code { get; }
        public string Label { get; }
    }
}




