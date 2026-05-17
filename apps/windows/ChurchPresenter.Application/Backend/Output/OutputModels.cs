using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Output;

/// <summary>
/// Logical output screen category.
/// </summary>
public enum OutputScreenKind
{
    /// <summary>Audience-facing rendered output.</summary>
    Audience,

    /// <summary>Stage or confidence monitor rendered output.</summary>
    Stage,
}

/// <summary>
/// Delivery endpoint category for a logical screen.
/// </summary>
public enum OutputEndpointKind
{
    /// <summary>Local Windows display endpoint.</summary>
    LocalDisplay,

    /// <summary>Configured endpoint with no active monitor or transport.</summary>
    Placeholder,

    /// <summary>Recording or stream consumer endpoint.</summary>
    Capture,

    /// <summary>Future NDI network endpoint.</summary>
    Ndi,

    /// <summary>Future SDI or hardware endpoint.</summary>
    Sdi,

    /// <summary>Endpoint that mirrors another target.</summary>
    Mirror,
}

/// <summary>
/// Endpoint capabilities that do not share one generic enabled flag.
/// </summary>
[Flags]
public enum EndpointCapability
{
    /// <summary>No declared capability.</summary>
    None = 0,

    /// <summary>Can host a local fullscreen window.</summary>
    LocalWindow = 1 << 0,

    /// <summary>Represents setup without a connected device.</summary>
    Placeholder = 1 << 1,

    /// <summary>Can carry audio.</summary>
    Audio = 1 << 2,

    /// <summary>Can consume frames for recording or streaming.</summary>
    Capture = 1 << 3,

    /// <summary>Can carry transparent output.</summary>
    Transparency = 1 << 4,

    /// <summary>Can carry key/fill output.</summary>
    KeyFill = 1 << 5,

    /// <summary>Can mirror another output.</summary>
    Mirror = 1 << 6,

    /// <summary>Has a fixed refresh mode.</summary>
    FixedRefresh = 1 << 7,

    /// <summary>Can be toggled by the operator.</summary>
    UserToggle = 1 << 8,
}

/// <summary>
/// Current endpoint health as reported by configuration, topology, or host feedback.
/// </summary>
public enum EndpointHealth
{
    /// <summary>Health has not yet been measured.</summary>
    Unknown,

    /// <summary>Endpoint is available.</summary>
    Connected,

    /// <summary>Endpoint is intentionally a placeholder.</summary>
    Placeholder,

    /// <summary>Configured endpoint is not currently available.</summary>
    Missing,

    /// <summary>Endpoint exists but is hidden.</summary>
    Hidden,

    /// <summary>Endpoint failed and needs recovery.</summary>
    Failed,
}

/// <summary>
/// Logical render target independent from monitor, window, or transport.
/// </summary>
public sealed record OutputScreen
{
    /// <summary>Stable logical screen id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing screen name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Audience or stage screen kind.</summary>
    public OutputScreenKind Kind { get; init; }

    /// <summary>Nominal render size for this screen.</summary>
    public PixelSize RenderSize { get; init; } = PixelSize.FullHd;

    /// <summary>Alpha mode reserved for future broadcast/keyed outputs.</summary>
    public RenderAlphaMode AlphaMode { get; init; } = RenderAlphaMode.Opaque;
}

/// <summary>
/// Physical, virtual, or future transport destination for a logical screen.
/// </summary>
public sealed record OutputEndpoint
{
    /// <summary>Stable endpoint id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing endpoint name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Endpoint kind.</summary>
    public OutputEndpointKind Kind { get; init; }

    /// <summary>Declared endpoint capabilities.</summary>
    public EndpointCapability Capabilities { get; init; }

    /// <summary>Current endpoint health.</summary>
    public EndpointHealth Health { get; init; } = EndpointHealth.Unknown;

    /// <summary>Optional native monitor/window/device identity.</summary>
    public string? NativeId { get; init; }

    /// <summary>Creates a placeholder endpoint with no monitor or window.</summary>
    public static OutputEndpoint Placeholder(string id, string name)
    {
        return new OutputEndpoint
        {
            Id = id,
            Name = name,
            Kind = OutputEndpointKind.Placeholder,
            Capabilities = EndpointCapability.Placeholder,
            Health = EndpointHealth.Placeholder,
        };
    }
}

