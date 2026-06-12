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
    private CaptureRegionSettings? _anchorRegion;
    private bool _autoHidePaused;
    private bool _pendingTopmostRefresh;
    private bool _rememberOverlayPosition;
    private bool _userHasCustomPosition;

    public OverlayWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoHideTimer.Tick += OnAutoHideTick;
        SourceInitialized += OnSourceInitialized;
        Hide();
    }

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

        _rememberOverlayPosition = overlay.RememberOverlayPosition;

        if (overlay.AutoHideSeconds > 0)
            _autoHideTimer.Interval = TimeSpan.FromSeconds(overlay.AutoHideSeconds);
        else
            _autoHideTimer.Interval = TimeSpan.Zero;

        if (IsVisible)
        {
            EnsureOnTop();
            if (!_autoHidePaused)
                RestartAutoHideTimer();
        }
    }

    public void ShowAtCaptureRegion(CaptureRegionSettings region)
    {
        if (_anchorRegion is null ||
            _anchorRegion.X != region.X ||
            _anchorRegion.Y != region.Y ||
            _anchorRegion.W != region.W ||
            _anchorRegion.H != region.H)
        {
            _userHasCustomPosition = false;
        }

        _anchorRegion = region;
    }

    public void ShowLoadingState()
    {
        _autoHideTimer.Stop();
        SetContent(null, "Translating...");
        SetLoading(true);
        PresentOverlay(reposition: ShouldRepositionToAnchor());
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
        SetLoading(false);
        PresentOverlay(reposition: ShouldRepositionToAnchor());
        RestartAutoHideTimer();
    }

    private bool ShouldRepositionToAnchor() =>
        !_rememberOverlayPosition || !_userHasCustomPosition;

    private void PresentOverlay(bool reposition)
    {
        var firstShow = !IsVisible;
        if (firstShow)
            Show();

        UpdateLayout();
        if (reposition)
            RepositionToAnchor();

        EnsureOnTop();
        if (firstShow)
            _pendingTopmostRefresh = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (IsVisible)
            EnsureOnTop();
    }

    private void EnsureOnTop()
    {
        Topmost = true;
        Win32Helper.ApplyOverlayStyles(this);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_pendingTopmostRefresh)
            return;

        _pendingTopmostRefresh = false;
        EnsureOnTop();
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
        if (_autoHidePaused)
            return;

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

    private void OnDragHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            DragMove();
            if (_rememberOverlayPosition)
                _userHasCustomPosition = true;
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
