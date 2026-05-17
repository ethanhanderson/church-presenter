using System.Text.Json.Serialization;

using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Models.Output;

/// <summary>
/// Operator-facing category for backend output layers.
/// </summary>
public enum OutputLayerCategory
{
    /// <summary>Presentation or slide content.</summary>
    Presentation,

    /// <summary>Media playback content.</summary>
    Media,

    /// <summary>Generated overlay content.</summary>
    Overlay,

    /// <summary>Input or masking utility layers.</summary>
    Utility,
}

/// <summary>
/// Operator-facing ProPresenter-style clear targets that can expand to one or more backend layers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputClearScope
{
    /// <summary>Audio-bin tracks marked as music/audio tracks.</summary>
    Music,

    /// <summary>Audio-bin items marked as sound effects.</summary>
    AudioEffects,

    /// <summary>The messages overlay layer.</summary>
    Messages,

    /// <summary>The props overlay layer.</summary>
    Props,

    /// <summary>The announcements presentation layer.</summary>
    Announcements,

    /// <summary>The main slide/presentation layer.</summary>
    Presentation,

    /// <summary>Presentation-attached media and independent media-layer content.</summary>
    PresentationMedia,

    /// <summary>Active live video inputs.</summary>
    VideoInput,
}

/// <summary>Built-in output feed identifiers used by the current shell.</summary>
public static class OutputFeedIds
{
    public const string Audience = "audience";
    public const string Main = Audience;
    public const string Stream = "stream";
    public const string Lobby = "lobby";
    public const string Stage = "stage";
}

/// <summary>Built-in output look identifiers.</summary>
public static class OutputLookIds
{
    public const string Default = "default";
    public const string Custom = "custom";
}

/// <summary>
/// One configurable output feed that a routing look can target.
/// The current UI exposes stage and audience feeds, while the model remains open-ended.
/// </summary>
public sealed class OutputFeedDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("screenKind")]
    public string ScreenKind { get; set; } = "Audience";

    [JsonPropertyName("supportsMirroring")]
    public bool SupportsMirroring { get; set; }

    [JsonPropertyName("supportsGrouping")]
    public bool SupportsGrouping { get; set; }

    [JsonPropertyName("supportsEdgeBlend")]
    public bool SupportsEdgeBlend { get; set; }
}

/// <summary>
/// Layer-routing rules for one feed inside a look preset.
/// </summary>
public sealed class OutputLookFeedRouting
{
    [JsonPropertyName("feedId")]
    public string FeedId { get; set; } = string.Empty;

    /// <summary>
    /// Legacy slide routing flag kept for existing portable Show settings and current UI bindings.
    /// Newer layer-aware code should prefer <see cref="Layers"/>.
    /// </summary>
    [JsonPropertyName("slide")]
    public bool Slide { get; set; } = true;

    /// <summary>
    /// Legacy media routing flag kept for existing portable Show settings and current UI bindings.
    /// It also controls audio when no explicit audio route is present.
    /// </summary>
    [JsonPropertyName("media")]
    public bool Media { get; set; } = true;

    /// <summary>Explicit backend-layer routing entries for ProPresenter-style output Looks.</summary>
    [JsonPropertyName("layers")]
    public List<OutputLayerRouteDefinition> Layers { get; set; } = new();

    /// <summary>Returns the route definition for one backend layer, if this feed has one.</summary>
    public OutputLayerRouteDefinition? ResolveLayerRoute(OutputLayerKind layerKind) =>
        Layers.FirstOrDefault(route => OutputRoutingDefaults.LayerIdEquals(route.Layer, layerKind));

    /// <summary>Returns whether the routed <paramref name="layerKind"/> is enabled for this feed.</summary>
    public bool Routes(OutputLayerKind layerKind)
    {
        OutputLayerRouteDefinition? explicitRoute = ResolveLayerRoute(layerKind);
        if (explicitRoute != null)
            return explicitRoute.Enabled;

        return layerKind switch
        {
            OutputLayerKind.Slide => Slide,
            OutputLayerKind.Media => Media,
            OutputLayerKind.Audio => Media,
            _ => true,
        };
    }

    /// <summary>Updates routing for one backend layer while preserving legacy slide/media flags.</summary>
    public void SetRoute(OutputLayerKind layerKind, bool enabled)
    {
        switch (layerKind)
        {
            case OutputLayerKind.Slide:
                Slide = enabled;
                break;
            case OutputLayerKind.Media:
                Media = enabled;
                SetExplicitRoute(OutputLayerKind.Audio, enabled);
                break;
        }

        SetExplicitRoute(layerKind, enabled);
    }

