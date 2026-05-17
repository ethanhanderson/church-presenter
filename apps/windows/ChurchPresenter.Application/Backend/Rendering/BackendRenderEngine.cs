using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Immutable live render-session snapshot used by the backend foundation.
/// </summary>
public sealed record LiveRenderSessionState
{
    /// <summary>Logical screens keyed by id.</summary>
    public IReadOnlyDictionary<string, OutputScreen> Screens { get; init; } =
        new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output endpoints keyed by id.</summary>
    public IReadOnlyDictionary<string, OutputEndpoint> Endpoints { get; init; } =
        new Dictionary<string, OutputEndpoint>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Logical screen to endpoint mappings.</summary>
    public IReadOnlyList<ScreenMapping> ScreenMappings { get; init; } = Array.Empty<ScreenMapping>();

    /// <summary>Active audience Look preset.</summary>
    public LookPreset ActiveLook { get; init; } = new() { Id = "default", Name = "Default" };

    /// <summary>Layer states keyed by layer kind.</summary>
    public IReadOnlyDictionary<OutputLayerKind, LayerState> Layers { get; init; } =
        CreateEmptyLayers();

    /// <summary>Stage layout assignments keyed by stage screen id.</summary>
    public IReadOnlyDictionary<string, string> StageLayoutIdsByScreenId { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Available stage layouts keyed by layout id.</summary>
    public IReadOnlyDictionary<string, StageLayout> StageLayouts { get; init; } =
        new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runtime stage-screen state keyed by stage screen id.</summary>
    public IReadOnlyDictionary<string, StageScreenState> StageScreens { get; init; } =
        new Dictionary<string, StageScreenState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Generated stage presentation data such as current/next text and notes.</summary>
    public StagePresentationSnapshot StagePresentation { get; init; } = new();

    /// <summary>Generated live state shared by overlays, stage layouts, and diagnostics.</summary>
    public GeneratedStateSnapshot GeneratedState { get; init; } = new();

    /// <summary>Command provenance for the latest applied live mutation.</summary>
    public LiveCommandProvenance LastCommandProvenance { get; init; } = new();

    /// <summary>Structured render or recovery errors emitted by the latest state mutation.</summary>
    public IReadOnlyList<RenderErrorDescriptor> RenderErrors { get; init; } = Array.Empty<RenderErrorDescriptor>();

    /// <summary>Monotonic version incremented by each applied action batch.</summary>
    public long Version { get; init; }

    /// <summary>Creates a layer-state map for all reserved layer identities.</summary>
    public static IReadOnlyDictionary<OutputLayerKind, LayerState> CreateEmptyLayers()
    {
        return Enum.GetValues<OutputLayerKind>()
            .ToDictionary(
                layerKind => layerKind,
                layerKind => new LayerState { Kind = layerKind },
                EqualityComparer<OutputLayerKind>.Default);
    }
}

/// <summary>
/// Result of applying an action batch to the live render state.
/// </summary>
public sealed record RenderEngineResult
{
    /// <summary>Updated session state.</summary>
    public LiveRenderSessionState State { get; init; } = new();

    /// <summary>Resolved frames after the update.</summary>
    public RenderFrameSet Frames { get; init; } = new();

    /// <summary>Diagnostics emitted while applying actions.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Resolves backend render frames from a live session snapshot.
/// </summary>
public interface IRenderFrameResolver
{
    /// <summary>Resolves audience and stage frames for the supplied state.</summary>
    RenderFrameSet Resolve(LiveRenderSessionState state);
}

/// <summary>
/// Applies action batches and produces new render frames.
/// </summary>
public interface IBackendRenderEngine
{
    /// <summary>Applies one normalized action batch.</summary>
    RenderEngineResult Apply(LiveRenderSessionState state, ActionBatch batch);
}

/// <summary>
/// Deterministic backend frame resolver for audience Looks and independent stage layouts.
/// </summary>
public sealed class BackendRenderFrameResolver : IRenderFrameResolver
{
    private static readonly OutputLayerKind[] LayerStack =
    [
        OutputLayerKind.Audio,
        OutputLayerKind.Media,
        OutputLayerKind.Slide,
        OutputLayerKind.Announcements,
        OutputLayerKind.Messages,
        OutputLayerKind.Props,
        OutputLayerKind.LiveVideo,
        OutputLayerKind.Mask,
    ];

