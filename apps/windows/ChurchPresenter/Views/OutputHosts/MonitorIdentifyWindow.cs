using ChurchPresenter;
using ChurchPresenter.Interop;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Graphics;
using Windows.UI;

namespace ChurchPresenter.Views;

/// <summary>
/// Small non-activating overlay window used to identify a monitor from the settings picker.
/// </summary>
internal sealed class MonitorIdentifyWindow : Window, IDisposable
{
    private const nuint MouseActivateSubclassId = 2;
    /// <summary>Hot key id for plain Escape; <see cref="NativeWindowInterop.RegisterHotKey"/> does not require focus (unlike <see cref="NativeWindowInterop.WmKeyDown"/> on a <c>WS_EX_NOACTIVATE</c> window).</summary>
    private const int IdentifyEscapeHotKeyId = 0x4850;

    private readonly TextBlock _displayIndexText;
    private readonly TextBlock _displayNameText;
    private readonly TextBlock _resolutionText;
    private readonly NativeWindowInterop.SubclassProc _subclassProc;
    private bool _disposed;
    private bool _isInitialized;
    private bool _escapeHotKeyRegistered;

    public MonitorIdentifyWindow()
    {
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Content = CreateRootContent(out _displayIndexText, out _displayNameText, out _resolutionText);
        ApplyForegroundBrushes();
        _subclassProc = WndProc;
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigureWindowChrome();
        RegisterSubclass();
        Closed += OnClosed;
    }

    /// <summary>Gets the native HWND for this identify overlay.</summary>
    public nint Hwnd { get; }

    /// <summary>Raised when Escape should dismiss the overlay (via <see cref="NativeWindowInterop.WmHotkey"/>).</summary>
    internal event EventHandler? EscapeRequested;

    /// <summary>Shows the overlay centered on the supplied monitor.</summary>
    /// <returns><see langword="true"/> if Escape was registered as a global hot key for this HWND; <see langword="false"/> if the caller should use another path (e.g. main-window key hook).</returns>
    public bool ShowForMonitor(MonitorInfoDto monitor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(monitor);

        if (Content is FrameworkElement root && global::ChurchPresenter.App.MainWindow?.Content is FrameworkElement shellRoot)
            root.RequestedTheme = shellRoot.RequestedTheme;

        ApplyForegroundBrushes();

        _displayIndexText.Text = $"Display {monitor.Index + 1}";
        _displayNameText.Text = string.IsNullOrWhiteSpace(monitor.Name) ? $"Display {monitor.Index + 1}" : monitor.Name;
        _resolutionText.Text = $"{monitor.Width}×{monitor.Height}";

        ApplyBounds(monitor);
        AppWindow.Show(false);
        if (!_isInitialized)
            _isInitialized = true;

        return TryRegisterEscapeHotKey();
    }

