using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameTranslateOverlay.Models;
using Hardcodet.Wpf.TaskbarNotification;

namespace GameTranslateOverlay.Services;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly Dictionary<string, MenuItem> _sourceLangItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MenuItem> _targetLangItems = new(StringComparer.OrdinalIgnoreCase);
    private MenuItem? _quotaMenuItem;
    private MenuItem? _useFixedRegionItem;
    private string _sourceLang = "JA";
    private string _targetLang = "EN-US";
    private string _quotaSummary = "—";
    private bool _disposed;

    public TrayService()
    {
        _icon = new TaskbarIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "GameTranslateOverlay",
            ContextMenu = new ContextMenu()
        };
    }

    public void Initialize(
        string sourceLang,
        string targetLang,
        bool useFixedRegion,
        Action<string> setSourceLang,
        Action<string> setTargetLang,
        Action setCaptureRegion,
        Action toggleUseFixedRegion,
        Action openSettings,
        Action exit)
    {
        var menu = _icon.ContextMenu;
        menu!.Items.Clear();
        _sourceLangItems.Clear();
        _targetLangItems.Clear();

        _useFixedRegionItem = CreateCheckItem("Use fixed region (F10)", useFixedRegion, toggleUseFixedRegion);
        menu.Items.Add(_useFixedRegionItem);
        menu.Items.Add(CreateItem("Set Capture Region", setCaptureRegion));
        menu.Items.Add(BuildLanguageSubmenu("Source language", LanguageCatalog.SourceLanguages, sourceLang, setSourceLang, _sourceLangItems));
        menu.Items.Add(BuildLanguageSubmenu("Target language", LanguageCatalog.TargetLanguages, targetLang, setTargetLang, _targetLangItems));
        menu.Items.Add(new Separator());
        _quotaMenuItem = new MenuItem { Header = "DeepL quota: —", IsEnabled = false };
        menu.Items.Add(_quotaMenuItem);
        menu.Items.Add(CreateItem("Settings...", openSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Exit", exit));

        _sourceLang = sourceLang;
        _targetLang = targetLang;
        _icon.Visibility = Visibility.Visible;
        UpdateTooltip();
    }

    public void SyncMenuState(string sourceLang, string targetLang, bool useFixedRegion)
    {
        _sourceLang = sourceLang;
        _targetLang = targetLang;
        SyncLanguageChecks(_sourceLangItems, sourceLang);
        SyncLanguageChecks(_targetLangItems, targetLang);
        if (_useFixedRegionItem is not null)
            _useFixedRegionItem.IsChecked = useFixedRegion;

        UpdateTooltip();
    }

    public void UpdateQuotaDisplay(string summary)
    {
        _quotaSummary = summary;
        if (_quotaMenuItem is not null)
            _quotaMenuItem.Header = $"DeepL quota: {summary}";

        UpdateTooltip();
    }

    public void ShowBalloon(string title, string message)
    {
        _icon.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    private void UpdateTooltip()
    {
        var source = LanguageCatalog.GetSourceLabel(_sourceLang) ?? _sourceLang;
        var target = LanguageCatalog.GetTargetLabel(_targetLang) ?? _targetLang;
        _icon.ToolTipText = $"GameTranslateOverlay\n{source} → {target}\nDeepL: {_quotaSummary}";
    }

    private static MenuItem BuildLanguageSubmenu(
        string header,
        IReadOnlyList<LanguageOption> languages,
        string selectedCode,
        Action<string> onSelected,
        Dictionary<string, MenuItem> itemMap)
    {
        var submenu = new MenuItem { Header = header };
        foreach (var lang in languages)
        {
            var item = new MenuItem
            {
                Header = lang.Label,
                IsCheckable = true,
                IsChecked = lang.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase),
            };
            var code = lang.Code;
            item.Click += (_, _) => onSelected(code);
            itemMap[code] = item;
            submenu.Items.Add(item);
        }

        return submenu;
    }

    private static void SyncLanguageChecks(Dictionary<string, MenuItem> items, string selectedCode)
    {
        foreach (var (code, item) in items)
            item.IsChecked = code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        const string resourcePath = "pack://application:,,,/Assets/translating16x16.ico";
        var stream = Application.GetResourceStream(new Uri(resourcePath))?.Stream;
        if (stream is null)
            throw new InvalidOperationException($"Tray icon resource not found: {resourcePath}");

        using (stream)
        {
            return new System.Drawing.Icon(stream);
        }
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