    /// <inheritdoc />
    public RenderFrameSet Resolve(LiveRenderSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Dictionary<string, AudienceRenderFrame> audienceFrames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, StageRenderFrame> stageFrames = new(StringComparer.OrdinalIgnoreCase);

        foreach (OutputScreen screen in state.Screens.Values)
        {
            IReadOnlyList<EndpointRenderDiagnostics> endpointDiagnostics = ResolveEndpointDiagnostics(state, screen.Id);
            IReadOnlyList<string> endpointIds = endpointDiagnostics.Select(endpoint => endpoint.EndpointId).ToArray();
            if (screen.Kind == OutputScreenKind.Audience)
            {
                audienceFrames[screen.Id] = ResolveAudienceFrame(state, screen, endpointIds, endpointDiagnostics);
            }
            else
            {
                stageFrames[screen.Id] = ResolveStageFrame(state, screen, endpointIds, endpointDiagnostics);
            }
        }

        return new RenderFrameSet
        {
            AudienceFrames = audienceFrames,
            StageFrames = stageFrames,
        };
    }

    private static AudienceRenderFrame ResolveAudienceFrame(
        LiveRenderSessionState state,
        OutputScreen screen,
        IReadOnlyList<string> endpointIds,
        IReadOnlyList<EndpointRenderDiagnostics> endpointDiagnostics)
    {
        ScreenLayerRouting route = state.ActiveLook.ResolveRoute(screen.Id);
        List<RenderLayerDescriptor> layers = [];

        foreach (OutputLayerKind layerKind in LayerStack)
        {
            if (!state.Layers.TryGetValue(layerKind, out LayerState? layerState) || layerState.Payload == null)
            {
                if (layerKind != OutputLayerKind.Mask || !TryCreateRouteMaskLayer(route, out RenderLayerDescriptor? maskLayer))
                    continue;

                layers.Add(maskLayer);
                continue;
            }

            bool routed = route.Routes(layerKind);
            string? themeVariantId = route.ResolveThemeVariant(layerKind) ?? layerState.Payload.ThemeVariantId;
            RenderPayloadDescriptor payload = layerState.Payload with
            {
                ThemeVariantId = themeVariantId,
                Detail = ApplyThemeVariant(layerState.Payload.Detail, themeVariantId),
            };

            layers.Add(new RenderLayerDescriptor
            {
                Kind = layerKind,
                Payload = payload,
                IsVisible = routed && layerState.IsVisible && !layerState.IsSuppressed && !layerState.IsCleared,
                IsSuppressed = layerState.IsSuppressed || !routed,
                ClearState = layerState.ClearState,
                SourceCommandId = layerState.SourceCommandId,
                Provenance = layerState.Provenance,
                Transition = layerState.Transition,
                PlayerState = layerState.PlayerState,
                RenderErrors = layerState.RenderErrors,
                Diagnostics = layerState.Diagnostics,
            });
        }

        return new AudienceRenderFrame
        {
            Sequence = state.Version,
            ScreenId = screen.Id,
            LookPresetId = state.ActiveLook.Id,
            Provenance = state.LastCommandProvenance,
            RenderSize = screen.RenderSize,
            AlphaMode = screen.AlphaMode,
            Layers = layers,
            Diagnostics = BuildFrameDiagnostics(endpointIds, endpointDiagnostics, state.RenderErrors),
        };
    }

