using ChurchPresenter.Interop;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Graphics;
using Windows.UI;

namespace ChurchPresenter.Views;

/// <summary>
/// Dedicated audience-output window hosted outside the normal shell so it can be fullscreen,
/// topmost, and non-activating on a selected monitor.
/// </summary>
internal sealed class AudienceOutputWindow : Window, IDisposable
{
    private const nuint MouseActivateSubclassId = 1;

    private readonly UIElement _outputContent;
    private readonly NativeWindowInterop.SubclassProc _subclassProc;
    private bool _disposed;
    private bool _isFullScreen;
    private bool _isInitialized;

    /// <summary>
    /// Initializes a new output window around the supplied WinUI content.
    /// </summary>
    /// <param name="content">The audience output surface to host.</param>
    public AudienceOutputWindow(UIElement content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _outputContent = content;
        Content = CreateBootstrapSurface();
        _subclassProc = WndProc;
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigureWindowChrome();
        RegisterSubclass();
        Closed += OnClosed;

        // Output windows are display-only surfaces. If the window is somehow activated
        // (e.g. programmatic Activate() during first init), immediately return keyboard
        // focus to the main control surface so slide navigation always works there.
        Activated += OnOutputActivated;
    }

    /// <summary>Gets the native HWND for this output window.</summary>
    public nint Hwnd { get; }

    /// <summary>Gets the WinUI AppWindow identity for diagnostics.</summary>
    public string WindowId => AppWindow.Id.ToString() ?? string.Empty;

    /// <summary>Gets the logical screen id currently assigned to this host.</summary>
    public string? ScreenId { get; private set; }

    /// <summary>Gets the endpoint id currently assigned to this host.</summary>
    public string? EndpointId { get; private set; }

    /// <summary>Gets the monitor index currently assigned to the output window.</summary>
    public int MonitorIndex { get; private set; }

    /// <summary>Assigns stable output topology identity to the hosted output surface.</summary>
    /// <param name="screenId">Logical screen id.</param>
    /// <param name="endpointId">Mapped endpoint id.</param>
    public void ConfigureHostTarget(string screenId, string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        ScreenId = screenId;
        EndpointId = endpointId;

        if (_outputContent is IOutputHostTargetAware aware)
            aware.SetOutputHostTarget(new OutputHostTarget(screenId, endpointId, WindowId));
    }

    /// <summary>
    /// Shows or repositions the output window on the supplied monitor without stealing focus.
    /// </summary>
    /// <param name="monitor">The target monitor bounds.</param>
    public void ShowOnMonitor(MonitorInfoDto monitor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(monitor);

        var targetBounds = GetMonitorBounds(monitor);
        MonitorIndex = monitor.Index;

        ApplyBounds(monitor, showWindow: false);

        if (!_isInitialized)
        {
            Activate();
            AttachOutputContent();
            EnsureFullScreenPresenter();
            _isInitialized = true;
        }

        EnsureFullScreenPresenter();
        AppWindow.Show(false);

        // Ensure the main window keeps keyboard focus after all output-window setup
        // (Activate() during first-init and presenter changes can briefly steal it).
        ReturnFocusToMainWindow();
    }

    /// <summary>
    /// Hides the audience output window without destroying the underlying HWND so it can be reused quickly.
    /// </summary>
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isInitialized)
            return;

        AppWindow.Hide();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Closed -= OnClosed;
        _ = NativeWindowInterop.RemoveWindowSubclass(Hwnd, _subclassProc, MouseActivateSubclassId);
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

        uint cornerPreference = NativeWindowInterop.DwmwcpDoNotRound;
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

    private void AttachOutputContent()
    {
        if (!ReferenceEquals(Content, _outputContent))
            Content = _outputContent;
    }

    private void EnsureFullScreenPresenter()
    {
        if (_isFullScreen && AppWindow.Presenter is FullScreenPresenter)
            return;

        AppWindow.SetPresenter(FullScreenPresenter.Create());
        _isFullScreen = true;
    }

    private static Grid CreateBootstrapSurface()
    {
        return new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0x00)),
        };
    }

    private static RectInt32 GetMonitorBounds(MonitorInfoDto monitor) =>
        new(monitor.X, monitor.Y, monitor.Width, monitor.Height);

    private void ApplyBounds(MonitorInfoDto monitor, bool showWindow)
    {
        var rect = GetMonitorBounds(monitor);
        AppWindow.MoveAndResize(rect);
        uint flags =
            NativeWindowInterop.SwpNoActivate |
            NativeWindowInterop.SwpNoOwnerZOrder |
            NativeWindowInterop.SwpFrameChanged;

        // Avoid showing the HWND until WinUI has been activated and the black bootstrap
        // surface is in place; this prevents the transient white restored window flash.
        if (showWindow)
        {
            flags |= DisplayMonitorInterop.SwpShowWindow;
        }
        else
        {
            flags |= NativeWindowInterop.SwpNoRedraw | NativeWindowInterop.SwpNoCopyBits;
        }

        _ = DisplayMonitorInterop.SetWindowPos(
            Hwnd,
            DisplayMonitorInterop.HwndTopMost,
            monitor.X,
            monitor.Y,
            monitor.Width,
            monitor.Height,
            flags);
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

        return NativeWindowInterop.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void OnOutputActivated(object sender, WindowActivatedEventArgs args)
    {
        _ = sender;
        if (args.WindowActivationState != WindowActivationState.Deactivated)
            ReturnFocusToMainWindow();
    }

    /// <summary>
    /// Explicitly returns Win32 keyboard focus to the main window.
    /// Uses <c>SetForegroundWindow</c> rather than <c>Window.Activate()</c> because
    /// same-process HWND calls are never blocked by Windows focus-steal prevention,
    /// whereas <c>Window.Activate()</c> can silently fail after multiple rapid calls.
    /// </summary>
    private static void ReturnFocusToMainWindow()
    {
        if (App.MainWindow == null) return;
        var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        NativeWindowInterop.SetForegroundWindow(mainHwnd);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;
        Dispose();
    }
}

/// <summary>Stable output host target identity supplied by the window service.</summary>
public sealed class OutputHostTarget
{
    /// <summary>Creates an empty host target for XAML metadata tooling.</summary>
    public OutputHostTarget()
    {
    }

    /// <summary>Creates a host target from resolved topology/window identity.</summary>
    public OutputHostTarget(string screenId, string endpointId, string windowId)
    {
        ScreenId = screenId;
        EndpointId = endpointId;
        WindowId = windowId;
    }

    /// <summary>Logical screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Mapped endpoint id.</summary>
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>WinUI WindowId/AppWindow id.</summary>
    public string WindowId { get; init; } = string.Empty;
}

/// <summary>Implemented by output host pages that report frame feedback.</summary>
public interface IOutputHostTargetAware
{
    /// <summary>Assigns the logical screen, endpoint, and window identity for host feedback.</summary>
    void SetOutputHostTarget(OutputHostTarget target);
}