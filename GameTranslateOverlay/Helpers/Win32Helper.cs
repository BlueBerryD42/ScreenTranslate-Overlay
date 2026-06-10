using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GameTranslateOverlay.Helpers;

public static class Win32Helper
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static void SetWindowLongPtrValue(IntPtr hWnd, int nIndex, IntPtr newLong)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, nIndex, newLong);
        else
            SetWindowLong32(hWnd, nIndex, newLong.ToInt32());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    public static void ApplyOverlayStyles(Window window, bool pinOverlay)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt32();
        exStyle |= WsExLayered | WsExToolWindow;
        if (pinOverlay)
            exStyle &= ~WsExTransparent;
        else
            exStyle |= WsExTransparent;

        SetWindowLongPtrValue(hwnd, GwlExStyle, new IntPtr(exStyle));

        var insertAfter = pinOverlay ? HwndTopmost : HwndNotTopmost;
        SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
    }
}