    private static bool TryCreateRouteMaskLayer(ScreenLayerRouting route, out RenderLayerDescriptor layer)
    {
        layer = new RenderLayerDescriptor();
        string? maskId = route.ResolveMaskId();
        bool routed = route.Routes(OutputLayerKind.Mask);
        if (!routed || string.IsNullOrWhiteSpace(maskId))
            return false;

        string normalizedMaskId = maskId.Trim();
        layer = new RenderLayerDescriptor
        {
            Kind = OutputLayerKind.Mask,
            Payload = new RenderPayloadDescriptor
            {
                Id = $"mask:{normalizedMaskId}",
                Kind = RenderPayloadKind.Overlay,
                DisplayName = normalizedMaskId,
                SourceReference = $"Mask:{normalizedMaskId}",
                Detail = new OverlayRenderPayload
                {
                    OverlayId = normalizedMaskId,
                    OverlayKind = "Mask",
                },
            },
            IsVisible = true,
        };
        return true;
    }

    private static RenderPayloadDetail? ApplyThemeVariant(RenderPayloadDetail? detail, string? themeVariantId)
    {
        if (detail is PresentationRenderPayload presentation)
        {
            return presentation with
            {
                Scene = !string.IsNullOrWhiteSpace(themeVariantId)
                    && presentation.VariantScenes.TryGetValue(themeVariantId, out SlideScene? variantScene)
                        ? variantScene
                        : presentation.Scene,
                ThemeVariantId = themeVariantId ?? presentation.ThemeVariantId,
            };
        }

        return detail;
    }

    private static StageRenderFrame ResolveStageFrame(
        LiveRenderSessionState state,
        OutputScreen screen,
        IReadOnlyList<string> endpointIds,
        IReadOnlyList<EndpointRenderDiagnostics> endpointDiagnostics)
    {
        string stageLayoutId = state.StageLayoutIdsByScreenId.TryGetValue(screen.Id, out string? layoutId)
            ? layoutId
            : "default";
        StageScreenState stageScreen = state.StageScreens.TryGetValue(screen.Id, out StageScreenState? stageScreenState)
            ? stageScreenState
            : new StageScreenState
            {
                ScreenId = screen.Id,
                Name = screen.Name,
                ActiveLayoutId = stageLayoutId,
            };

        List<RenderPayloadDescriptor> payloads = state.StageLayouts.TryGetValue(stageLayoutId, out StageLayout? layout)
            ? ResolveStageLayoutPayloads(state, screen.Id, layout)
            : [];

        if (payloads.Count == 0)
        {
            AddStagePayload(payloads, state, OutputLayerKind.Slide, "current-slide");
            AddStagePayload(payloads, state, OutputLayerKind.Media, "media-status");
        }

        return new StageRenderFrame
        {
            Sequence = state.Version,
            ScreenId = screen.Id,
            StageLayoutId = stageLayoutId,
            CommandMode = stageScreen.LastCommandMode,
            Provenance = state.LastCommandProvenance,
            RenderSize = screen.RenderSize,
            Payloads = payloads,
            Diagnostics = BuildFrameDiagnostics(endpointIds, endpointDiagnostics, state.RenderErrors),
        };
    }

    private static List<RenderPayloadDescriptor> ResolveStageLayoutPayloads(
        LiveRenderSessionState state,
        string screenId,
        StageLayout layout)
    {
        List<RenderPayloadDescriptor> payloads = [];
        foreach (StageLayoutElement element in layout.Elements)
        {
            RenderPayloadDescriptor? payload = ResolveStageElementPayload(state, screenId, element);
            if (payload != null)
            {
                payloads.Add(payload);
            }
        }

        return payloads;
    }

    private static RenderPayloadDescriptor? ResolveStageElementPayload(
        LiveRenderSessionState state,
        string screenId,
        StageLayoutElement element)
    {
        StageDataRequest request = new()
        {
            State = state,
            ScreenId = screenId,
            Element = element,
        };

        IStageDataProvider? provider = StageDataProviderCatalog.DefaultProviders
            .FirstOrDefault(candidate => candidate.CanResolve(element));

        return provider?.Resolve(request).Payload;
    }