    private void SetExplicitRoute(OutputLayerKind layerKind, bool enabled)
    {
        OutputLayerRouteDefinition? route = ResolveLayerRoute(layerKind);
        if (route == null)
        {
            Layers.Add(new OutputLayerRouteDefinition
            {
                Layer = OutputRoutingDefaults.GetLayerId(layerKind),
                Enabled = enabled,
            });
            return;
        }

        route.Enabled = enabled;
    }

    /// <summary>Creates a deep copy of this routing rule.</summary>
    public OutputLookFeedRouting Clone() =>
        new()
        {
            FeedId = FeedId,
            Slide = Slide,
            Media = Media,
            Layers = Layers.Select(static layer => layer.Clone()).ToList(),
        };
}

/// <summary>
/// Persisted routing flag for one backend output layer.
/// </summary>
public sealed class OutputLayerRouteDefinition
{
    [JsonPropertyName("layer")]
    public string Layer { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional theme/layout variant applied to this layer on this feed.</summary>
    [JsonPropertyName("themeVariantId")]
    public string? ThemeVariantId { get; set; }

    /// <summary>Optional mask definition applied when this is the mask layer route.</summary>
    [JsonPropertyName("maskId")]
    public string? MaskId { get; set; }

    /// <summary>Creates a deep copy of this routing flag.</summary>
    public OutputLayerRouteDefinition Clone() =>
        new()
        {
            Layer = Layer,
            Enabled = Enabled,
            ThemeVariantId = ThemeVariantId,
            MaskId = MaskId,
        };
}

/// <summary>Operator-facing metadata for one backend output layer.</summary>
public sealed record OutputLayerDefinition
{
    /// <summary>Backend layer identity.</summary>
    public OutputLayerKind Kind { get; init; }

    /// <summary>Stable portable id used in JSON settings and clear groups.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name shown in routing and clear controls.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Display grouping for operator-facing controls.</summary>
    public OutputLayerCategory Category { get; init; }

    /// <summary>Whether Looks can route this layer to audience screens.</summary>
    public bool IsRoutable { get; init; } = true;

    /// <summary>Whether clear groups may target this layer.</summary>
    public bool IsClearable { get; init; } = true;
}

/// <summary>Resolved route state for one operator-facing layer toggle.</summary>
public sealed record OutputLayerRouteState
{
    /// <summary>Backend layer identity.</summary>
    public OutputLayerKind Kind { get; init; }

    /// <summary>Stable portable id used in JSON settings and clear groups.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name shown in routing and clear controls.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Display grouping for operator-facing controls.</summary>
    public OutputLayerCategory Category { get; init; }

    /// <summary>Whether the layer is enabled for the current route.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Optional theme/layout variant applied on this route.</summary>
    public string? ThemeVariantId { get; init; }

    /// <summary>Optional mask definition applied on this route.</summary>
    public string? MaskId { get; init; }
}

/// <summary>
/// Named clear-group definition persisted alongside a configurable output Look.
/// </summary>
public sealed class OutputLookClearGroupDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "\uE894";

    [JsonPropertyName("tintEnabled")]
    public bool TintEnabled { get; set; }

    [JsonPropertyName("tintColor")]
    public string? TintColor { get; set; }

    [JsonPropertyName("scopes")]
    public List<OutputClearScope> Scopes { get; set; } = new();

    [JsonPropertyName("stopPresentationTimeline")]
    public bool StopPresentationTimeline { get; set; }

    [JsonPropertyName("stopAnnouncementTimeline")]
    public bool StopAnnouncementTimeline { get; set; }

    /// <summary>
    /// Legacy persisted backend layer ids. New code should prefer <see cref="Scopes"/>, but this remains
    /// populated so older settings and backend Look conversion continue to round-trip.
    /// </summary>
    [JsonPropertyName("layers")]
    public List<string> Layers { get; set; } = new();

    /// <summary>Creates a deep copy of this clear-group definition.</summary>
    public OutputLookClearGroupDefinition Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Icon = Icon,
            TintEnabled = TintEnabled,
            TintColor = TintColor,
            Scopes = [.. Scopes],
            StopPresentationTimeline = StopPresentationTimeline,
            StopAnnouncementTimeline = StopAnnouncementTimeline,
            Layers = [.. Layers],
        };
}

/// <summary>
/// Named layer-routing preset used by the title-bar Looks flyout.
/// </summary>
public sealed class OutputLookDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = OutputLookIds.Default;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("routes")]
    public List<OutputLookFeedRouting> Routes { get; set; } = new();

    [JsonPropertyName("clearGroups")]
    public List<OutputLookClearGroupDefinition> ClearGroups { get; set; } = new();

    /// <summary>
    /// Resolves routing for the requested <paramref name="feedId"/>, defaulting to all layers enabled
    /// so new feeds never go dark by accident.
    /// </summary>
    public OutputLookFeedRouting ResolveRouting(string feedId)
    {
        var route = Routes.FirstOrDefault(candidate =>
            string.Equals(candidate.FeedId, feedId, StringComparison.OrdinalIgnoreCase));
        return route?.Clone() ?? new OutputLookFeedRouting { FeedId = feedId, Slide = true, Media = true };
    }

    /// <summary>Creates a deep copy of this look definition.</summary>
    public OutputLookDefinition Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            IsBuiltIn = IsBuiltIn,
            Routes = Routes.Select(static route => route.Clone()).ToList(),
            ClearGroups = ClearGroups.Select(static clearGroup => clearGroup.Clone()).ToList(),
        };
}

