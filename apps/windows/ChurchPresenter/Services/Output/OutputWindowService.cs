
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Materializes dedicated fullscreen output windows for audience and stage roles,
/// keeping hidden windows warm so toggling does not pay repeated creation costs.
/// </summary>
public sealed class OutputWindowService : IOutputWindowService, IDisposable
{
    private readonly ILogger<OutputWindowService> _logger;
    private readonly IMonitorService _monitors;
    private readonly IOutputTopologyService _topology;
    private readonly IServiceProvider _services;

    // ── Audience ─────────────────────────────────────────────────────────────
    private readonly Dictionary<string, AudienceOutputWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Func<IReadOnlyList<LocalDisplayOutputTarget>>? _audienceTargetResolver;
    private DispatcherQueueTimer? _monitorPollTimer;

    // ── Stage ─────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, AudienceOutputWindow> _stageWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stageGate = new();
    private Func<IReadOnlyList<LocalDisplayOutputTarget>>? _stageTargetResolver;
    private DispatcherQueueTimer? _stagePollTimer;

    private bool _disposed;

    public OutputWindowService(
        ILogger<OutputWindowService> logger,
        IMonitorService monitors,
        IOutputTopologyService topology,
        IServiceProvider services)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public void OpenAudience()
    {
        lock (_gate)
        {
            _audienceTargetResolver = ResolveConfiguredAudienceTargets;
        }

        RunOnUiThread(() =>
        {
            EnsureMonitorPollTimer();
            ApplyRequestedWindows();
        });
    }

    /// <inheritdoc />
    public void OpenForMonitors(IReadOnlyList<int> monitorIndices)
    {
        ArgumentNullException.ThrowIfNull(monitorIndices);

        List<int> requested = monitorIndices
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        lock (_gate)
        {
            _audienceTargetResolver = () => ResolveLegacyAudienceTargets(requested);
        }

        RunOnUiThread(() =>
        {
            EnsureMonitorPollTimer();
            ApplyRequestedWindows();
        });
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        lock (_gate)
        {
            _audienceTargetResolver = null;
        }

        RunOnUiThread(() =>
        {
            StopMonitorPollTimer();
            HideAllCore();
        });
    }

    // ── Stage output ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void OpenStage()
    {
        lock (_stageGate)
        {
            _stageTargetResolver = ResolveConfiguredStageTargets;
        }

        RunOnUiThread(() =>
        {
            EnsureStagePollTimer();
            ApplyRequestedStageWindows();
        });
    }

    /// <inheritdoc />
    public void OpenStageForMonitors(IReadOnlyList<int> monitorIndices)
    {
        ArgumentNullException.ThrowIfNull(monitorIndices);

        List<int> requested = monitorIndices
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        lock (_stageGate)
        {
            _stageTargetResolver = () => ResolveLegacyStageTargets(requested);
        }

        RunOnUiThread(() =>
        {
            EnsureStagePollTimer();
            ApplyRequestedStageWindows();
        });
    }

