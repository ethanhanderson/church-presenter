using System.Runtime.InteropServices;

namespace ChurchPresenter.Interop;

/// <summary>
/// Win32 helpers for configuring non-activating topmost output windows and subclassing their HWNDs.
/// </summary>
internal static class NativeWindowInterop
{
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;

    internal const int WsBorder = 0x00800000;
    internal const int WsCaption = 0x00C00000;
    internal const int WsClipChildren = 0x02000000;
    internal const int WsClipSiblings = 0x04000000;
    internal const int WsDlgFrame = 0x00400000;
    internal const int WsMaximizeBox = 0x00010000;
    internal const int WsMinimizeBox = 0x00020000;
    internal const int WsPopup = unchecked((int)0x80000000);
    internal const int WsSysMenu = 0x00080000;
    internal const int WsThickFrame = 0x00040000;
    internal const int WsVisible = 0x10000000;

    internal const int WsExAppWindow = 0x00040000;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExToolWindow = 0x00000080;

    internal const int DwmaWindowCornerPreference = 33;
    internal const uint DwmwcpDoNotRound = 1;
    /// <summary>DWMWCP_ROUND — rounded corners (Windows 11).</summary>
    internal const uint DwmwcpRound = 2;

    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoCopyBits = 0x0100;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpNoRedraw = 0x0008;
    internal const uint SwpFrameChanged = 0x0020;

    internal const int SwShowNoActivate = 4;

    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmKeyDown = 0x0100;
    /// <summary>Posted when a <see cref="RegisterHotKey"/> combination is pressed; delivered to the registering HWND without keyboard focus.</summary>
    internal const uint WmHotkey = 0x0312;
    internal const nint VkEscape = 0x1B;
    internal const uint MaNoActivate = 3;

    internal delegate nint SubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc subclassProc,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc subclassProc,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    /// <summary>Registers a hot key; <see cref="WmHotkey"/> is posted to <paramref name="hWnd"/> when pressed (no focus required).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int index, int newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    /// <summary>
    /// Brings the window with the given HWND to the foreground and gives it keyboard focus.
    /// More reliable than <c>Window.Activate()</c> for same-process windows because it is not
    /// gated by Windows focus-steal prevention.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("dwmapi.dll", SetLastError = true)]
    internal static extern int DwmSetWindowAttribute(
        nint hWnd,
        int attribute,
        in uint value,
        int valueSize);

    internal static nint GetWindowLongPtr(nint hWnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, index)
            : new nint(GetWindowLong32(hWnd, index));
    }

    internal static nint SetWindowLongPtr(nint hWnd, int index, nint value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, value)
            : new nint(SetWindowLong32(hWnd, index, value.ToInt32()));
    }
}