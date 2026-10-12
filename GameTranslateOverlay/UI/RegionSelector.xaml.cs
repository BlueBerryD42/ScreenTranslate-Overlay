using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GameTranslateOverlay.Models;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.UI;

public partial class RegionSelector : Window
{
    private const int VkEscape = 0x1B;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private System.Windows.Point _start;
    private bool _dragging;
    private string _currentSourceLang = "JA";
    private string _currentTargetLang = "EN-US";
    private DispatcherTimer? _escTimer;

    public event EventHandler<Int32Rect>? RegionSelected;
    public event EventHandler<(string? source, string? target)>? LanguageChanged;

    public RegionSelector()
    {
        InitializeComponent();
        PreviewKeyDown += OnSelectorPreviewKeyDown;
    }

    private void OnSelectorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelSelection();
        }
    }

    private void ForceFocus()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var fg = GetForegroundWindow();
                var fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
                var curThread = GetCurrentThreadId();

                if (fgThread != curThread && fgThread != 0)
                {
                    AttachThreadInput(curThread, fgThread, true);
                    SetForegroundWindow(hwnd);
                    AttachThreadInput(curThread, fgThread, false);
                }
                else
                {
                    SetForegroundWindow(hwnd);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"RegionSelector ForceFocus failed: {ex.Message}");
        }

        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void StartEscWatcher()
    {
        _escTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _escTimer.Tick -= OnEscTick;
        _escTimer.Tick += OnEscTick;
        _escTimer.Start();
    }

    private void StopEscWatcher() => _escTimer?.Stop();

    private void OnEscTick(object? sender, EventArgs e)
    {
        // If context menu is open, let standard WPF close the menu first
        if (LangSwitchButton.ContextMenu?.IsOpen == true)
            return;

        if ((GetAsyncKeyState(VkEscape) & 0x8000) != 0)
        {
            StopEscWatcher();
            CancelSelection();
        }
    }

    public void CancelSelection()
    {
        LogService.Instance.Info("RegionSelector cancelled");
        StopEscWatcher();
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        DimensionBadge.Visibility = Visibility.Collapsed;
        Hide();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        CancelSelection();
    }

    public void ShowSelector(CaptureRegionSettings? existingRegion = null, string? sourceLang = null, string? targetLang = null)
    {
        LogService.Instance.Info("RegionSelector ShowSelector");
        if (!string.IsNullOrWhiteSpace(sourceLang))
            _currentSourceLang = sourceLang;
        if (!string.IsNullOrWhiteSpace(targetLang))
            _currentTargetLang = targetLang;

        LangBadgeText.Text = $"{_currentSourceLang} \u2192 {_currentTargetLang}";

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        SelectionBox.Visibility = Visibility.Collapsed;
        DimensionBadge.Visibility = Visibility.Collapsed;

        InstructionBanner.Visibility = Visibility.Visible;
        UpdateLayout();
        var bannerWidth = InstructionBanner.ActualWidth > 0 ? InstructionBanner.ActualWidth : 460;
        Canvas.SetLeft(InstructionBanner, Math.Max(20, (Width - bannerWidth) / 2));
        Canvas.SetTop(InstructionBanner, 32);

        if (existingRegion is not null && existingRegion.IsValid)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var lx = (existingRegion.X / dpi.DpiScaleX) - Left;
            var ly = (existingRegion.Y / dpi.DpiScaleY) - Top;
            var lw = existingRegion.W / dpi.DpiScaleX;
            var lh = existingRegion.H / dpi.DpiScaleY;

            Canvas.SetLeft(SavedRegionBox, lx);
            Canvas.SetTop(SavedRegionBox, ly);
            SavedRegionBox.Width = lw;
            SavedRegionBox.Height = lh;
            SavedRegionBox.Visibility = Visibility.Visible;

            Canvas.SetLeft(SavedRegionLabel, lx);
            Canvas.SetTop(SavedRegionLabel, Math.Max(4, ly - 20));
            SavedRegionLabel.Visibility = Visibility.Visible;
        }
        else
        {
            SavedRegionBox.Visibility = Visibility.Collapsed;
            SavedRegionLabel.Visibility = Visibility.Collapsed;
        }

        Show();
        ForceFocus();
        StartEscWatcher();
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

        LangSwitchButton.ContextMenu = menu;
        menu.PlacementTarget = LangSwitchButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // If clicking inside the instruction banner / button, ignore canvas dragging
        var bannerPos = e.GetPosition(InstructionBanner);
        if (bannerPos.X >= 0 && bannerPos.X <= InstructionBanner.ActualWidth &&
            bannerPos.Y >= 0 && bannerPos.Y <= InstructionBanner.ActualHeight)
        {
            return;
        }

        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        CaptureMouse();
        InstructionBanner.Visibility = Visibility.Collapsed;
        SavedRegionBox.Visibility = Visibility.Collapsed;
        SavedRegionLabel.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(SelectionBox, _start.X);
        Canvas.SetTop(SelectionBox, _start.Y);
        SelectionBox.Width = 0;
        SelectionBox.Height = 0;
        SelectionBox.Visibility = Visibility.Visible;
        DimensionBadge.Visibility = Visibility.Collapsed;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;

        var current = e.GetPosition(RootCanvas);
        var x = Math.Min(_start.X, current.X);
        var y = Math.Min(_start.Y, current.Y);
        var w = Math.Abs(current.X - _start.X);
        var h = Math.Abs(current.Y - _start.Y);

        Canvas.SetLeft(SelectionBox, x);
        Canvas.SetTop(SelectionBox, y);
        SelectionBox.Width = w;
        SelectionBox.Height = h;

        var dpi = VisualTreeHelper.GetDpi(this);
        var physW = (int)Math.Round(w * dpi.DpiScaleX);
        var physH = (int)Math.Round(h * dpi.DpiScaleY);
        DimensionText.Text = $"{physW} \u00d7 {physH} px";
        DimensionBadge.Visibility = Visibility.Visible;

        var badgeLeft = x + w - 85;
        if (badgeLeft < x)
            badgeLeft = x;
        var badgeTop = y + h + 8;
        if (badgeTop + 30 > Height)
            badgeTop = Math.Max(0, y - 28);

        Canvas.SetLeft(DimensionBadge, badgeLeft);
        Canvas.SetTop(DimensionBadge, badgeTop);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
            return;

        _dragging = false;
        ReleaseMouseCapture();
        DimensionBadge.Visibility = Visibility.Collapsed;
        StopEscWatcher();
        Hide();

        var x = (int)Canvas.GetLeft(SelectionBox);
        var y = (int)Canvas.GetTop(SelectionBox);
        var w = (int)SelectionBox.Width;
        var h = (int)SelectionBox.Height;

        if (w < 4 || h < 4)
            return;

        var logicalX = (int)(Left + x);
        var logicalY = (int)(Top + y);

        var dpi = VisualTreeHelper.GetDpi(this);
        var physicalX = (int)Math.Round((Left + x) * dpi.DpiScaleX);
        var physicalY = (int)Math.Round((Top + y) * dpi.DpiScaleY);
        var physicalW = (int)Math.Max(1, Math.Round(w * dpi.DpiScaleX));
        var physicalH = (int)Math.Max(1, Math.Round(h * dpi.DpiScaleY));

        LogService.Instance.Info(
            $"RegionSelector region confirmed: logical x={logicalX}, y={logicalY}, w={w}, h={h}; " +
            $"physical x={physicalX}, y={physicalY}, w={physicalW}, h={physicalH} " +
            $"(dpiScale={dpi.DpiScaleX:F2}x{dpi.DpiScaleY:F2})");
        RegionSelected?.Invoke(this, new Int32Rect(physicalX, physicalY, physicalW, physicalH));
    }
}
