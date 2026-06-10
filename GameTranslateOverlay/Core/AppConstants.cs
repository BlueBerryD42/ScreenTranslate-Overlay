using System.IO;

namespace GameTranslateOverlay.Core;

public static class AppConstants
{
    public const string AppName = "GameTranslateOverlay";
    public const double DefaultFontSize = 12;
    public const double MinFontSize = 10;
    public const double MaxFontSize = 14;
    public const int SettingsPanelWidth = 320;

    public static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    public static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "config.json");
}