    private static void AddStagePayload(
        ICollection<RenderPayloadDescriptor> payloads,
        LiveRenderSessionState state,
        OutputLayerKind layerKind,
        string stageRole)
    {
        if (!state.Layers.TryGetValue(layerKind, out LayerState? layerState) || layerState.Payload == null)
        {
            return;
        }

        payloads.Add(layerState.Payload with
        {
            SourceReference = string.IsNullOrWhiteSpace(layerState.Payload.SourceReference)
                ? stageRole
                : layerState.Payload.SourceReference,
        });
    }

    private static RenderDiagnostics BuildFrameDiagnostics(
        IReadOnlyList<string> endpointIds,
        IReadOnlyList<EndpointRenderDiagnostics> endpointDiagnostics,
        IReadOnlyList<RenderErrorDescriptor> renderErrors)
    {
        return new RenderDiagnostics
        {
            EndpointIds = endpointIds,
            Endpoints = endpointDiagnostics,
            RenderErrors = renderErrors,
            Message = renderErrors.Count > 0
                ? string.Join("; ", renderErrors.Select(error => error.Message))
                : null,
        };
    }

    private static IReadOnlyList<EndpointRenderDiagnostics> ResolveEndpointDiagnostics(LiveRenderSessionState state, string screenId)
    {
        ScreenMapping? mapping = state.ScreenMappings.FirstOrDefault(candidate =>
            string.Equals(candidate.ScreenId, screenId, StringComparison.OrdinalIgnoreCase));

        if (mapping == null)
        {
            return Array.Empty<EndpointRenderDiagnostics>();
        }

        return mapping.EndpointIds
            .Select(endpointId =>
            {
                if (state.Endpoints.TryGetValue(endpointId, out OutputEndpoint? endpoint))
                {
                    return new EndpointRenderDiagnostics
                    {
                        EndpointId = endpointId,
                        Kind = endpoint.Kind,
                        Health = endpoint.Health,
                        Message = endpoint.Health == EndpointHealth.Connected
                            ? "Endpoint connected."
                            : $"Endpoint health: {endpoint.Health}.",
                    };
                }

                return new EndpointRenderDiagnostics
                {
                    EndpointId = endpointId,
                    Health = EndpointHealth.Missing,
                    Message = "Mapped endpoint is not present in the endpoint catalog.",
                };
            })
            .ToArray();
    }
}

/// <summary>
/// Simple backend render engine that mutates immutable snapshots and resolves frames.
/// </summary>
public sealed class BackendRenderEngine(IRenderFrameResolver frameResolver, IRenderFrameStore? frameStore = null) : IBackendRenderEngine
{
    /// <inheritdoc />
    public RenderEngineResult Apply(LiveRenderSessionState state, ActionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(batch);

        Dictionary<OutputLayerKind, LayerState> layers = new(state.Layers);
        Dictionary<string, string> stageLayouts = new(state.StageLayoutIdsByScreenId, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, StageScreenState> stageScreens = new(state.StageScreens, StringComparer.OrdinalIgnoreCase);
        LookPreset activeLook = state.ActiveLook;
        GeneratedStateSnapshot generatedState = state.GeneratedState;
        List<string> diagnostics = [];
        List<RenderErrorDescriptor> renderErrors = [];
        LiveCommandProvenance lastProvenance = CreateBatchProvenance(batch);

        foreach (LiveAction action in batch.Actions)
        {
            LiveCommandProvenance provenance = CreateActionProvenance(action, batch);
            lastProvenance = provenance;
            ApplyAction(
                action,
                provenance,
                layers,
                stageLayouts,
                stageScreens,
                ref activeLook,
                ref generatedState,
                diagnostics,
                renderErrors);
        }

        generatedState = BuildGeneratedState(generatedState, layers, stageLayouts, activeLook);

        LiveRenderSessionState updated = state with
        {
            ActiveLook = activeLook,
            Layers = layers,
            StageLayoutIdsByScreenId = stageLayouts,
            StageScreens = stageScreens,
            GeneratedState = generatedState,
            LastCommandProvenance = lastProvenance,
            RenderErrors = renderErrors,
            Version = state.Version + 1,
        };

        RenderFrameSet frames = frameResolver.Resolve(updated);
        frameStore?.Save(frames);

        return new RenderEngineResult
        {
            State = updated,
            Frames = frames,
            Diagnostics = diagnostics,
        };
    }

    private static void ApplyAction(
        LiveAction action,
        LiveCommandProvenance provenance,
        IDictionary<OutputLayerKind, LayerState> layers,
        IDictionary<string, string> stageLayouts,
        IDictionary<string, StageScreenState> stageScreens,
        ref LookPreset activeLook,
        ref GeneratedStateSnapshot generatedState,
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors)
    {
        switch (action.Kind)
        {
            case LiveActionKind.SetLayerPayload:
                ApplyLayerPayload(action, provenance, layers, diagnostics, renderErrors);
                break;
            case LiveActionKind.ClearLayers:
                generatedState = ApplyClear(action, provenance, layers, activeLook, generatedState, diagnostics, renderErrors);
                break;
            case LiveActionKind.SetLook:
                if (action.Look == null)
                {
                    AddError(diagnostics, renderErrors, "SetLook action did not include a Look preset.", provenance);
                    break;
                }

                activeLook = action.Look;
                break;
            case LiveActionKind.SetStageLayout:
                if (string.IsNullOrWhiteSpace(action.Target.Id) || string.IsNullOrWhiteSpace(action.StageLayoutId))
                {
                    AddError(diagnostics, renderErrors, "SetStageLayout action requires a screen id and layout id.", provenance, screenId: action.Target.Id);
                    break;
                }

                stageLayouts[action.Target.Id] = action.StageLayoutId;
                stageScreens[action.Target.Id] = stageScreens.TryGetValue(action.Target.Id, out StageScreenState? stageScreen)
                    ? stageScreen with
                    {
                        ActiveLayoutId = action.StageLayoutId,
                        LastCommandMode = action.DeliveryMode,
                    }
                    : new StageScreenState
                    {
                        ScreenId = action.Target.Id,
                        Name = action.Target.Id,
                        ActiveLayoutId = action.StageLayoutId,
                        LastCommandMode = action.DeliveryMode,
                    };
                break;
            case LiveActionKind.SetOverlayState:
                generatedState = ApplyOverlay(action, provenance, generatedState, layers, diagnostics, renderErrors);
                break;
            case LiveActionKind.SetTimerState:
                generatedState = ApplyTimer(action, provenance, generatedState, diagnostics, renderErrors);
                break;
            case LiveActionKind.SetCaptureSessionState:
                generatedState = ApplyCaptureSession(action, provenance, generatedState, diagnostics, renderErrors);
                break;
            default:
                AddError(diagnostics, renderErrors, $"Unsupported action kind '{action.Kind}'.", provenance);
                break;
        }
    }

    private static void ApplyLayerPayload(
        LiveAction action,
        LiveCommandProvenance provenance,
        IDictionary<OutputLayerKind, LayerState> layers,
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors)
    {
        if (action.Target.LayerKind == null || action.Payload == null)
        {
            AddError(
                diagnostics,
                renderErrors,
                "SetLayerPayload action requires a target layer and payload.",
                provenance,
                layerKind: action.Target.LayerKind);
            return;
        }

        OutputLayerKind layerKind = action.Target.LayerKind.Value;
        layers[layerKind] = new LayerState
        {
            Kind = layerKind,
            Payload = action.Payload,
            IsVisible = true,
            ClearState = LayerClearState.None,
            SourceCommandId = provenance.CommandId,
            Provenance = provenance,
            Transition = NormalizeTransition(action.Transition, provenance),
        };
    }

    private static GeneratedStateSnapshot ApplyClear(
        LiveAction action,
        LiveCommandProvenance provenance,
        IDictionary<OutputLayerKind, LayerState> layers,
        LookPreset activeLook,
        GeneratedStateSnapshot generatedState,
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors)
    {
        IReadOnlySet<OutputLayerKind> layersToClear = ResolveClearLayers(action.Clear, activeLook);
        if (layersToClear.Count == 0)
        {
            AddError(diagnostics, renderErrors, "Clear action did not resolve any layers.", provenance);
            return generatedState;
        }

        foreach (OutputLayerKind layerKind in layersToClear.Where(static layer => layer != OutputLayerKind.Mask))
        {
            layers[layerKind] = layers.TryGetValue(layerKind, out LayerState? current)
                ? current with
                {
                    Payload = null,
                    IsVisible = false,
                    IsCleared = true,
                    ClearState = LayerClearState.Cleared,
                    SourceCommandId = provenance.CommandId,
                    Provenance = provenance,
                    Transition = LayerTransitionState.None with { Provenance = provenance },
                }
                : new LayerState
                {
                    Kind = layerKind,
                    IsCleared = true,
                    ClearState = LayerClearState.Cleared,
                    SourceCommandId = provenance.CommandId,
                    Provenance = provenance,
                    Transition = LayerTransitionState.None with { Provenance = provenance },
                };
        }

        return ClearGeneratedState(
            generatedState,
            layersToClear.Where(static layer => layer != OutputLayerKind.Mask).ToHashSet());
    }

    private static IReadOnlySet<OutputLayerKind> ResolveClearLayers(ClearCommand? clear, LookPreset activeLook)
    {
        if (clear == null)
        {
            return new HashSet<OutputLayerKind>();
        }

        if (clear.Layers.Count > 0)
        {
            return clear.Layers.Where(static layer => layer != OutputLayerKind.Mask).ToHashSet();
        }

        if (string.IsNullOrWhiteSpace(clear.ClearGroupId))
        {
            return new HashSet<OutputLayerKind>();
        }

        ClearGroup? clearGroup = activeLook.ClearGroups.FirstOrDefault(group =>
            string.Equals(group.Id, clear.ClearGroupId, StringComparison.OrdinalIgnoreCase));

        return clearGroup?.Layers.Where(static layer => layer != OutputLayerKind.Mask).ToHashSet()
            ?? new HashSet<OutputLayerKind>();
    }

    private static GeneratedStateSnapshot ApplyOverlay(
        LiveAction action,
        LiveCommandProvenance provenance,
        GeneratedStateSnapshot generatedState,
        IDictionary<OutputLayerKind, LayerState> layers,
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors)
    {
        if (action.Overlay == null)
        {
            AddError(diagnostics, renderErrors, "SetOverlayState action requires overlay content.", provenance);
            return generatedState;
        }

        OverlayContentState overlay = action.Overlay;
        generatedState = overlay.Kind switch
        {
            OverlayContentKind.Message => generatedState with
            {
                Messages = Upsert(generatedState.Messages, overlay.Id, overlay),
            },
            OverlayContentKind.Prop => generatedState with
            {
                Props = Upsert(generatedState.Props, overlay.Id, overlay),
            },
            OverlayContentKind.Announcement => generatedState with
            {
                Announcements = Upsert(generatedState.Announcements, overlay.Id, overlay),
            },
            OverlayContentKind.Mask => generatedState with
            {
                Masks = Upsert(generatedState.Masks, overlay.Id, overlay),
            },
            OverlayContentKind.StageMessage => generatedState with
            {
                StageMessageText = overlay.Text ?? overlay.Payload?.DisplayName ?? overlay.Name,
            },
            _ => generatedState,
        };

        if (!OverlayLayerIdentity.TryGetAudienceLayer(overlay.Kind, out OutputLayerKind layerKind))
        {
            return generatedState;
        }

        RenderPayloadDescriptor payload = ResolveOverlayPayload(overlay);
        layers[layerKind] = new LayerState
        {
            Kind = layerKind,
            Payload = payload,
            IsVisible = overlay.IsVisible,
            IsCleared = !overlay.IsVisible && overlay.Payload == null && string.IsNullOrWhiteSpace(overlay.Text),
            ClearState = !overlay.IsVisible && overlay.Payload == null && string.IsNullOrWhiteSpace(overlay.Text)
                ? LayerClearState.Cleared
                : LayerClearState.None,
            SourceCommandId = provenance.CommandId,
            Provenance = provenance,
            Transition = NormalizeTransition(action.Transition, provenance),
        };

        return generatedState;
    }

    private static GeneratedStateSnapshot ClearGeneratedState(
        GeneratedStateSnapshot generatedState,
        IReadOnlySet<OutputLayerKind> layersToClear)
    {
        return generatedState with
        {
            Messages = layersToClear.Contains(OutputLayerKind.Messages)
                ? SetOverlayVisibility(generatedState.Messages, false)
                : generatedState.Messages,
            Props = layersToClear.Contains(OutputLayerKind.Props)
                ? SetOverlayVisibility(generatedState.Props, false)
                : generatedState.Props,
            Announcements = layersToClear.Contains(OutputLayerKind.Announcements)
                ? SetOverlayVisibility(generatedState.Announcements, false)
                : generatedState.Announcements,
            Masks = layersToClear.Contains(OutputLayerKind.Mask)
                ? SetOverlayVisibility(generatedState.Masks, false)
                : generatedState.Masks,
        };
    }

    private static IReadOnlyDictionary<string, OverlayContentState> SetOverlayVisibility(
        IReadOnlyDictionary<string, OverlayContentState> overlays,
        bool isVisible)
    {
        return overlays.ToDictionary(
            static pair => pair.Key,
            pair => pair.Value with { IsVisible = isVisible },
            StringComparer.OrdinalIgnoreCase);
    }

    private static GeneratedStateSnapshot ApplyTimer(
        LiveAction action,
        LiveCommandProvenance provenance,
        GeneratedStateSnapshot generatedState,
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors)
    {
        if (action.Timer == null)
        {
            AddError(diagnostics, renderErrors, "SetTimerState action requires a timer snapshot.", provenance);
            return generatedState;
        }

        return generatedState with
        {
            Timers = Upsert(generatedState.Timers, action.Timer.Id, action.Timer),
        };
    }

    private static GeneratedStateSnapshot ApplyCaptureSession(
        LiveAction action,
        LiveCommandProvenance provenance,
        GeneratedStateSnapshot generatedState,
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors)
    {
        if (action.CaptureSession == null)
        {
            AddError(diagnostics, renderErrors, "SetCaptureSessionState action requires capture-session state.", provenance);
            return generatedState;
        }

        return generatedState with
        {
            CaptureSessions = Upsert(
                generatedState.CaptureSessions,
                action.CaptureSession.Metadata.Id,
                action.CaptureSession),
        };
    }

    private static RenderPayloadDescriptor ResolveOverlayPayload(OverlayContentState overlay)
    {
        if (overlay.Payload != null)
        {
            return overlay.Payload;
        }

        string displayName = !string.IsNullOrWhiteSpace(overlay.Text)
            ? overlay.Text
            : overlay.Name;

        return new RenderPayloadDescriptor
        {
            Id = $"overlay:{overlay.Kind}:{overlay.Id}",
            Kind = RenderPayloadKind.Overlay,
            DisplayName = displayName,
            SourceReference = $"{overlay.Kind}:{overlay.Id}",
            Detail = new OverlayRenderPayload
            {
                OverlayId = overlay.Id,
                OverlayKind = overlay.Kind.ToString(),
                Scene = overlay.Payload?.Detail is PresentationRenderPayload presentationPayload
                    ? presentationPayload.Scene
                    : null,
            },
        };
    }

    private static IReadOnlyDictionary<string, TValue> Upsert<TValue>(
        IReadOnlyDictionary<string, TValue> values,
        string key,
        TValue value)
    {
        Dictionary<string, TValue> updated = new(values, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value,
        };

        return updated;
    }

    private static LiveCommandProvenance CreateBatchProvenance(ActionBatch batch)
    {
        return new LiveCommandProvenance
        {
            CommandId = batch.SourceCommandId == Guid.Empty ? null : batch.SourceCommandId,
            CorrelationId = batch.CorrelationId,
            SourceKind = batch.Source.Kind.ToString(),
            SourceId = batch.Source.Id,
            Actor = batch.Source.Actor,
        };
    }

    private static LiveCommandProvenance CreateActionProvenance(LiveAction action, ActionBatch batch)
    {
        LiveCommandSource source = action.Source ?? batch.Source;
        Guid? commandId = action.SourceCommandId ?? batch.SourceCommandId;

        return new LiveCommandProvenance
        {
            CommandId = commandId == Guid.Empty ? null : commandId,
            CorrelationId = action.CorrelationId ?? batch.CorrelationId,
            SourceKind = source.Kind.ToString(),
            SourceId = source.Id,
            Actor = source.Actor,
        };
    }

    private static LayerTransitionState NormalizeTransition(
        LayerTransitionState? transition,
        LiveCommandProvenance provenance)
    {
        if (transition == null || transition.Phase == LayerTransitionPhase.None)
        {
            return LayerTransitionState.None with { Provenance = provenance };
        }

        return transition with { Provenance = provenance };
    }

    private static void AddError(
        ICollection<string> diagnostics,
        ICollection<RenderErrorDescriptor> renderErrors,
        string message,
        LiveCommandProvenance provenance,
        OutputLayerKind? layerKind = null,
        string? screenId = null,
        string? endpointId = null)
    {
        diagnostics.Add(message);
        renderErrors.Add(new RenderErrorDescriptor
        {
            Message = message,
            LayerKind = layerKind,
            ScreenId = screenId,
            EndpointId = endpointId,
            Provenance = provenance,
        });
    }

    private static GeneratedStateSnapshot BuildGeneratedState(
        GeneratedStateSnapshot state,
        IDictionary<OutputLayerKind, LayerState> layers,
        IDictionary<string, string> stageLayouts,
        LookPreset activeLook)
    {
        OperatorRecoveryDiagnosticsState diagnostics = new()
        {
            Layers = layers.Values
                .OrderBy(layer => layer.Kind)
                .Select(layer => new LayerRecoveryState
                {
                    LayerKind = layer.Kind,
                    IsLive = layer.Payload != null && layer.IsVisible && !layer.IsSuppressed && !layer.IsCleared,
                    PayloadId = layer.Payload?.Id,
                    SourceReference = layer.Payload?.SourceReference,
                    SourceCommandId = layer.SourceCommandId,
                    CorrelationId = layer.Provenance.CorrelationId,
                    SourceKind = layer.Provenance.SourceKind,
                    ClearState = layer.ClearState,
                    Transition = layer.Transition,
                    HasPlayerState = layer.PlayerState != null,
                    RenderErrors = layer.RenderErrors,
                    Diagnostics = layer.Diagnostics,
                })
                .ToArray(),
            StageLayoutsByScreenId = new Dictionary<string, string>(stageLayouts, StringComparer.OrdinalIgnoreCase),
            AvailableClearGroupIds = activeLook.ClearGroups
                .Select(group => group.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            ActiveCaptureSessionIds = state.CaptureSessions.Values
                .Where(session => session.IsActive)
                .Select(session => session.Metadata.Id)
                .ToArray(),
        };

        return state with
        {
            RecoveryDiagnostics = diagnostics,
        };
    }
}