/// <summary>Helpers and defaults for output routing feeds and Looks.</summary>
public static class OutputRoutingDefaults
{
    /// <summary>Backend layers shown by routing and clear controls, in operator-facing stack order.</summary>
    public static IReadOnlyList<OutputLayerDefinition> Layers { get; } =
    [
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Slide,
            Id = "slide",
            DisplayName = "Slide",
            Category = OutputLayerCategory.Presentation,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Media,
            Id = "media",
            DisplayName = "Media",
            Category = OutputLayerCategory.Media,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Audio,
            Id = "audio",
            DisplayName = "Audio",
            Category = OutputLayerCategory.Media,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Messages,
            Id = "messages",
            DisplayName = "Messages",
            Category = OutputLayerCategory.Overlay,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Props,
            Id = "props",
            DisplayName = "Props",
            Category = OutputLayerCategory.Overlay,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Announcements,
            Id = "announcements",
            DisplayName = "Announcements",
            Category = OutputLayerCategory.Overlay,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.LiveVideo,
            Id = "live-video",
            DisplayName = "Live Video",
            Category = OutputLayerCategory.Utility,
        },
        new OutputLayerDefinition
        {
            Kind = OutputLayerKind.Mask,
            Id = "mask",
            DisplayName = "Mask",
            Category = OutputLayerCategory.Utility,
            IsClearable = false,
        },
    ];

    /// <summary>Backend layer identities that can be routed through audience Looks.</summary>
    public static IReadOnlyList<OutputLayerKind> RoutableLayers { get; } =
        Layers.Where(static layer => layer.IsRoutable).Select(static layer => layer.Kind).ToArray();

    /// <summary>Default audience screens exposed by the current application shell.</summary>
    public static IReadOnlyList<OutputFeedDefinition> BuiltInFeeds { get; } =
    [
        new OutputFeedDefinition
        {
            Id = OutputFeedIds.Main,
            DisplayName = "Main",
            ScreenKind = "Audience",
            SupportsMirroring = true,
            SupportsGrouping = true,
            SupportsEdgeBlend = true,
        },
        new OutputFeedDefinition
        {
            Id = OutputFeedIds.Stream,
            DisplayName = "Stream",
            ScreenKind = "Audience",
            SupportsMirroring = true,
            SupportsGrouping = true,
            SupportsEdgeBlend = false,
        },
        new OutputFeedDefinition
        {
            Id = OutputFeedIds.Lobby,
            DisplayName = "Lobby",
            ScreenKind = "Audience",
            SupportsMirroring = true,
            SupportsGrouping = true,
            SupportsEdgeBlend = true,
        },
    ];

    /// <summary>Creates the built-in default look that routes all layers to all known feeds.</summary>
    public static OutputLookDefinition CreateDefaultLook(IEnumerable<OutputFeedDefinition>? feeds = null)
    {
        var effectiveFeeds = feeds?.ToList() ?? [.. BuiltInFeeds];
        return new OutputLookDefinition
        {
            Id = OutputLookIds.Default,
            Name = "Default",
            IsBuiltIn = true,
            Routes = effectiveFeeds
                .Select(feed => new OutputLookFeedRouting
                {
                    FeedId = feed.Id,
                    Slide = true,
                    Media = true,
                    Layers = CreateEnabledLayerRoutes(),
                })
                .ToList(),
            ClearGroups = [],
        };
    }

    /// <summary>Creates a writable custom look cloned from the supplied source or from the default look.</summary>
    public static OutputLookDefinition CreateCustomLook(OutputLookDefinition? source = null, IEnumerable<OutputFeedDefinition>? feeds = null)
    {
        var baseline = source?.Clone() ?? CreateDefaultLook(feeds);
        baseline.Id = OutputLookIds.Custom;
        baseline.Name = "Custom";
        baseline.IsBuiltIn = false;
        return baseline;
    }

    /// <summary>Returns the stable portable id for a backend layer.</summary>
    public static string GetLayerId(OutputLayerKind layerKind) =>
        Layers.FirstOrDefault(layer => layer.Kind == layerKind)?.Id ?? layerKind.ToString().ToLowerInvariant();

    /// <summary>Returns display metadata for a backend layer.</summary>
    public static OutputLayerDefinition GetLayerDefinition(OutputLayerKind layerKind) =>
        Layers.FirstOrDefault(layer => layer.Kind == layerKind)
        ?? new OutputLayerDefinition
        {
            Kind = layerKind,
            Id = GetLayerId(layerKind),
            DisplayName = layerKind.ToString(),
            Category = OutputLayerCategory.Utility,
        };

    /// <summary>Resolves route states for all routable backend layers.</summary>
    public static IReadOnlyList<OutputLayerRouteState> CreateRouteStates(OutputLookFeedRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);

        return Layers
            .Where(static layer => layer.IsRoutable)
            .Select(layer => new OutputLayerRouteState
            {
                Kind = layer.Kind,
                Id = layer.Id,
                DisplayName = layer.DisplayName,
                Category = layer.Category,
                IsEnabled = routing.Routes(layer.Kind),
                ThemeVariantId = routing.ResolveLayerRoute(layer.Kind)?.ThemeVariantId,
                MaskId = routing.ResolveLayerRoute(layer.Kind)?.MaskId,
            })
            .ToArray();
    }

    /// <summary>Ensures a persisted feed route has explicit entries for all known backend layers.</summary>
    public static void EnsureLayerRoutes(OutputLookFeedRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);

        foreach (OutputLayerDefinition layer in Layers.Where(static layer => layer.IsRoutable))
        {
            if (routing.Layers.Any(route => LayerIdEquals(route.Layer, layer.Kind)))
                continue;

            routing.Layers.Add(new OutputLayerRouteDefinition
            {
                Layer = layer.Id,
                Enabled = routing.Routes(layer.Kind),
            });
        }
    }

    /// <summary>Parses a portable layer id into the backend layer identity.</summary>
    public static bool TryParseLayerKind(string? layer, out OutputLayerKind layerKind)
    {
        layerKind = default;
        if (string.IsNullOrWhiteSpace(layer))
            return false;

        string normalized = NormalizeLayerId(layer);
        OutputLayerDefinition? definition = Layers.FirstOrDefault(candidate =>
            string.Equals(NormalizeLayerId(candidate.Id), normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeLayerId(candidate.Kind.ToString()), normalized, StringComparison.OrdinalIgnoreCase));
        if (definition == null)
            return false;

        layerKind = definition.Kind;
        return true;
    }

    /// <summary>Returns whether a portable layer id refers to the supplied backend layer.</summary>
    public static bool LayerIdEquals(string? layer, OutputLayerKind layerKind) =>
        TryParseLayerKind(layer, out OutputLayerKind parsed) && parsed == layerKind;

    /// <summary>Expands an operator-facing clear scope into backend output layers.</summary>
    public static IReadOnlyList<OutputLayerKind> ExpandClearScope(OutputClearScope scope) =>
        scope switch
        {
            OutputClearScope.Music => [OutputLayerKind.Audio],
            OutputClearScope.AudioEffects => [OutputLayerKind.Audio],
            OutputClearScope.Messages => [OutputLayerKind.Messages],
            OutputClearScope.Props => [OutputLayerKind.Props],
            OutputClearScope.Announcements => [OutputLayerKind.Announcements],
            OutputClearScope.Presentation => [OutputLayerKind.Slide],
            OutputClearScope.PresentationMedia => [OutputLayerKind.Media, OutputLayerKind.Audio],
            OutputClearScope.VideoInput => [OutputLayerKind.LiveVideo],
            _ => [],
        };

    /// <summary>Resolves backend layers for a configured clear group, supporting both new scopes and legacy layer ids.</summary>
    public static IReadOnlySet<OutputLayerKind> ResolveClearGroupLayers(OutputLookClearGroupDefinition clearGroup)
    {
        ArgumentNullException.ThrowIfNull(clearGroup);

        HashSet<OutputLayerKind> layers = clearGroup.Scopes
            .SelectMany(ExpandClearScope)
            .ToHashSet();

        foreach (string layer in clearGroup.Layers)
        {
            if (TryParseLayerKind(layer, out OutputLayerKind parsed)
                && GetLayerDefinition(parsed).IsClearable)
            {
                layers.Add(parsed);
            }
        }

        return layers;
    }

    /// <summary>Creates portable backend layer ids for a set of operator-facing clear scopes.</summary>
    public static List<string> CreateClearGroupLayers(params OutputClearScope[] scopes) =>
        scopes
            .SelectMany(ExpandClearScope)
            .Distinct()
            .Select(GetLayerId)
            .ToList();

    private static List<OutputLayerRouteDefinition> CreateEnabledLayerRoutes() =>
        Layers
            .Where(static layer => layer.IsRoutable)
            .Select(static layer => new OutputLayerRouteDefinition
            {
                Layer = layer.Id,
                Enabled = true,
            })
            .ToList();

    private static string NormalizeLayerId(string value) =>
        value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
}