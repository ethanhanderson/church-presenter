
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Output;

/// <summary>Creates and owns fullscreen audience windows from the configured logical output topology.</summary>
public sealed class AudienceWindowService(
    IOutputTopologyService topology,
    IServiceProvider services,
    ILiveProductionFacade liveProduction,
    ILogger<AudienceWindowService> logger) : IAudienceWindowService, IDisposable
{
    private readonly IOutputTopologyService _topology = topology ?? throw new ArgumentNullException(nameof(topology));
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly ILogger<AudienceWindowService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Dictionary<OutputWindowTrackingKey, AudienceOutputWindow> _windows = new();
    private readonly ILiveProductionFacade _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
    private bool _disposed;

    /// <inheritdoc />
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        RunOnUiThread(() =>
        {
            OutputTopologySnapshot topology = _topology.GetSnapshot();
            IReadOnlyList<LocalDisplayOutputTarget> requestedTargets = topology.GetLocalDisplayTargets(OutputScreenIds.Main);
            List<LocalDisplayOutputTarget> connectedTargets = requestedTargets
                .Where(static target => target.IsConnected && target.Monitor != null)
                .ToList();

            if (requestedTargets.Count == 0)
            {
                OutputScreenDiagnostics diagnostics = topology.ResolveDiagnostics(OutputScreenIds.Main);
                _logger.LogWarning("Audience output has no mapped local display endpoints. {DiagnosticsMessage}", diagnostics.Message);
                CloseAllCore();
                return;
            }

            if (connectedTargets.Count == 0)
            {
                OutputScreenDiagnostics diagnostics = topology.ResolveDiagnostics(OutputScreenIds.Main);
                _logger.LogWarning("Audience output targets are unavailable. {DiagnosticsMessage}", diagnostics.Message);
                CloseAllCore();
                return;
            }

            OutputWindowTrackingKey[] activeKeys = connectedTargets
                .Select(static target => new OutputWindowTrackingKey(target.ScreenId, target.EndpointId))
                .ToArray();
            foreach (OutputWindowTrackingKey key in _windows.Keys.Except(activeKeys).ToArray())
                CloseWindow(key);

            foreach (LocalDisplayOutputTarget target in connectedTargets)
            {
                AudienceOutputWindow window = GetOrCreateWindow(target);
                bool wasRemapped = window.EndpointId != null && window.MonitorIndex != target.Monitor!.Index;
                window.ConfigureHostTarget(target.ScreenId, target.EndpointId);
                window.ShowOnMonitor(target.Monitor!);
                ReportEndpointFeedback(target, window, isVisible: true, wasRemapped);
            }
        });
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnUiThread(CloseAllCore);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseAllCore();
    }

    private AudienceOutputWindow GetOrCreateWindow(LocalDisplayOutputTarget target)
    {
        OutputWindowTrackingKey key = new(target.ScreenId, target.EndpointId);
        if (_windows.TryGetValue(key, out AudienceOutputWindow? existing))
            return existing;

        var outputPage = _services.GetRequiredService<OutputPage>();
        var window = new AudienceOutputWindow(outputPage);
        window.ConfigureHostTarget(target.ScreenId, target.EndpointId);
        window.Closed += (_, _) => _windows.Remove(key);
        _windows[key] = window;
        return window;
    }

    private void CloseAllCore()
    {
        foreach (OutputWindowTrackingKey key in _windows.Keys.ToArray())
            CloseWindow(key);

        _windows.Clear();
    }

    private void CloseWindow(OutputWindowTrackingKey key)
    {
        if (!_windows.Remove(key, out AudienceOutputWindow? window))
            return;

        try
        {
            ReportEndpointFeedback(key, window, EndpointHealth.Hidden, isVisible: false, wasRemapped: false);
            window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close audience output window for endpoint {EndpointId}.", key.EndpointId);
            window.Dispose();
        }
    }

    private static void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var queue = App.MainWindow?.DispatcherQueue;
        if (queue == null || !queue.TryEnqueue(() => action()))
            action();
    }

    private void ReportEndpointFeedback(
        LocalDisplayOutputTarget target,
        AudienceOutputWindow window,
        bool isVisible,
        bool wasRemapped)
    {
        ReportEndpointFeedback(
            new OutputWindowTrackingKey(target.ScreenId, target.EndpointId),
            window,
            target.Endpoint.Health,
            isVisible,
            wasRemapped,
            target.MonitorIndex);
    }

    private void ReportEndpointFeedback(
        OutputWindowTrackingKey key,
        AudienceOutputWindow window,
        EndpointHealth health,
        bool isVisible,
        bool wasRemapped,
        int? monitorIndex = null)
    {
        _liveProduction.ReportOutputHostFeedback(new OutputHostFrameFeedbackState
        {
            ScreenId = key.ScreenId,
            EndpointId = key.EndpointId,
            IsVisible = isVisible,
            EndpointHealth = health,
            MonitorIndex = monitorIndex ?? window.MonitorIndex,
            WindowId = window.WindowId,
            WasRemapped = wasRemapped,
            Detail = isVisible ? "Audience output window is visible." : "Audience output window is hidden.",
        });
    }

    private readonly record struct OutputWindowTrackingKey(string ScreenId, string EndpointId);
}