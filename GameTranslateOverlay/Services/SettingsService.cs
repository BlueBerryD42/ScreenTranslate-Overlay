using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslateOverlay.Core;
using GameTranslateOverlay.Models;

namespace GameTranslateOverlay.Services;

public enum SettingsLoadResult
{
    Loaded,
    CreatedDefault,
    RestoredFromCorruption,
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppSettings Current { get; private set; } = CreateDefaults();
    public SettingsLoadResult LastLoadResult { get; private set; }

    public SettingsLoadResult Load()
    {
        Directory.CreateDirectory(AppConstants.SettingsDirectory);

        if (!File.Exists(AppConstants.SettingsFilePath))
        {
            Current = CreateDefaults();
            Save();
            LastLoadResult = SettingsLoadResult.CreatedDefault;
            LogService.Instance.Info($"Settings load: {LastLoadResult}");
            return LastLoadResult;
        }

        try
        {
            var json = File.ReadAllText(AppConstants.SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            Current = MergeWithDefaults(loaded);
            LastLoadResult = SettingsLoadResult.Loaded;
            LogService.Instance.Info($"Settings load: {LastLoadResult}");
            return LastLoadResult;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Settings load failed; restoring defaults", ex);
            var backupPath = AppConstants.SettingsFilePath + ".bak";
            try
            {
                if (File.Exists(AppConstants.SettingsFilePath))
                    File.Copy(AppConstants.SettingsFilePath, backupPath, overwrite: true);
            }
            catch
            {
                // ignore backup failures
            }

            Current = CreateDefaults();
            Save();
            LastLoadResult = SettingsLoadResult.RestoredFromCorruption;
            LogService.Instance.Warn($"Settings load: {LastLoadResult} (backup at {backupPath})");
            return LastLoadResult;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppConstants.SettingsDirectory);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(AppConstants.SettingsFilePath, json);
        LogService.Instance.Info("Settings saved to disk");
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
    }

    public void ResetToDefaults()
    {
        Current = CreateDefaults();
        Save();
        LogService.Instance.Info("Settings reset to defaults");
    }

    private static AppSettings CreateDefaults() => new();

    private static AppSettings MergeWithDefaults(AppSettings? loaded)
    {
        var defaults = CreateDefaults();
        if (loaded is null)
            return defaults;

        return new AppSettings
        {
            DeeplApiKey = loaded.DeeplApiKey ?? defaults.DeeplApiKey,
            SourceLang = NormalizeSourceLang(loaded.SourceLang, defaults.SourceLang),
            TargetLang = NormalizeTargetLang(loaded.TargetLang, defaults.TargetLang),
            Hotkeys = MergeHotkeys(loaded.Hotkeys, defaults.Hotkeys),
            CaptureRegion = loaded.CaptureRegion ?? defaults.CaptureRegion,
            Overlay = MergeOverlay(loaded.Overlay, defaults.Overlay),
            StartWithWindows = loaded.StartWithWindows,
            UseFixedRegion = loaded.UseFixedRegion,
        };
    }


    private static string NormalizeSourceLang(string? loaded, string defaultLang)
    {
        if (string.IsNullOrWhiteSpace(loaded))
            return defaultLang;

        if (loaded.Equals("ZH", StringComparison.OrdinalIgnoreCase))
            return "ZH-HANS";

        return loaded;
    }

    private static string NormalizeTargetLang(string? loaded, string defaultLang)
    {
        if (string.IsNullOrWhiteSpace(loaded))
            return defaultLang;

        if (loaded.Equals("EN", StringComparison.OrdinalIgnoreCase))
            return "EN-US";

        if (loaded.Equals("ZH", StringComparison.OrdinalIgnoreCase))
            return "ZH-HANS";

        return loaded;
    }
    private static HotkeySettings MergeHotkeys(HotkeySettings? loaded, HotkeySettings defaults)
    {
        if (loaded is null)
            return defaults;

        return new HotkeySettings
        {
            Translate = string.IsNullOrWhiteSpace(loaded.Translate) ? defaults.Translate : loaded.Translate,
            PickRegion = string.IsNullOrWhiteSpace(loaded.PickRegion) ? defaults.PickRegion : loaded.PickRegion,
            Dismiss = string.IsNullOrWhiteSpace(loaded.Dismiss) ? defaults.Dismiss : loaded.Dismiss,
        };
    }

    private static OverlaySettings MergeOverlay(OverlaySettings? loaded, OverlaySettings defaults)
    {
        if (loaded is null)
            return defaults;

        return new OverlaySettings
        {
            TextColor = string.IsNullOrWhiteSpace(loaded.TextColor) ? defaults.TextColor : loaded.TextColor,
            BackgroundColor = string.IsNullOrWhiteSpace(loaded.BackgroundColor) ? defaults.BackgroundColor : loaded.BackgroundColor,
            BackgroundOpacity = loaded.BackgroundOpacity <= 0 ? defaults.BackgroundOpacity : Math.Min(loaded.BackgroundOpacity, 1),
            FontSize = ClampFontSize(loaded.FontSize),
            AutoHideSeconds = loaded.AutoHideSeconds < 0 ? defaults.AutoHideSeconds : loaded.AutoHideSeconds,
            RememberOverlayPosition = loaded.RememberOverlayPosition,
        };
    }

    private static double ClampFontSize(double value)
    {
        if (value < AppConstants.MinFontSize)
            return AppConstants.DefaultFontSize;
        if (value > AppConstants.MaxFontSize)
            return AppConstants.MaxFontSize;
        return value;
    }
}