    /// <summary>Hides the identify overlay without destroying the HWND.</summary>
    public void HideOverlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isInitialized)
            return;

        UnregisterEscapeHotKey();
        AppWindow.Hide();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Closed -= OnClosed;
        UnregisterEscapeHotKey();
        _ = NativeWindowInterop.RemoveWindowSubclass(Hwnd, _subclassProc, MouseActivateSubclassId);
    }

    private bool TryRegisterEscapeHotKey()
    {
        UnregisterEscapeHotKey();

        // No modifier bits — matches a plain Escape press. WM_HOTKEY is delivered to this HWND even though we are non-activating.
        if (!NativeWindowInterop.RegisterHotKey(Hwnd, IdentifyEscapeHotKeyId, fsModifiers: 0, vk: (uint)NativeWindowInterop.VkEscape))
            return false;

        _escapeHotKeyRegistered = true;
        return true;
    }

    private void UnregisterEscapeHotKey()
    {
        if (!_escapeHotKeyRegistered)
            return;

        _ = NativeWindowInterop.UnregisterHotKey(Hwnd, IdentifyEscapeHotKeyId);
        _escapeHotKeyRegistered = false;
    }

    private void ConfigureWindowChrome()
    {
        OverlappedPresenter presenter = AppWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);

        nint style = NativeWindowInterop.GetWindowLongPtr(Hwnd, NativeWindowInterop.GwlStyle);
        style &= ~(NativeWindowInterop.WsCaption |
                   NativeWindowInterop.WsThickFrame |
                   NativeWindowInterop.WsBorder |
                   NativeWindowInterop.WsDlgFrame |
                   NativeWindowInterop.WsSysMenu |
                   NativeWindowInterop.WsMinimizeBox |
                   NativeWindowInterop.WsMaximizeBox);
        style |= NativeWindowInterop.WsPopup |
                 NativeWindowInterop.WsClipChildren |
                 NativeWindowInterop.WsClipSiblings;
        _ = NativeWindowInterop.SetWindowLongPtr(Hwnd, NativeWindowInterop.GwlStyle, style);

        nint extendedStyle = NativeWindowInterop.GetWindowLongPtr(Hwnd, NativeWindowInterop.GwlExStyle);
        extendedStyle |= NativeWindowInterop.WsExNoActivate | NativeWindowInterop.WsExToolWindow;
        extendedStyle &= ~NativeWindowInterop.WsExAppWindow;
        _ = NativeWindowInterop.SetWindowLongPtr(Hwnd, NativeWindowInterop.GwlExStyle, extendedStyle);

        uint cornerPreference = NativeWindowInterop.DwmwcpRound;
        _ = NativeWindowInterop.DwmSetWindowAttribute(
            Hwnd,
            NativeWindowInterop.DwmaWindowCornerPreference,
            in cornerPreference,
            sizeof(uint));
    }

    private void RegisterSubclass()
    {
        _ = NativeWindowInterop.SetWindowSubclass(Hwnd, _subclassProc, MouseActivateSubclassId, 0);
    }

    private void ApplyBounds(MonitorInfoDto monitor)
    {
        // Portrait, near-square (slightly taller than wide). Scaled up from the prior 260×300 card.
        const int width = 390;
        const int height = 450;
        int x = monitor.X + Math.Max(0, (monitor.Width - width) / 2);
        int y = monitor.Y + Math.Max(0, (monitor.Height - height) / 2);

        var rect = new RectInt32(x, y, width, height);
        AppWindow.MoveAndResize(rect);
        _ = DisplayMonitorInterop.SetWindowPos(
            Hwnd,
            DisplayMonitorInterop.HwndTopMost,
            x,
            y,
            width,
            height,
            NativeWindowInterop.SwpNoActivate |
            NativeWindowInterop.SwpNoOwnerZOrder |
            NativeWindowInterop.SwpFrameChanged |
            DisplayMonitorInterop.SwpShowWindow);
    }

    private static Border CreateRootContent(
        out TextBlock displayIndexText,
        out TextBlock displayNameText,
        out TextBlock resolutionText)
    {
        displayIndexText = new TextBlock
        {
            FontSize = 36,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = false,
        };

        displayNameText = new TextBlock
        {
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = false,
        };

        resolutionText = new TextBlock
        {
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = false,
        };

        var stack = new StackPanel
        {
            Spacing = 10,
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { displayIndexText, displayNameText, resolutionText },
        };

        // Single transparent shell: DesktopAcrylicBackdrop fills the window; no second filled "card" layer.
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(28, 32, 28, 32),
            Child = stack,
        };
    }

    private void ApplyForegroundBrushes()
    {
        bool useDarkFallback = ShouldUseDarkFallbackForIdentifyOverlay();
        _displayIndexText.Foreground = ResolveThemeBrush(
            "TextFillColorPrimaryBrush",
            Color.FromArgb(0xFF, 0x00, 0x00, 0x00),
            Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            useDarkFallback);
        _displayNameText.Foreground = ResolveThemeBrush(
            "TextFillColorPrimaryBrush",
            Color.FromArgb(0xFF, 0x00, 0x00, 0x00),
            Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            useDarkFallback);
        _resolutionText.Foreground = ResolveThemeBrush(
            "TextFillColorSecondaryBrush",
            Color.FromArgb(0xFF, 0x5C, 0x5C, 0x5C),
            Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF),
            useDarkFallback);
    }

    private bool ShouldUseDarkFallbackForIdentifyOverlay()
    {
        try
        {
            if (Content is FrameworkElement fe)
            {
                if (fe.RequestedTheme == ElementTheme.Dark)
                    return true;
                if (fe.RequestedTheme == ElementTheme.Light)
                    return false;
            }

            if (Application.Current?.RequestedTheme == ApplicationTheme.Dark)
                return true;
            if (Application.Current?.RequestedTheme == ApplicationTheme.Light)
                return false;
        }
        catch
        {
            // ignore
        }

        return AppThemeHelper.IsSystemDarkTheme();
    }

    /// <summary>
    /// Resolves Fluent theme brushes from merged dictionaries; falls back to light/dark solid colors.
    /// </summary>
    private static Brush ResolveThemeBrush(string key, Color fallbackLight, Color fallbackDark, bool useDarkFallback)
    {
        try
        {
            if (TryLookupThemeBrush(key, out Brush? resolved) && resolved is not null)
                return resolved;
        }
        catch
        {
            // ignore lookup failures during early init
        }

        return new SolidColorBrush(useDarkFallback ? fallbackDark : fallbackLight);
    }

    private static bool TryLookupThemeBrush(string key, out Brush? brush)
    {
        brush = null;
        ResourceDictionary? root = Application.Current?.Resources;
        if (root is null)
            return false;

        return TryLookupThemeBrushRecursive(root, key, out brush) && brush is not null;
    }

    private static bool TryLookupThemeBrushRecursive(ResourceDictionary dict, string key, out Brush? brush)
    {
        brush = null;
        if (dict.ContainsKey(key) && dict[key] is Brush direct)
        {
            brush = direct;
            return true;
        }

        foreach (ResourceDictionary merged in dict.MergedDictionaries)
        {
            if (TryLookupThemeBrushRecursive(merged, key, out brush) && brush is not null)
                return true;
        }

        return false;
    }

    private nint WndProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        _ = subclassId;
        _ = referenceData;

        if (message == NativeWindowInterop.WmMouseActivate)
            return new nint(NativeWindowInterop.MaNoActivate);

        if (message == NativeWindowInterop.WmHotkey && wParam == (nint)IdentifyEscapeHotKeyId)
        {
            EscapeRequested?.Invoke(this, EventArgs.Empty);
            return new nint(1);
        }

        return NativeWindowInterop.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;
        Dispose();
    }
}