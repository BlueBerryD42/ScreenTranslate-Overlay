using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace GameTranslateOverlay.Services;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private MenuItem? _quotaMenuItem;
    private MenuItem? _startWithWindowsItem;
    private MenuItem? _pinOverlayItem;
    private MenuItem? _useFixedRegionItem;
    private bool _disposed;

    public TrayService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "GameTranslateOverlay",
            ContextMenu = new ContextMenu()
        };
    }

    public void Initialize(
        bool pinOverlay,
        bool startWithWindows,
        bool useFixedRegion,
        Action togglePinOverlay,
        Action setCaptureRegion,
        Action toggleUseFixedRegion,
        Action toggleStartWithWindows,
        Action openSettings,
        Action exit)
    {
        var menu = _icon.ContextMenu;
        menu!.Items.Clear();

        _pinOverlayItem = CreateCheckItem("Pin Overlay", pinOverlay, togglePinOverlay);
        menu.Items.Add(_pinOverlayItem);
        _useFixedRegionItem = CreateCheckItem("Use fixed region (F10)", useFixedRegion, toggleUseFixedRegion);
        menu.Items.Add(_useFixedRegionItem);
        menu.Items.Add(CreateItem("Set Capture Region", setCaptureRegion));
        _startWithWindowsItem = CreateCheckItem("Start with Windows", startWithWindows, toggleStartWithWindows);
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add(new Separator());
        _quotaMenuItem = new MenuItem { Header = "DeepL quota: —", IsEnabled = false };
        menu.Items.Add(_quotaMenuItem);
        menu.Items.Add(CreateItem("Settings...", openSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Exit", exit));

        _icon.Visibility = Visibility.Visible;
    }

    public void SyncMenuState(bool pinOverlay, bool startWithWindows, bool useFixedRegion)
    {
        if (_pinOverlayItem is not null)
            _pinOverlayItem.IsChecked = pinOverlay;
        if (_startWithWindowsItem is not null)
            _startWithWindowsItem.IsChecked = startWithWindows;
        if (_useFixedRegionItem is not null)
            _useFixedRegionItem.IsChecked = useFixedRegion;
    }

    public void UpdateQuotaDisplay(string summary)
    {
        if (_quotaMenuItem is not null)
            _quotaMenuItem.Header = $"DeepL quota: {summary}";

        _icon.ToolTipText = $"GameTranslateOverlay\n{summary}";
    }

    public void ShowBalloon(string title, string message)
    {
        _icon.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem CreateCheckItem(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _icon.Dispose();
    }
}
