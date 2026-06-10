using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.UI;

public partial class RegionSelector : Window
{
    private System.Windows.Point _start;
    private bool _dragging;

    public event EventHandler<Int32Rect>? RegionSelected;

    public RegionSelector()
    {
        InitializeComponent();
    }

    public void ShowSelector()
    {
        LogService.Instance.Info("RegionSelector ShowSelector");
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        SelectionBox.Visibility = Visibility.Collapsed;
        Show();
        Activate();
        Focus();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        CaptureMouse();
        Canvas.SetLeft(SelectionBox, _start.X);
        Canvas.SetTop(SelectionBox, _start.Y);
        SelectionBox.Width = 0;
        SelectionBox.Height = 0;
        SelectionBox.Visibility = Visibility.Visible;
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
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
            return;

        _dragging = false;
        ReleaseMouseCapture();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            LogService.Instance.Info("RegionSelector cancelled (ESC)");
            _dragging = false;
            Hide();
        }
    }
}