    /// <inheritdoc />
    public void CloseStage()
    {
        lock (_stageGate)
        {
            _stageTargetResolver = null;
        }

        RunOnUiThread(() =>
        {
            StopStagePollTimer();
            HideAllStageCore();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopMonitorPollTimer();
        StopStagePollTimer();
        DestroyAllCore();
        DestroyAllStageCore();
    }

    private void ApplyRequestedWindows()
    {
        if (_disposed)
            return;

        Func<IReadOnlyList<LocalDisplayOutputTarget>>? resolver;
        lock (_gate)
        {
            resolver = _audienceTargetResolver;
        }

        if (resolver == null)
        {
            StopMonitorPollTimer();
            HideAllCore();
            return;
        }

        IReadOnlyList<LocalDisplayOutputTarget> requestedTargets = resolver();
        List<LocalDisplayOutputTarget> validTargets = requestedTargets
            .Where(target => target.IsConnected && target.Monitor != null)
            .ToList();

        if (requestedTargets.Count == 0)
        {
            StopMonitorPollTimer();
            HideAllCore();
            return;
        }

        if (validTargets.Count == 0)
        {
            OutputScreenDiagnostics diagnostics = _topology.GetSnapshot().ResolveDiagnostics(OutputScreenIds.Main);
            _logger.LogWarning(
                "Audience output targets are unavailable. {DiagnosticsMessage}",
                diagnostics.Message);
            HideAllCore();
            return;
        }

        foreach (string endpointId in _windows.Keys.Except(validTargets.Select(target => target.EndpointId), StringComparer.OrdinalIgnoreCase).ToList())
        {
            HideWindow(endpointId);
        }

        foreach (LocalDisplayOutputTarget target in validTargets)
        {
            AudienceOutputWindow window = GetOrCreateWindow(target);
            window.ShowOnMonitor(target.Monitor!);
        }

        if (App.MainWindow is global::ChurchPresenter.MainWindow mainWindow)
            mainWindow.RestoreForegroundFocus();
    }

    // ── Audience helpers ──────────────────────────────────────────────────────

    private AudienceOutputWindow GetOrCreateWindow(LocalDisplayOutputTarget target)
    {
        if (_windows.TryGetValue(target.EndpointId, out AudienceOutputWindow? existing))
            return existing;

        OutputPage outputPage = _services.GetRequiredService<OutputPage>();
        var window = new AudienceOutputWindow(outputPage);
        window.Closed += (_, _) => _windows.Remove(target.EndpointId);
        _windows[target.EndpointId] = window;
        return window;
    }

    private void HideAllCore()
    {
        foreach (string endpointId in _windows.Keys.ToList())
            HideWindow(endpointId);
    }

    private void HideWindow(string endpointId)
    {
        if (!_windows.TryGetValue(endpointId, out AudienceOutputWindow? window))
            return;

        try { window.Hide(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to hide audience output window for endpoint {EndpointId}.", endpointId);
        }
    }

    private void DestroyAllCore()
    {
        foreach (string endpointId in _windows.Keys.ToList())
            DestroyWindow(endpointId);
    }

    private void DestroyWindow(string endpointId)
    {
        if (!_windows.Remove(endpointId, out AudienceOutputWindow? window))
            return;

        try { window.Close(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close audience output window for endpoint {EndpointId}.", endpointId);
            window.Dispose();
        }
    }

    private void EnsureMonitorPollTimer()
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue == null)
            return;

        _monitorPollTimer ??= CreateMonitorPollTimer(queue);
        _monitorPollTimer.Start();
    }

    private DispatcherQueueTimer CreateMonitorPollTimer(DispatcherQueue queue)
    {
        DispatcherQueueTimer timer = queue.CreateTimer();
        timer.IsRepeating = true;
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => ApplyRequestedWindows();
        return timer;
    }

    private void StopMonitorPollTimer()
    {
        _monitorPollTimer?.Stop();
    }

    // ── Stage helpers ─────────────────────────────────────────────────────────

    private void ApplyRequestedStageWindows()
    {
        if (_disposed)
            return;

        Func<IReadOnlyList<LocalDisplayOutputTarget>>? resolver;
        lock (_stageGate)
        {
            resolver = _stageTargetResolver;
        }

        if (resolver == null)
        {
            StopStagePollTimer();
            HideAllStageCore();
            return;
        }

        IReadOnlyList<LocalDisplayOutputTarget> requestedTargets = resolver();
        List<LocalDisplayOutputTarget> validTargets = requestedTargets
            .Where(target => target.IsConnected && target.Monitor != null)
            .ToList();

        if (requestedTargets.Count == 0)
        {
            StopStagePollTimer();
            HideAllStageCore();
            return;
        }

        if (validTargets.Count == 0)
        {
            OutputScreenDiagnostics diagnostics = _topology.GetSnapshot().ResolveDiagnostics(OutputScreenIds.Stage);
            _logger.LogWarning(
                "Stage output targets are unavailable. {DiagnosticsMessage}",
                diagnostics.Message);
            HideAllStageCore();
            return;
        }

        foreach (string endpointId in _stageWindows.Keys.Except(validTargets.Select(target => target.EndpointId), StringComparer.OrdinalIgnoreCase).ToList())
            HideStageWindow(endpointId);

        foreach (LocalDisplayOutputTarget target in validTargets)
        {
            AudienceOutputWindow window = GetOrCreateStageWindow(target);
            window.ShowOnMonitor(target.Monitor!);
        }

        if (App.MainWindow is global::ChurchPresenter.MainWindow mainWindow)
            mainWindow.RestoreForegroundFocus();
    }

    private AudienceOutputWindow GetOrCreateStageWindow(LocalDisplayOutputTarget target)
    {
        if (_stageWindows.TryGetValue(target.EndpointId, out AudienceOutputWindow? existing))
            return existing;

        StageOutputPage stagePage = _services.GetRequiredService<StageOutputPage>();
        var window = new AudienceOutputWindow(stagePage);
        window.Closed += (_, _) => _stageWindows.Remove(target.EndpointId);
        _stageWindows[target.EndpointId] = window;
        return window;
    }

    private void HideAllStageCore()
    {
        foreach (string endpointId in _stageWindows.Keys.ToList())
            HideStageWindow(endpointId);
    }

    private void HideStageWindow(string endpointId)
    {
        if (!_stageWindows.TryGetValue(endpointId, out AudienceOutputWindow? window))
            return;

        try { window.Hide(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to hide stage output window for endpoint {EndpointId}.", endpointId);
        }
    }

    private void DestroyAllStageCore()
    {
        foreach (string endpointId in _stageWindows.Keys.ToList())
            DestroyStageWindow(endpointId);
    }

    private void DestroyStageWindow(string endpointId)
    {
        if (!_stageWindows.Remove(endpointId, out AudienceOutputWindow? window))
            return;

        try { window.Close(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close stage output window for endpoint {EndpointId}.", endpointId);
            window.Dispose();
        }
    }

    private void EnsureStagePollTimer()
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue == null)
            return;

        _stagePollTimer ??= CreateStagePollTimer(queue);
        _stagePollTimer.Start();
    }

    private DispatcherQueueTimer CreateStagePollTimer(DispatcherQueue queue)
    {
        DispatcherQueueTimer timer = queue.CreateTimer();
        timer.IsRepeating = true;
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => ApplyRequestedStageWindows();
        return timer;
    }

    private void StopStagePollTimer()
    {
        _stagePollTimer?.Stop();
    }

    private IReadOnlyList<LocalDisplayOutputTarget> ResolveConfiguredAudienceTargets() =>
        _topology.GetSnapshot().GetLocalDisplayTargets(OutputScreenIds.Main);

    private IReadOnlyList<LocalDisplayOutputTarget> ResolveConfiguredStageTargets() =>
        _topology.GetSnapshot().GetLocalDisplayTargets(OutputScreenIds.Stage);

    private IReadOnlyList<LocalDisplayOutputTarget> ResolveLegacyAudienceTargets(IReadOnlyList<int> monitorIndices) =>
        ResolveLegacyTargets(OutputScreenIds.Main, monitorIndices);

    private IReadOnlyList<LocalDisplayOutputTarget> ResolveLegacyStageTargets(IReadOnlyList<int> monitorIndices) =>
        ResolveLegacyTargets(OutputScreenIds.Stage, monitorIndices);

    private IReadOnlyList<LocalDisplayOutputTarget> ResolveLegacyTargets(string screenId, IReadOnlyList<int> monitorIndices)
    {
        Dictionary<int, MonitorInfoDto> connectedDisplays = _monitors
            .GetMonitors()
            .ToDictionary(monitor => monitor.Index);

        return monitorIndices
            .Distinct()
            .OrderBy(index => index)
            .Select(index =>
            {
                bool isConnected = connectedDisplays.TryGetValue(index, out MonitorInfoDto? monitor);
                return new LocalDisplayOutputTarget
                {
                    ScreenId = screenId,
                    EndpointId = $"legacy-local-display:{index}",
                    Endpoint = new ChurchPresenter.Backend.Output.OutputEndpoint
                    {
                        Id = $"legacy-local-display:{index}",
                        Name = isConnected ? monitor!.Name : $"Display {index + 1} (Missing)",
                        Kind = ChurchPresenter.Backend.Output.OutputEndpointKind.LocalDisplay,
                        Capabilities = ChurchPresenter.Backend.Output.EndpointCapability.LocalWindow
                                       | ChurchPresenter.Backend.Output.EndpointCapability.UserToggle,
                        Health = isConnected
                            ? ChurchPresenter.Backend.Output.EndpointHealth.Connected
                            : ChurchPresenter.Backend.Output.EndpointHealth.Missing,
                        NativeId = index.ToString(),
                    },
                    MonitorIndex = index,
                    Monitor = monitor,
                };
            })
            .ToArray();
    }

    private static void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var queue = App.MainWindow?.DispatcherQueue;
        if (queue == null || !queue.TryEnqueue(() => action()))
            action();
    }
}