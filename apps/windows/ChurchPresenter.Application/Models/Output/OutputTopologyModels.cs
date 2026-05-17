using ChurchPresenter.Backend.Output;

namespace ChurchPresenter.Models.Output;

/// <summary>
/// Stable logical screen identifiers used by the backend output track and local-output adapters.
/// </summary>
public static class OutputScreenIds
{
    /// <summary>Main audience/program screen.</summary>
    public const string Main = OutputFeedIds.Audience;

    /// <summary>Dedicated stream output screen.</summary>
    public const string Stream = OutputFeedIds.Stream;

    /// <summary>Dedicated lobby output screen.</summary>
    public const string Lobby = OutputFeedIds.Lobby;

    /// <summary>Stage/confidence screen.</summary>
    public const string Stage = OutputFeedIds.Stage;
}

/// <summary>
/// Runtime local-display target resolved from the current output topology snapshot.
/// </summary>
public sealed record LocalDisplayOutputTarget
{
    /// <summary>Logical screen receiving the target.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Endpoint id for the local display target.</summary>
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>Endpoint snapshot attached to the target.</summary>
    public OutputEndpoint Endpoint { get; init; } = OutputEndpoint.Placeholder("placeholder", "Placeholder");

    /// <summary>Resolved monitor information when the display is currently connected.</summary>
    public MonitorInfoDto? Monitor { get; init; }

    /// <summary>Configured monitor index for local displays, even when missing.</summary>
    public int? MonitorIndex { get; init; }

    /// <summary>True when a connected monitor is available for this target.</summary>
    public bool IsConnected => Monitor != null && Endpoint.Health == EndpointHealth.Connected;
}

/// <summary>
/// Operator-facing diagnostics for one logical output screen.
/// </summary>
public sealed record OutputScreenDiagnostics
{
    /// <summary>Logical screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Operator-facing screen name.</summary>
    public string ScreenName { get; init; } = string.Empty;

    /// <summary>Current screen state summary.</summary>
    public EndpointHealth Health { get; init; } = EndpointHealth.Unknown;

    /// <summary>Mapped endpoint ids for the screen.</summary>
    public IReadOnlyList<string> EndpointIds { get; init; } = Array.Empty<string>();

    /// <summary>Short diagnostics message suitable for settings or logging.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Whether a missing local endpoint may reconnect automatically.</summary>
    public bool CanReconnect { get; init; }
}

/// <summary>
/// Runtime output topology snapshot bridging logical screens, endpoint mappings, and local displays.
/// </summary>
public sealed record OutputTopologySnapshot
{
    /// <summary>Connected local displays keyed by monitor index.</summary>
    public IReadOnlyDictionary<int, MonitorInfoDto> ConnectedDisplays { get; init; } =
        new Dictionary<int, MonitorInfoDto>();

    /// <summary>Logical screens keyed by id.</summary>
    public IReadOnlyDictionary<string, OutputScreen> Screens { get; init; } =
        new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Known endpoints keyed by id.</summary>
    public IReadOnlyDictionary<string, OutputEndpoint> Endpoints { get; init; } =
        new Dictionary<string, OutputEndpoint>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Logical screen-to-endpoint mappings.</summary>
    public IReadOnlyList<ScreenMapping> ScreenMappings { get; init; } = Array.Empty<ScreenMapping>();

    /// <summary>Per-screen diagnostics snapshot.</summary>
    public IReadOnlyDictionary<string, OutputScreenDiagnostics> ScreenDiagnostics { get; init; } =
        new Dictionary<string, OutputScreenDiagnostics>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves one screen's mapping, or an empty mapping when no explicit row exists.</summary>
    public ScreenMapping ResolveMapping(string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);

        return ScreenMappings.FirstOrDefault(mapping =>
                   string.Equals(mapping.ScreenId, screenId, StringComparison.OrdinalIgnoreCase))
               ?? new ScreenMapping { ScreenId = screenId };
    }

    /// <summary>Resolves one screen's diagnostics, or a neutral fallback when missing.</summary>
    public OutputScreenDiagnostics ResolveDiagnostics(string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);

        if (ScreenDiagnostics.TryGetValue(screenId, out OutputScreenDiagnostics? diagnostics))
            return diagnostics;

        return new OutputScreenDiagnostics
        {
            ScreenId = screenId,
            ScreenName = screenId,
            Health = EndpointHealth.Unknown,
            Message = "No output diagnostics are available for this screen.",
        };
    }

    /// <summary>Returns connected or missing local-display targets for the requested screen.</summary>
    public IReadOnlyList<LocalDisplayOutputTarget> GetLocalDisplayTargets(string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);

        return ResolveMapping(screenId)
            .ResolveEndpoints(Endpoints)
            .Where(endpoint => endpoint.Kind == OutputEndpointKind.LocalDisplay)
            .Select(endpoint =>
            {
                int? monitorIndex = ParseMonitorIndex(endpoint.NativeId);
                MonitorInfoDto? monitor = null;
                if (monitorIndex is int value
                    && ConnectedDisplays.TryGetValue(value, out MonitorInfoDto? connectedMonitor))
                {
                    monitor = connectedMonitor;
                }

                return new LocalDisplayOutputTarget
                {
                    ScreenId = screenId,
                    EndpointId = endpoint.Id,
                    Endpoint = endpoint,
                    MonitorIndex = monitorIndex,
                    Monitor = monitor,
                };
            })
            .ToArray();
    }

    private static int? ParseMonitorIndex(string? nativeId) =>
        TryParseMonitorIndex(nativeId, out int index) ? index : null;

    private static bool TryParseMonitorIndex(string? nativeId, out int index)
    {
        if (string.IsNullOrWhiteSpace(nativeId))
        {
            index = -1;
            return false;
        }

        return int.TryParse(nativeId, out index);
    }
}