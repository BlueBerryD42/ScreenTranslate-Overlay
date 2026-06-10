using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using GameTranslateOverlay.Core;
using GameTranslateOverlay.Services;
using GameTranslateOverlay.UI;

namespace GameTranslateOverlay;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private TrayService? _trayService;
    private HotkeyManager? _hotkeyManager;
    private TranslationCoordinator? _coordinator;
    private DeepLUsageService? _usageService;
    private Translator? _translator;
    private OverlayWindow? _overlayWindow;
    private RegionSelector? _regionSelector;
    private HotkeyHostWindow? _hotkeyHost;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var log = LogService.Instance;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        log.Info($"Application startup (version {version})");

        _settingsService = new SettingsService();
        var loadResult = _settingsService.Load();
        log.Info($"Settings load result: {loadResult}");

        _startupService = new StartupService();
        SyncStartupRegistry();

        var ocrEngine = new OcrEngine();
        _translator = new Translator();
        _usageService = new DeepLUsageService(_translator);

        _overlayWindow = new OverlayWindow();
        _regionSelector = new RegionSelector();
        _trayService = new TrayService();

        _coordinator = new TranslationCoordinator(
            _settingsService,
            ocrEngine,
            _translator,
            _usageService,
            _overlayWindow,
            _trayService,
            _regionSelector);

        _coordinator.ApplySettingsToUi();

        _overlayWindow.SetPinToggleHandler(pinned =>
        {
            _settingsService!.Update(s => s.PinOverlay = pinned);
            _coordinator!.ApplySettingsToUi();
            SyncTrayMenuState();
        });

        _hotkeyHost = new HotkeyHostWindow();
        _hotkeyHost.Show();
        var hwnd = new WindowInteropHelper(_hotkeyHost).Handle;
        _hotkeyManager = new HotkeyManager(hwnd);
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        RegisterHotkeysFromSettings();

        _trayService.Initialize(
            pinOverlay: _settingsService.Current.PinOverlay,
            startWithWindows: _settingsService.Current.StartWithWindows,
            useFixedRegion: _settingsService.Current.UseFixedRegion,
            togglePinOverlay: TogglePinOverlay,
            setCaptureRegion: () => _coordinator!.BeginPickFixedRegion(),
            toggleUseFixedRegion: ToggleUseFixedRegion,
            toggleStartWithWindows: ToggleStartWithWindows,
            openSettings: OpenSettings,
            exit: Shutdown);

        if (string.IsNullOrWhiteSpace(_settingsService.Current.DeeplApiKey))
        {
            log.Warn("DeepL API key is not configured");
            if (MessageBox.Show(
                    "DeepL API key is not configured. Open Settings now?",
                    "GameTranslateOverlay",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                OpenSettings();
            }
        }

        var sourceLang = _settingsService.Current.SourceLang;
        if (!OcrEngine.IsLanguageAvailable(sourceLang))
        {
            log.Warn($"OCR language unavailable at startup: {sourceLang}");
            _trayService.ShowBalloon("GameTranslateOverlay", OcrEngine.GetAvailabilityMessage(sourceLang));
        }

        log.Info("Application startup complete");
        _ = _coordinator.CheckQuotaAtStartupAsync();
    }

    private void OnHotkeyPressed(object? sender, int hotkeyId)
    {
        LogService.Instance.Info($"Hotkey pressed (id={hotkeyId})");
        Dispatcher.Invoke(() =>
        {
            switch (hotkeyId)
            {
                case HotkeyManager.HotkeyPickRegion:
                    _coordinator!.BeginPickFixedRegion();
                    break;
                case HotkeyManager.HotkeyTranslate:
                    _coordinator!.RunTranslateHotkey();
                    break;
            }
        });
    }

    private void OpenSettings()
    {
        var panel = new SettingsPanel(
            _settingsService!,
            _startupService!,
            _translator ?? new Translator(),
            _usageService ?? new DeepLUsageService(_translator ?? new Translator()),
            OnSettingsSaved);
        panel.ShowDialog();
    }

    private void OnSettingsSaved()
    {
        LogService.Instance.Info("Settings saved (from Settings panel)");
        _usageService?.InvalidateCache();
        _usageService?.ResetWarnings();
        _coordinator!.ApplySettingsToUi();
        SyncStartupRegistry();
        RegisterHotkeysFromSettings();
        SyncTrayMenuState();
        _ = _coordinator.CheckQuotaAtStartupAsync();
    }

    private void RegisterHotkeysFromSettings()
    {
        var hotkeys = _settingsService!.Current.Hotkeys;
        if (!_hotkeyManager!.Register(hotkeys.PickRegion, hotkeys.Translate, out var error))
            _trayService!.ShowBalloon("GameTranslateOverlay", error);
    }

    private void TogglePinOverlay()
    {
        _settingsService!.Update(s => s.PinOverlay = !s.PinOverlay);
        _coordinator!.ApplySettingsToUi();
        SyncTrayMenuState();
    }

    private void ToggleUseFixedRegion()
    {
        _settingsService!.Update(s => s.UseFixedRegion = !s.UseFixedRegion);
        SyncTrayMenuState();
    }

    private void ToggleStartWithWindows()
    {
        _settingsService!.Update(s => s.StartWithWindows = !s.StartWithWindows);
        SyncStartupRegistry();
        SyncTrayMenuState();
    }

    private void SyncTrayMenuState()
    {
        _trayService!.SyncMenuState(
            _settingsService!.Current.PinOverlay,
            _settingsService.Current.StartWithWindows,
            _settingsService.Current.UseFixedRegion);
    }

    private void SyncStartupRegistry()
    {
        var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        _startupService!.SetEnabled(_settingsService!.Current.StartWithWindows, exePath);
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        LogService.Instance.Info("Application shutdown");
        _hotkeyManager?.Dispose();
        _trayService?.Dispose();
        _hotkeyHost?.Close();
    }

    private sealed class HotkeyHostWindow : Window
    {
        public HotkeyHostWindow()
        {
            Width = 0;
            Height = 0;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            ShowActivated = false;
            Visibility = Visibility.Hidden;
        }
    }
}