/// <summary>
/// Links one logical screen to zero, one, or many endpoints.
/// </summary>
public sealed record ScreenMapping
{
    /// <summary>Logical screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Endpoint ids receiving this screen.</summary>
    public IReadOnlyList<string> EndpointIds { get; init; } = Array.Empty<string>();

    /// <summary>Returns mapped endpoints that are known in the supplied catalog.</summary>
    public IReadOnlyList<OutputEndpoint> ResolveEndpoints(IReadOnlyDictionary<string, OutputEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return EndpointIds
            .Select(endpointId => endpoints.TryGetValue(endpointId, out OutputEndpoint? endpoint) ? endpoint : null)
            .Where(static endpoint => endpoint != null)
            .Cast<OutputEndpoint>()
            .ToArray();
    }
}

/// <summary>
/// Routes one layer on one audience screen.
/// </summary>
public sealed record LayerRoute
{
    /// <summary>Layer included in the route.</summary>
    public OutputLayerKind LayerKind { get; init; }

    /// <summary>Whether the layer is enabled on this screen.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Optional theme or layout variant for this screen and layer.</summary>
    public string? ThemeVariantId { get; init; }

    /// <summary>Optional mask definition applied when this route targets the mask layer.</summary>
    public string? MaskId { get; init; }
}

/// <summary>
/// Screen-specific layer routing inside a Look.
/// </summary>
public sealed record ScreenLayerRouting
{
    /// <summary>Audience screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Per-layer routing rules.</summary>
    public IReadOnlyList<LayerRoute> Layers { get; init; } = Array.Empty<LayerRoute>();

    /// <summary>Returns whether the layer is enabled for this screen.</summary>
    public bool Routes(OutputLayerKind layerKind)
    {
        LayerRoute? route = Layers.FirstOrDefault(layer => layer.LayerKind == layerKind);
        return route?.IsEnabled ?? true;
    }

    /// <summary>Returns the theme variant for a layer, if one is configured.</summary>
    public string? ResolveThemeVariant(OutputLayerKind layerKind)
    {
        return Layers.FirstOrDefault(layer => layer.LayerKind == layerKind)?.ThemeVariantId;
    }

    /// <summary>Returns the configured mask id for this screen, if one is assigned.</summary>
    public string? ResolveMaskId()
    {
        return Layers.FirstOrDefault(layer => layer.LayerKind == OutputLayerKind.Mask)?.MaskId;
    }
}

/// <summary>
/// Named group of layers that can be cleared together.
/// </summary>
public sealed record ClearGroup
{
    /// <summary>Stable clear group id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing clear group name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Layers included in the clear group.</summary>
    public IReadOnlySet<OutputLayerKind> Layers { get; init; } = new HashSet<OutputLayerKind>();
}

/// <summary>
/// Command payload for clearing one or more layers.
/// </summary>
public sealed record ClearCommand
{
    /// <summary>Specific layers to clear.</summary>
    public IReadOnlySet<OutputLayerKind> Layers { get; init; } = new HashSet<OutputLayerKind>();

    /// <summary>Optional clear group id used to derive layers.</summary>
    public string? ClearGroupId { get; init; }
}

/// <summary>
/// Per-screen layer-routing preset for audience outputs.
/// </summary>
public sealed record LookPreset
{
    /// <summary>Stable Look id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing Look name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Audience-screen routing rules.</summary>
    public IReadOnlyList<ScreenLayerRouting> ScreenRoutes { get; init; } = Array.Empty<ScreenLayerRouting>();

    /// <summary>Clear groups exposed by this Look.</summary>
    public IReadOnlyList<ClearGroup> ClearGroups { get; init; } = Array.Empty<ClearGroup>();

    /// <summary>Resolves the route for an audience screen, defaulting to all layers enabled.</summary>
    public ScreenLayerRouting ResolveRoute(string screenId)
    {
        return ScreenRoutes.FirstOrDefault(route =>
                string.Equals(route.ScreenId, screenId, StringComparison.OrdinalIgnoreCase))
            ?? new ScreenLayerRouting { ScreenId = screenId };
    }
}