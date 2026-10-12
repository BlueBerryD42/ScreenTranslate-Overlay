using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GameTranslateOverlay.Helpers;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.UI;

public partial class OverlayWindow : Window
{
    public event EventHandler<(string? source, string? target)>? LanguageChanged;
    public event EventHandler? RetranslateRequested;

    private readonly DispatcherTimer _autoHideTimer;
    private TimeSpan _autoHideDuration = TimeSpan.FromSeconds(15);
    private TimeSpan _autoHideElapsed = TimeSpan.Zero;
    private string _currentSourceLang = "JA";
    private string _currentTargetLang = "EN-US";
    private string _lastSource = string.Empty;
    private string _lastTranslation = string.Empty;
    private CaptureRegionSettings? _anchorRegion;
    private bool _autoHidePaused;
    private bool _isMouseOver;
    private bool _isMenuOpen;
    private bool _pendingTopmostRefresh;
    private bool _rememberOverlayPosition;
    private bool _userHasCustomPosition;

    public string CurrentSourceText => _lastSource;

    public OverlayWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _autoHideTimer.Tick += OnAutoHideTick;
        SourceInitialized += OnSourceInitialized;
        Hide();
    }

    public void ApplySettings(AppSettings settings)
    {
        var overlay = settings.Overlay;
        TranslationText.FontSize = overlay.FontSize;
        TranslationText.Foreground = BrushFromHex(overlay.TextColor);

        _currentSourceLang = settings.SourceLang;
        _currentTargetLang = settings.TargetLang;
        LangBadgeText.Text = $"{settings.SourceLang} \u2192 {settings.TargetLang}";

        var bg = BrushFromHex(overlay.BackgroundColor);
        if (bg is System.Windows.Media.SolidColorBrush solid)
        {
            var c = solid.Color;
            PillBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)(overlay.BackgroundOpacity * 255),
                c.R, c.G, c.B));
        }

        _rememberOverlayPosition = overlay.RememberOverlayPosition;
        _autoHideDuration = overlay.AutoHideSeconds > 0 ? TimeSpan.FromSeconds(overlay.AutoHideSeconds) : TimeSpan.Zero;

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
        var hasSource = !string.IsNullOrWhiteSpace(source);
        SourceText.Text = source ?? string.Empty;
        OrigButton.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Visibility = (hasSource && OrigButton.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

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
        _autoHideElapsed = TimeSpan.Zero;
        if (_autoHidePaused || _autoHideDuration <= TimeSpan.Zero)
        {
            AutoHideProgress.Visibility = Visibility.Collapsed;
            return;
        }

        AutoHideProgress.Value = 100;
        AutoHideProgress.Visibility = Visibility.Visible;
        _autoHideTimer.Start();
    }

    private void OnOverlayMouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOver = true;
    }

    private void OnOverlayMouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOver = false;
    }

    private void OnOrigToggleClick(object sender, RoutedEventArgs e)
    {
        var showOrig = OrigButton.IsChecked == true;
        SourceText.Visibility = (showOrig && !string.IsNullOrWhiteSpace(SourceText.Text))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        _autoHidePaused = KeepButton.IsChecked == true;
        if (_autoHidePaused)
        {
            _autoHideTimer.Stop();
            AutoHideProgress.Visibility = Visibility.Collapsed;
            LogService.Instance.Info("Overlay auto-hide paused (Keep)");
            return;
        }

        LogService.Instance.Info("Overlay auto-hide resumed");
        if (IsVisible)
            RestartAutoHideTimer();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        if (_autoHidePaused || _isMouseOver || _isMenuOpen || _autoHideDuration <= TimeSpan.Zero)
            return;

        _autoHideElapsed += TimeSpan.FromMilliseconds(100);
        var remaining = _autoHideDuration - _autoHideElapsed;
        if (remaining <= TimeSpan.Zero)
        {
            AutoHideProgress.Value = 0;
            Dismiss();
        }
        else
        {
            var pct = (remaining.TotalMilliseconds / _autoHideDuration.TotalMilliseconds) * 100.0;
            AutoHideProgress.Value = Math.Clamp(pct, 0, 100);
        }
    }

    public void Dismiss()
    {
        _autoHideTimer.Stop();
        AutoHideProgress.Visibility = Visibility.Collapsed;
        Hide();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    private void OnLangSwitchClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        // 1. Target Language Submenu
        var targetSubmenu = new MenuItem { Header = "Target Language" };
        foreach (var lang in LanguageCatalog.TargetLanguages)
        {
            var item = new MenuItem
            {
                Header = lang.Label,
                IsCheckable = true,
                IsChecked = lang.Code.Equals(_currentTargetLang, StringComparison.OrdinalIgnoreCase)
            };
            var code = lang.Code;
            item.Click += (_, _) =>
            {
                _currentTargetLang = code;
                LangBadgeText.Text = $"{_currentSourceLang} \u2192 {_currentTargetLang}";
                LanguageChanged?.Invoke(this, (null, code));
            };
            targetSubmenu.Items.Add(item);
        }
        menu.Items.Add(targetSubmenu);

        // 2. Source Language Submenu
        var sourceSubmenu = new MenuItem { Header = "Source Language" };
        foreach (var lang in LanguageCatalog.SourceLanguages)
        {
            var isOcrReady = GameTranslateOverlay.Core.OcrEngine.IsLanguageAvailable(lang.Code);
            var headerText = isOcrReady ? lang.Label : $"{lang.Label} (OCR missing ⚠)";
            var item = new MenuItem
            {
                Header = headerText,
                IsCheckable = true,
                IsChecked = lang.Code.Equals(_currentSourceLang, StringComparison.OrdinalIgnoreCase),
                ToolTip = isOcrReady ? null : $"Windows OCR package is not installed for {lang.Label}."
            };
            var code = lang.Code;
            item.Click += (_, _) =>
            {
                _currentSourceLang = code;
                LangBadgeText.Text = $"{_currentSourceLang} \u2192 {_currentTargetLang}";
                LanguageChanged?.Invoke(this, (code, null));
            };
            sourceSubmenu.Items.Add(item);
        }
        menu.Items.Add(sourceSubmenu);

        menu.Items.Add(new Separator());

        // 3. Quick Swap Item
        var swapItem = new MenuItem { Header = $"\u21C4 Swap ({_currentTargetLang} \u21C4 {_currentSourceLang})" };
        swapItem.Click += (_, _) =>
        {
            var temp = _currentSourceLang;
            _currentSourceLang = _currentTargetLang;
            _currentTargetLang = temp;
            LangBadgeText.Text = $"{_currentSourceLang} \u2192 {_currentTargetLang}";
            LanguageChanged?.Invoke(this, (_currentSourceLang, _currentTargetLang));
        };
        menu.Items.Add(swapItem);

        // 4. Re-translate Item
        if (!string.IsNullOrWhiteSpace(_lastSource))
        {
            var retranslateItem = new MenuItem { Header = "\u21BB Re-translate" };
            retranslateItem.Click += (_, _) => RetranslateRequested?.Invoke(this, EventArgs.Empty);
            menu.Items.Add(retranslateItem);
        }

        LangSwitchButton.ContextMenu = menu;
        menu.PlacementTarget = LangSwitchButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Opened += (_, _) =>
        {
            _isMenuOpen = true;
            _autoHideTimer.Stop();
        };
        menu.Closed += (_, _) =>
        {
            _isMenuOpen = false;
            if (IsVisible && !_autoHidePaused && !_isMouseOver)
                _autoHideTimer.Start();
        };
        menu.IsOpen = true;
    }

    private DispatcherTimer? _copyFeedbackTimer;

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastTranslation))
            return;

        try
        {
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(_lastTranslation, true);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException) when (i < 2)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }

            CopyButton.Content = "Copied!";
            _copyFeedbackTimer?.Stop();
            _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _copyFeedbackTimer.Tick += (_, _) =>
            {
                _copyFeedbackTimer.Stop();
                CopyButton.Content = "Copy";
            };
            _copyFeedbackTimer.Start();
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Clipboard copy failed: {ex.Message}");
        }
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
