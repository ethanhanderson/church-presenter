using ChurchPresenter.Interop;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

using WinRT.Interop;

namespace ChurchPresenter.Adapters.Display;

/// <summary>
/// Shows a toggleable monitor-identification overlay for one display at a time.
/// </summary>
public sealed class MonitorIdentifyService : IMonitorIdentifyService, IDisposable
{
    private const nuint MainWindowEscapeSubclassId = 3;

    private readonly IMonitorService _monitors;
    private readonly ILogger<MonitorIdentifyService> _logger;
    private readonly Dictionary<int, MonitorIdentifyWindow> _windows = new();
    private readonly NativeWindowInterop.SubclassProc _mainWindowEscapeSubclassProc;
    private int? _visibleMonitorIndex;
    private bool _disposed;
    private bool _mainWindowEscapeSubclassRegistered;

    public MonitorIdentifyService(IMonitorService monitors, ILogger<MonitorIdentifyService> logger)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mainWindowEscapeSubclassProc = MainWindowEscapeSubclassProc;
    }

    /// <inheritdoc />
    public bool IsIdentificationActive => _visibleMonitorIndex is not null;

    /// <inheritdoc />
    public void ToggleIdentifier(int monitorIndex)
    {
        RunOnUiThread(() =>
        {
            if (_disposed)
                return;

            IReadOnlyList<MonitorInfoDto> monitors = _monitors.GetMonitors();
            if (monitors.Count == 0)
                return;

            foreach (int index in _windows.Keys.Except(monitors.Select(monitor => monitor.Index)).ToList())
                DestroyWindow(index);

            if (_visibleMonitorIndex == monitorIndex)
            {
                HideIdentifiersCore();
                return;
            }

            MonitorInfoDto? targetMonitor = monitors.FirstOrDefault(monitor => monitor.Index == monitorIndex);
            if (targetMonitor == null)
                return;

            HideIdentifiersCore();

            try
            {
                MonitorIdentifyWindow window = GetOrCreateWindow(targetMonitor.Index);
                bool escapeHotKeyOk = window.ShowForMonitor(targetMonitor);
                _visibleMonitorIndex = targetMonitor.Index;
                if (!escapeHotKeyOk)
                    SubscribeMainWindowEscapeHook();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to show identify overlay for monitor {MonitorIndex}.", monitorIndex);
            }
        });
    }

    /// <inheritdoc />
    public void HideIdentifiers()
    {
        RunOnUiThread(HideIdentifiersCore);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnsubscribeMainWindowEscapeHook();
        _visibleMonitorIndex = null;
        foreach (int index in _windows.Keys.ToList())
            DestroyWindow(index);
    }

    private MonitorIdentifyWindow GetOrCreateWindow(int monitorIndex)
    {
        if (_windows.TryGetValue(monitorIndex, out MonitorIdentifyWindow? existing))
            return existing;

        var window = new MonitorIdentifyWindow();
        window.Closed += (_, _) => _windows.Remove(monitorIndex);
        window.EscapeRequested += (_, _) => RunOnUiThread(HideIdentifiersCore);
        _windows[monitorIndex] = window;
        return window;
    }

    private void HideIdentifiersCore()
    {
        UnsubscribeMainWindowEscapeHook();

        foreach (MonitorIdentifyWindow window in _windows.Values)
        {
            try
            {
                window.HideOverlay();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to hide a monitor identify overlay.");
            }
        }

        _visibleMonitorIndex = null;
    }

    /// <summary>
    /// Fallback when <see cref="NativeWindowInterop.RegisterHotKey"/> fails: subclass the main HWND for <see cref="NativeWindowInterop.WmKeyDown"/>.
    /// Keyboard focus may be on the flyout or main content; the identify overlay is <c>WS_EX_NOACTIVATE</c> so it never receives key messages.
    /// </summary>
    private void SubscribeMainWindowEscapeHook()
    {
        if (_mainWindowEscapeSubclassRegistered)
            return;

        if (App.MainWindow is null)
            return;

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        if (hwnd == 0)
            return;

        _ = NativeWindowInterop.SetWindowSubclass(hwnd, _mainWindowEscapeSubclassProc, MainWindowEscapeSubclassId, 0);
        _mainWindowEscapeSubclassRegistered = true;
    }

    private void UnsubscribeMainWindowEscapeHook()
    {
        if (!_mainWindowEscapeSubclassRegistered)
            return;

        if (App.MainWindow is null)
            return;

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        if (hwnd != 0)
            _ = NativeWindowInterop.RemoveWindowSubclass(hwnd, _mainWindowEscapeSubclassProc, MainWindowEscapeSubclassId);

        _mainWindowEscapeSubclassRegistered = false;
    }

    private nint MainWindowEscapeSubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        _ = subclassId;
        _ = referenceData;

        if (message == NativeWindowInterop.WmKeyDown && wParam == NativeWindowInterop.VkEscape && _visibleMonitorIndex is not null)
        {
            Microsoft.UI.Dispatching.DispatcherQueue? dq = App.MainWindow?.DispatcherQueue;
            if (dq is not null)
            {
                if (dq.HasThreadAccess)
                    HideIdentifiersCore();
                else
                    _ = dq.TryEnqueue(HideIdentifiersCore);
            }
            else
            {
                HideIdentifiersCore();
            }

            // Do not forward Escape to WinUI so the flyout does not close on the same key press.
            return new nint(1);
        }

        return NativeWindowInterop.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void DestroyWindow(int monitorIndex)
    {
        if (!_windows.Remove(monitorIndex, out MonitorIdentifyWindow? window))
            return;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close identify overlay for monitor {MonitorIndex}.", monitorIndex);
            window.Dispose();
        }
    }

    private static void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Microsoft.UI.Dispatching.DispatcherQueue? queue = App.MainWindow?.DispatcherQueue;
        if (queue == null || !queue.TryEnqueue(() => action()))
            action();
    }
}