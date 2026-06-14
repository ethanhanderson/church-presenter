using ChurchPresenter.Interop;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Graphics;
using Windows.UI;

namespace ChurchPresenter.Views;

/// <summary>
/// Dedicated output window hosted outside the normal shell so it can fill a selected monitor
/// as a borderless, topmost, non-activating custom output endpoint.
/// </summary>
internal sealed class AudienceOutputWindow : Window, IDisposable
{
    private const nuint MouseActivateSubclassId = 1;

    private readonly UIElement _outputContent;
    private readonly Grid _hostRoot;
    private readonly NativeWindowInterop.SubclassProc _subclassProc;
    private bool _disposed;
    private bool _isCustomOutputPresenter;
    private bool _isInitialized;
    private bool _isShown;
    private RectInt32? _lastShownBounds;

    /// <summary>
    /// Initializes a new output window around the supplied WinUI content.
    /// </summary>
    /// <param name="content">The audience output surface to host.</param>
    public AudienceOutputWindow(UIElement content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _outputContent = content;
        _hostRoot = CreateOutputHostRoot(content);
        Content = _hostRoot;
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
    public bool ConfigureHostTarget(string screenId, string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        if (string.Equals(ScreenId, screenId, StringComparison.Ordinal)
            && string.Equals(EndpointId, endpointId, StringComparison.Ordinal))
        {
            return false;
        }

        ScreenId = screenId;
        EndpointId = endpointId;

        if (_outputContent is IOutputHostTargetAware aware)
            aware.SetOutputHostTarget(new OutputHostTarget(screenId, endpointId, WindowId));

        return true;
    }

    /// <summary>
    /// Shows or repositions the output window on the supplied monitor without stealing focus.
    /// </summary>
    /// <param name="monitor">The target monitor bounds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ShowOnMonitorAsync(MonitorInfoDto monitor, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(monitor);
        cancellationToken.ThrowIfCancellationRequested();

        var targetBounds = GetMonitorBounds(monitor);
        MonitorIndex = monitor.Index;

        if (CanReuseCurrentPresentation(targetBounds))
            return;

        EnsureOutputContentAttached();
        ApplyBounds(monitor, showWindow: false);
        EnsureCustomOutputPresenter();
        await PrepareOutputContentAsync(cancellationToken).ConfigureAwait(true);
        AppWindow.Show(false);
        ApplyBounds(monitor, showWindow: true);
        _isShown = true;
        _isInitialized = true;
        _lastShownBounds = targetBounds;

        // Ensure the main window keeps keyboard focus after all output-window setup.
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
        _isShown = false;
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
        EnsureCustomOutputPresenter();

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

    private void EnsureOutputContentAttached()
    {
        if (!ReferenceEquals(Content, _hostRoot))
            Content = _hostRoot;

        if (!_hostRoot.Children.Contains(_outputContent))
            _hostRoot.Children.Add(_outputContent);
    }

    private void EnsureCustomOutputPresenter()
    {
        if (_isCustomOutputPresenter && AppWindow.Presenter is OverlappedPresenter)
            return;

        OverlappedPresenter presenter = AppWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        _isCustomOutputPresenter = true;
    }

    private async Task PrepareOutputContentAsync(CancellationToken cancellationToken)
    {
        if (_outputContent is IOutputHostPreparer preparer)
            await preparer.PrepareForDisplayAsync(cancellationToken).ConfigureAwait(true);
    }

    private bool CanReuseCurrentPresentation(RectInt32 targetBounds) =>
        _isInitialized
        && _isShown
        && _isCustomOutputPresenter
        && ReferenceEquals(Content, _hostRoot)
        && _hostRoot.Children.Contains(_outputContent)
        && _lastShownBounds is RectInt32 lastBounds
        && lastBounds.X == targetBounds.X
        && lastBounds.Y == targetBounds.Y
        && lastBounds.Width == targetBounds.Width
        && lastBounds.Height == targetBounds.Height;

    private static Grid CreateOutputHostRoot(UIElement outputContent)
    {
        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0x00)),
        };

        root.Children.Add(outputContent);
        return root;
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

        // Keep the HWND hidden while bounds/presenter state are reconciled; the
        // permanent black host root is already attached as the window content.
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

/// <summary>Implemented by output host pages that can prepare their layer hosts before first display.</summary>
public interface IOutputHostPreparer
{
    /// <summary>Attaches layer surfaces and applies the current frame before the native window is shown.</summary>
    Task PrepareForDisplayAsync(CancellationToken cancellationToken = default);
}