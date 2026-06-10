using Microsoft.Win32;

namespace GameTranslateOverlay.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameTranslateOverlay";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\"");
            LogService.Instance.Info($"Startup registry: enabled ({executablePath})");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            LogService.Instance.Info("Startup registry: disabled");
        }
    }
}
