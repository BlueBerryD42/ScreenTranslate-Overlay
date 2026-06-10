using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GameTranslateOverlay.Helpers;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.UI;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _autoHideTimer;
    private string _lastTranslation = string.Empty;
    private Action<bool>? _onPinToggled;
    private CaptureRegionSettings? _anchorRegion;
    private bool _autoHidePaused;

    public OverlayWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoHideTimer.Tick += OnAutoHideTick;
        Hide();
    }

    public void SetPinToggleHandler(Action<bool> onPinToggled) => _onPinToggled = onPinToggled;

    public void ApplySettings(AppSettings settings)
    {
        var overlay = settings.Overlay;
        TranslationText.FontSize = overlay.FontSize;
        TranslationText.Foreground = BrushFromHex(overlay.TextColor);

        var bg = BrushFromHex(overlay.BackgroundColor);
        if (bg is System.Windows.Media.SolidColorBrush solid)
        {
            var c = solid.Color;
            PillBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)(overlay.BackgroundOpacity * 255),
                c.R, c.G, c.B));
        }

        // PinOverlay defaults true so Copy/Pin work; unpinned = click-through to the game (Win32 WS_EX_TRANSPARENT).
        PinButton.IsChecked = settings.PinOverlay;
        Win32Helper.ApplyOverlayStyles(this, settings.PinOverlay);

        if (overlay.AutoHideSeconds > 0)
            _autoHideTimer.Interval = TimeSpan.FromSeconds(overlay.AutoHideSeconds);
        else
            _autoHideTimer.Interval = TimeSpan.Zero;
    }

    public void ShowAtCaptureRegion(CaptureRegionSettings region)
    {
        _anchorRegion = region;
    }

    public void SetLoading(bool loading)
    {
        LoadingBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = !loading;
        CloseButton.IsEnabled = true;
    }

    public void SetContent(string? source, string? translation)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            SourceText.Visibility = Visibility.Collapsed;
            SourceText.Text = string.Empty;
        }
        else
        {
            SourceText.Text = source;
            SourceText.Visibility = Visibility.Visible;
        }

        TranslationText.Text = translation ?? "Translating...";
        _lastTranslation = translation ?? string.Empty;
    }

    public void ShowOverlay()
    {
        if (!IsVisible)
            Show();

        UpdateLayout();
        RepositionToAnchor();

        Topmost = true;
        RestartAutoHideTimer();
    }

    private void RepositionToAnchor()
    {
        if (_anchorRegion is null || !_anchorRegion.IsValid)
            return;

        const double marginDip = 8;

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;

        var region = _anchorRegion;
        var regionLeft = region.X / scaleX;
        var regionTop = region.Y / scaleY;
        var regionWidth = region.W / scaleX;
        var regionHeight = region.H / scaleY;

        var overlayWidth = ActualWidth;
        var overlayHeight = ActualHeight;

        var left = regionLeft + (regionWidth - overlayWidth) / 2;
        var top = regionTop + regionHeight + marginDip;

        var workArea = SystemParameters.WorkArea;

        if (top + overlayHeight > workArea.Bottom)
            top = regionTop - overlayHeight - marginDip;

        left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - overlayWidth));
        top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - overlayHeight));

        Left = left;
        Top = top;

        LogService.Instance.Info(
            $"Overlay positioned: Left={Left:F1}, Top={Top:F1}, Width={overlayWidth:F1}, Height={overlayHeight:F1}");
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer.Stop();
        if (_autoHidePaused)
            return;

        var seconds = _autoHideTimer.Interval.TotalSeconds;
        if (seconds > 0)
            _autoHideTimer.Start();
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        _autoHidePaused = KeepButton.IsChecked == true;
        if (_autoHidePaused)
        {
            _autoHideTimer.Stop();
            LogService.Instance.Info("Overlay auto-hide paused (Keep)");
            return;
        }

        LogService.Instance.Info("Overlay auto-hide resumed");
        if (IsVisible)
            RestartAutoHideTimer();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        Dismiss();
    }

    public void Dismiss()
    {
        _autoHideTimer.Stop();
        Hide();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastTranslation))
            Clipboard.SetText(_lastTranslation);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        var pinned = PinButton.IsChecked == true;
        _onPinToggled?.Invoke(pinned);
        Win32Helper.ApplyOverlayStyles(this, pinned);
    }

    private void OnDragHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (PinButton.IsChecked != true)
            return;

        try
        {
            DragMove();
        }
        catch
        {
            // Mouse released before DragMove completes.
        }
    }

    private static System.Windows.Media.Brush BrushFromHex(string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new System.Windows.Media.SolidColorBrush(color);
        }
        catch
        {
            return System.Windows.Media.Brushes.White;
        }
    }
}
