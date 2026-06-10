using System.Windows;

namespace GameTranslateOverlay.Models;

public sealed class AppSettings
{
    public string DeeplApiKey { get; set; } = string.Empty;
    public string SourceLang { get; set; } = "JA";
    public string TargetLang { get; set; } = "EN-US";
    public HotkeySettings Hotkeys { get; set; } = new();
    public CaptureRegionSettings CaptureRegion { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool PinOverlay { get; set; } = true;
    public bool UseFixedRegion { get; set; }
}

public sealed class HotkeySettings
{
    public string Translate { get; set; } = "F10";
    public string PickRegion { get; set; } = "F9";
}

public sealed class CaptureRegionSettings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public bool IsValid => W >= 4 && H >= 4;

    public static CaptureRegionSettings FromRect(Int32Rect rect) =>
        new() { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height };
}

public sealed class OverlaySettings
{
    public string TextColor { get; set; } = "#F5F5F7";
    public string BackgroundColor { get; set; } = "#0A0A0B";
    public double BackgroundOpacity { get; set; } = 0.92;
    public double FontSize { get; set; } = 12;
    public int AutoHideSeconds { get; set; } = 15;
}

