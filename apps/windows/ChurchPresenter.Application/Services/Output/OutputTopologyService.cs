using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Provides the current machine's connected local displays as candidate output endpoints.
/// </summary>
public interface ILocalDisplayCatalogService
{
    /// <summary>Returns connected local displays in stable monitor order.</summary>
    IReadOnlyList<MonitorInfoDto> GetDisplays();
}

/// <summary>
/// Resolves the machine-local output topology that bridges settings, displays, and backend contracts.
/// </summary>
public interface IOutputTopologyService
{
    /// <summary>Audience screens surfaced in the Looks UI and backend audience registry.</summary>
    IReadOnlyList<OutputFeedDefinition> AudienceScreens { get; }

    /// <summary>Builds the latest topology snapshot from connected displays and saved settings.</summary>
    OutputTopologySnapshot GetSnapshot();
}

/// <summary>
/// Compatibility output-topology adapter that introduces logical screens and endpoint diagnostics
/// without forcing an immediate WinUI rewrite.
/// </summary>
public sealed class OutputTopologyService(
    ISettingsService settings,
    ILocalDisplayCatalogService displays) : IOutputTopologyService
{
    private const string StreamPlaceholderEndpointId = "placeholder:stream";
    private const string LobbyPlaceholderEndpointId = "placeholder:lobby";

    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILocalDisplayCatalogService _displays = displays ?? throw new ArgumentNullException(nameof(displays));

    /// <inheritdoc />
    public IReadOnlyList<OutputFeedDefinition> AudienceScreens => OutputRoutingDefaults.BuiltInFeeds;

    /// <inheritdoc />
    public OutputTopologySnapshot GetSnapshot()
    {
        IReadOnlyList<MonitorInfoDto> connectedDisplays = _displays.GetDisplays();
        Dictionary<int, MonitorInfoDto> connectedByIndex = connectedDisplays.ToDictionary(display => display.Index);

        IReadOnlyList<int> audienceMonitorIndices = ParseMonitorIds(_settings.Settings.Output.AudienceMonitorIds);
        IReadOnlyList<int> stageMonitorIndices = ParseMonitorIds(_settings.Settings.Output.StageMonitorIds);

        Dictionary<string, OutputScreen> screens = BuildScreens(connectedByIndex, audienceMonitorIndices, stageMonitorIndices);
        Dictionary<string, OutputEndpoint> endpoints = connectedDisplays.ToDictionary(
            display => GetLocalDisplayEndpointId(display.Index),
            CreateConnectedDisplayEndpoint,
            StringComparer.OrdinalIgnoreCase);

        endpoints[StreamPlaceholderEndpointId] = CreatePlaceholderEndpoint(
            StreamPlaceholderEndpointId,
            "Stream Placeholder",
            EndpointCapability.Placeholder | EndpointCapability.Capture | EndpointCapability.UserToggle);
        endpoints[LobbyPlaceholderEndpointId] = CreatePlaceholderEndpoint(
            LobbyPlaceholderEndpointId,
            "Lobby Placeholder",
            EndpointCapability.Placeholder | EndpointCapability.UserToggle | EndpointCapability.Mirror);

        AddMissingDisplayEndpoints(endpoints, audienceMonitorIndices.Concat(stageMonitorIndices));

        IReadOnlyList<ScreenMapping> mappings =
        [
            CreateScreenMapping(OutputScreenIds.Main, audienceMonitorIndices),
            new ScreenMapping { ScreenId = OutputScreenIds.Stream, EndpointIds = [StreamPlaceholderEndpointId] },
            new ScreenMapping { ScreenId = OutputScreenIds.Lobby, EndpointIds = [LobbyPlaceholderEndpointId] },
            CreateScreenMapping(OutputScreenIds.Stage, stageMonitorIndices),
        ];

        Dictionary<string, OutputScreenDiagnostics> diagnostics = screens.Values.ToDictionary(
            screen => screen.Id,
            screen => BuildDiagnostics(screen, mappings, endpoints),
            StringComparer.OrdinalIgnoreCase);

        return new OutputTopologySnapshot
        {
            ConnectedDisplays = connectedByIndex,
            Screens = screens,
            Endpoints = endpoints,
            ScreenMappings = mappings,
            ScreenDiagnostics = diagnostics,
        };
    }

    private static Dictionary<string, OutputScreen> BuildScreens(
        IReadOnlyDictionary<int, MonitorInfoDto> connectedByIndex,
        IReadOnlyList<int> audienceMonitorIndices,
        IReadOnlyList<int> stageMonitorIndices)
    {
        PixelSize audienceSize = ResolveMappedDisplaySize(connectedByIndex, audienceMonitorIndices);
        PixelSize stageSize = ResolveMappedDisplaySize(connectedByIndex, stageMonitorIndices);

        return new[]
        {
            new OutputScreen
            {
                Id = OutputScreenIds.Main,
                Name = "Main",
                Kind = OutputScreenKind.Audience,
                RenderSize = audienceSize,
                AlphaMode = RenderAlphaMode.Opaque,
            },
            new OutputScreen
            {
                Id = OutputScreenIds.Stream,
                Name = "Stream",
                Kind = OutputScreenKind.Audience,
                RenderSize = PixelSize.FullHd,
                AlphaMode = RenderAlphaMode.Opaque,
            },
            new OutputScreen
            {
                Id = OutputScreenIds.Lobby,
                Name = "Lobby",
                Kind = OutputScreenKind.Audience,
                RenderSize = PixelSize.FullHd,
                AlphaMode = RenderAlphaMode.Opaque,
            },
            new OutputScreen
            {
                Id = OutputScreenIds.Stage,
                Name = "Stage",
                Kind = OutputScreenKind.Stage,
                RenderSize = stageSize,
                AlphaMode = RenderAlphaMode.Opaque,
            },
        }.ToDictionary(screen => screen.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static PixelSize ResolveMappedDisplaySize(
        IReadOnlyDictionary<int, MonitorInfoDto> connectedByIndex,
        IReadOnlyList<int> monitorIndices)
    {
        foreach (int monitorIndex in monitorIndices)
        {
            if (connectedByIndex.TryGetValue(monitorIndex, out MonitorInfoDto? monitor))
                return new PixelSize(Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
        }

        MonitorInfoDto? primary = connectedByIndex.Values.FirstOrDefault(static monitor => monitor.IsPrimary)
            ?? connectedByIndex.Values.FirstOrDefault();
        if (primary is not null)
            return new PixelSize(Math.Max(1, primary.Width), Math.Max(1, primary.Height));

        return PixelSize.FullHd;
    }

    private static ScreenMapping CreateScreenMapping(string screenId, IEnumerable<int> monitorIndices)
    {
        return new ScreenMapping
        {
            ScreenId = screenId,
            EndpointIds = monitorIndices
                .Distinct()
                .OrderBy(index => index)
                .Select(GetLocalDisplayEndpointId)
                .ToArray(),
        };
    }

    private static OutputEndpoint CreateConnectedDisplayEndpoint(MonitorInfoDto display)
    {
        EndpointCapability capabilities = EndpointCapability.LocalWindow | EndpointCapability.UserToggle;
        if (display.RefreshRate.HasValue)
            capabilities |= EndpointCapability.FixedRefresh;

        return new OutputEndpoint
        {
            Id = GetLocalDisplayEndpointId(display.Index),
            Name = display.Name,
            Kind = OutputEndpointKind.LocalDisplay,
            Capabilities = capabilities,
            Health = EndpointHealth.Connected,
            NativeId = display.Index.ToString(),
        };
    }

    private static OutputEndpoint CreatePlaceholderEndpoint(string id, string name, EndpointCapability capabilities)
    {
        return OutputEndpoint.Placeholder(id, name) with
        {
            Capabilities = capabilities,
        };
    }

    private static void AddMissingDisplayEndpoints(
        IDictionary<string, OutputEndpoint> endpoints,
        IEnumerable<int> configuredIndices)
    {
        foreach (int index in configuredIndices.Distinct().OrderBy(value => value))
        {
            string endpointId = GetLocalDisplayEndpointId(index);
            if (endpoints.ContainsKey(endpointId))
                continue;

            endpoints[endpointId] = new OutputEndpoint
            {
                Id = endpointId,
                Name = $"Display {index + 1} (Missing)",
                Kind = OutputEndpointKind.LocalDisplay,
                Capabilities = EndpointCapability.LocalWindow | EndpointCapability.UserToggle,
                Health = EndpointHealth.Missing,
                NativeId = index.ToString(),
            };
        }
    }

    private static OutputScreenDiagnostics BuildDiagnostics(
        OutputScreen screen,
        IReadOnlyList<ScreenMapping> mappings,
        IReadOnlyDictionary<string, OutputEndpoint> endpoints)
    {
        ScreenMapping mapping = mappings.FirstOrDefault(candidate =>
                                  string.Equals(candidate.ScreenId, screen.Id, StringComparison.OrdinalIgnoreCase))
                              ?? new ScreenMapping { ScreenId = screen.Id };
        IReadOnlyList<OutputEndpoint> resolvedEndpoints = mapping.ResolveEndpoints(endpoints);

        if (resolvedEndpoints.Count == 0)
        {
            return new OutputScreenDiagnostics
            {
                ScreenId = screen.Id,
                ScreenName = screen.Name,
                Health = EndpointHealth.Hidden,
                Message = "No endpoints are mapped to this screen.",
            };
        }

        if (resolvedEndpoints.All(endpoint => endpoint.Health == EndpointHealth.Placeholder))
        {
            return new OutputScreenDiagnostics
            {
                ScreenId = screen.Id,
                ScreenName = screen.Name,
                Health = EndpointHealth.Placeholder,
                EndpointIds = resolvedEndpoints.Select(endpoint => endpoint.Id).ToArray(),
                Message = "This screen is currently backed by a placeholder endpoint.",
            };
        }

        if (resolvedEndpoints.Any(endpoint => endpoint.Health == EndpointHealth.Missing))
        {
            return new OutputScreenDiagnostics
            {
                ScreenId = screen.Id,
                ScreenName = screen.Name,
                Health = EndpointHealth.Missing,
                EndpointIds = resolvedEndpoints.Select(endpoint => endpoint.Id).ToArray(),
                Message = "One or more mapped local displays are missing. The screen will reconnect automatically when the endpoint returns.",
                CanReconnect = true,
            };
        }

        return new OutputScreenDiagnostics
        {
            ScreenId = screen.Id,
            ScreenName = screen.Name,
            Health = EndpointHealth.Connected,
            EndpointIds = resolvedEndpoints.Select(endpoint => endpoint.Id).ToArray(),
            Message = $"{resolvedEndpoints.Count} endpoint{(resolvedEndpoints.Count == 1 ? string.Empty : "s")} connected.",
        };
    }

    private static IReadOnlyList<int> ParseMonitorIds(IEnumerable<string> monitorIds) =>
        monitorIds
            .Select(id => int.TryParse(id, out int index) ? index : -1)
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

    private static string GetLocalDisplayEndpointId(int monitorIndex) => $"local-display:{monitorIndex}";
}