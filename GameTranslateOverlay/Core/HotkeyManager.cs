using System.Runtime.InteropServices;
using System.Windows.Interop;
using GameTranslateOverlay.Helpers;
using GameTranslateOverlay.Services;

namespace GameTranslateOverlay.Core;

public sealed class HotkeyManager : IDisposable
{
    public const int HotkeyPickRegion = 1;
    public const int HotkeyTranslate = 2;
    public const int HotkeyDismiss = 3;

    private const int WmHotkey = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private bool _disposed;

    public event EventHandler<int>? HotkeyPressed;

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Unable to create HwndSource for hotkeys.");
        _source.AddHook(WndProc);
    }

    public bool Register(string pickRegionHotkey, string translateHotkey, string dismissHotkey, out string error)
    {
        UnregisterAll();
        error = string.Empty;

        if (!HotkeyParser.TryParse(pickRegionHotkey, out var pick, out var pickError))
        {
            error = $"Pick region hotkey: {pickError}";
            LogService.Instance.Warn($"Hotkey register failed: {error}");
            return false;
        }

        if (!HotkeyParser.TryParse(translateHotkey, out var translate, out var translateError))
        {
            error = $"Translate hotkey: {translateError}";
            LogService.Instance.Warn($"Hotkey register failed: {error}");
            return false;
        }

        if (!HotkeyParser.TryParse(dismissHotkey, out var dismiss, out var dismissError))
        {
            error = $"Dismiss hotkey: {dismissError}";
            LogService.Instance.Warn($"Hotkey register failed: {error}");
            return false;
        }

        if (!RegisterHotKey(_hwnd, HotkeyPickRegion, pick.Modifiers, pick.VirtualKey))
        {
            error = "Failed to register pick-region hotkey (may be in use).";
            LogService.Instance.Warn($"Hotkey register failed: {error}");
            return false;
        }

        if (!RegisterHotKey(_hwnd, HotkeyTranslate, translate.Modifiers, translate.VirtualKey))
        {
            UnregisterHotKey(_hwnd, HotkeyPickRegion);
            error = "Failed to register translate hotkey (may be in use).";
            LogService.Instance.Warn($"Hotkey register failed: {error}");
            return false;
        }

        if (!RegisterHotKey(_hwnd, HotkeyDismiss, dismiss.Modifiers, dismiss.VirtualKey))
        {
            UnregisterHotKey(_hwnd, HotkeyPickRegion);
            UnregisterHotKey(_hwnd, HotkeyTranslate);
            error = "Failed to register dismiss hotkey (may be in use).";
            LogService.Instance.Warn($"Hotkey register failed: {error}");
            return false;
        }

        LogService.Instance.Info(
            $"Hotkeys registered: pick=\"{pickRegionHotkey}\", translate=\"{translateHotkey}\", dismiss=\"{dismissHotkey}\"");
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            HotkeyPressed?.Invoke(this, wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        UnregisterHotKey(_hwnd, HotkeyPickRegion);
        UnregisterHotKey(_hwnd, HotkeyTranslate);
        UnregisterHotKey(_hwnd, HotkeyDismiss);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
