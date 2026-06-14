using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Models.Themes;
using ChurchPresenter.Services.Output;
using ChurchPresenter.Services.Pipeline;

namespace ChurchPresenter.Backend.Pipeline.Routing;

/// <summary>
/// Applies prepared cues and clear actions to the live session snapshot.
/// </summary>
public interface ILayerRouter
{
    /// <summary>Applies a prepared cue to its target layer slot.</summary>
    LiveSessionSnapshot ApplyPreparedCue(LiveSessionSnapshot snapshot, PreparedCueRecord preparedCue, LayerTransitionState? transition = null);

    /// <summary>Clears the specified layers.</summary>
    LiveSessionSnapshot ClearLayers(LiveSessionSnapshot snapshot, IEnumerable<OutputLayerKind> layers);

    /// <summary>Clears a configured clear group from the active Look.</summary>
    LiveSessionSnapshot ClearGroup(LiveSessionSnapshot snapshot, string clearGroupId);

    /// <summary>Replaces the payload on a layer slot, retaining prior payload for recovery.</summary>
    LiveSessionSnapshot ReplaceLayerPayload(LiveSessionSnapshot snapshot, OutputLayerKind layer, PreparedCueRecord preparedCue, LayerTransitionState? transition = null);

    /// <summary>Releases runtime clear overrides for the specified layers.</summary>
    LiveSessionSnapshot ReleaseClearedLayers(LiveSessionSnapshot snapshot, IEnumerable<OutputLayerKind> layers);
}

/// <summary>
/// Converts prepared cue records into backend live commands and mutates the session snapshot.
/// </summary>
public sealed class LayerRouter(
    IBackendRenderEngine renderEngine,
    ILiveCommandExecutor commandExecutor,
    IOutputRoutingService routingService,
    IShowTransitionDefaults transitionDefaults) : ILayerRouter
{
    private readonly IBackendRenderEngine _renderEngine = renderEngine ?? throw new ArgumentNullException(nameof(renderEngine));
    private readonly ILiveCommandExecutor _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
    private readonly IOutputRoutingService _routingService = routingService ?? throw new ArgumentNullException(nameof(routingService));
    private readonly IShowTransitionDefaults _transitionDefaults = transitionDefaults ?? throw new ArgumentNullException(nameof(transitionDefaults));

    /// <inheritdoc />
    public LiveSessionSnapshot ApplyPreparedCue(
        LiveSessionSnapshot snapshot,
        PreparedCueRecord preparedCue,
        LayerTransitionState? transition = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preparedCue);

        LiveCommand command = LiveCommandExecutor.SetLayerPayload(
            preparedCue.Layer,
            preparedCue.Payload,
            source: new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            transition: ResolveLayerTransition(preparedCue, transition));

        return ApplyCommand(snapshot, command, preparedCue, clearOverride: false);
    }

    /// <inheritdoc />
    public LiveSessionSnapshot ClearLayers(LiveSessionSnapshot snapshot, IEnumerable<OutputLayerKind> layers)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(layers);

        HashSet<OutputLayerKind> layerSet = layers.Where(static layer => layer != OutputLayerKind.Mask).ToHashSet();
        if (layerSet.Count == 0)
            return snapshot;

        LiveCommand command = LiveCommandExecutor.ClearLayers(layerSet);
        return ApplyCommand(snapshot, command, preparedCue: null, clearOverride: true, clearedLayers: layerSet);
    }

    /// <inheritdoc />
    public LiveSessionSnapshot ClearGroup(LiveSessionSnapshot snapshot, string clearGroupId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(clearGroupId);

        HashSet<OutputLayerKind> clearedLayers = ResolveClearGroupLayers(clearGroupId);
        LiveCommand command = LiveCommandExecutor.ClearGroup(clearGroupId);
        return ApplyCommand(
            snapshot,
            command,
            preparedCue: null,
            clearOverride: true,
            clearedLayers: clearedLayers.Count > 0 ? clearedLayers : null);
    }

    /// <inheritdoc />
    public LiveSessionSnapshot ReplaceLayerPayload(
        LiveSessionSnapshot snapshot,
        OutputLayerKind layer,
        PreparedCueRecord preparedCue,
        LayerTransitionState? transition = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preparedCue);

        RenderPayloadDescriptor? priorPayload = snapshot.SessionState.Layers.TryGetValue(layer, out LayerState? current)
            ? current.Payload
            : null;

        LiveSessionSnapshot updated = ApplyPreparedCue(snapshot, preparedCue with { Layer = layer }, transition);
        Dictionary<OutputLayerKind, LayerSlot> slots = new(updated.LayerSlots);
        if (slots.TryGetValue(layer, out LayerSlot? slot))
        {
            slots[layer] = slot with
            {
                PriorPayload = priorPayload,
                ClearOverride = false,
            };
        }

        return updated with { LayerSlots = slots };
    }

    /// <inheritdoc />
    public LiveSessionSnapshot ReleaseClearedLayers(LiveSessionSnapshot snapshot, IEnumerable<OutputLayerKind> layers)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(layers);

        Dictionary<OutputLayerKind, LayerSlot> slots = new(snapshot.LayerSlots);
        foreach (OutputLayerKind layer in layers)
        {
            if (slots.TryGetValue(layer, out LayerSlot? slot))
                slots[layer] = slot with { ClearOverride = false };
        }

        return snapshot with { LayerSlots = slots };
    }

    private LiveSessionSnapshot ApplyCommand(
        LiveSessionSnapshot snapshot,
        LiveCommand command,
        PreparedCueRecord? preparedCue,
        bool clearOverride,
        IReadOnlySet<OutputLayerKind>? clearedLayers = null)
    {
        LiveRenderSessionState sessionState = EnsureActiveLook(snapshot.SessionState);
        ActionResult result = _commandExecutor.Execute(sessionState, command);
        Dictionary<OutputLayerKind, LayerSlot> slots = new(snapshot.LayerSlots);

        if (preparedCue != null)
        {
            OutputLayerKind layer = preparedCue.Layer;
            LayerState layerState = result.State.Layers.TryGetValue(layer, out LayerState? updatedLayer)
                ? updatedLayer
                : new LayerState { Kind = layer };

            slots[layer] = new LayerSlot
            {
                Kind = layer,
                PreparedCue = preparedCue,
                State = layerState,
                EnterTimestamp = DateTimeOffset.UtcNow,
                Transition = layerState.Transition,
                ClearOverride = false,
                PriorPayload = slots.TryGetValue(layer, out LayerSlot? existing) ? existing.PriorPayload : null,
            };
        }
        else if (clearedLayers != null)
        {
            foreach (OutputLayerKind layer in clearedLayers)
            {
                LayerState layerState = result.State.Layers.TryGetValue(layer, out LayerState? updatedLayer)
                    ? updatedLayer
                    : new LayerState { Kind = layer };

                slots.TryGetValue(layer, out LayerSlot? existing);
                slots[layer] = new LayerSlot
                {
                    Kind = layer,
                    PreparedCue = null,
                    State = layerState,
                    EnterTimestamp = existing?.EnterTimestamp ?? default,
                    Transition = ResolveClearTransition(layer, existing),
                    ClearOverride = clearOverride,
                    PriorPayload = existing?.PreparedCue?.Payload ?? existing?.State.Payload,
                };
            }
        }

        return snapshot with
        {
            SessionState = result.State,
            LayerSlots = slots,
            Version = result.State.Version,
            ActiveLookId = _routingService.ActiveLookId,
        };
    }

    private LiveRenderSessionState EnsureActiveLook(LiveRenderSessionState sessionState)
    {
        LookPreset look = ConvertLook(_routingService.ActiveLook, _routingService.Feeds);
        return sessionState with { ActiveLook = look };
    }

    private static LookPreset ConvertLook(OutputLookDefinition look, IReadOnlyList<OutputFeedDefinition> feeds)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(feeds);

        HashSet<string> audienceScreenIds = feeds
            .Select(feed => feed.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new LookPreset
        {
            Id = string.IsNullOrWhiteSpace(look.Id) ? "default" : look.Id,
            Name = string.IsNullOrWhiteSpace(look.Name) ? "Default" : look.Name,
            ScreenRoutes = look.Routes
                .Where(route => audienceScreenIds.Contains(route.FeedId))
                .Select(route => new ScreenLayerRouting
                {
                    ScreenId = route.FeedId,
                    Layers = OutputRoutingDefaults.RoutableLayers
                        .Select(layerKind => new LayerRoute
                        {
                            LayerKind = layerKind,
                            IsEnabled = route.Routes(layerKind),
                            ThemeVariantId = route.ResolveLayerRoute(layerKind)?.ThemeVariantId,
                            MaskId = layerKind == OutputLayerKind.Mask
                                ? route.ResolveLayerRoute(layerKind)?.MaskId
                                : null,
                        })
                        .ToArray(),
                })
                .ToArray(),
            ClearGroups = look.ClearGroups
                .Select(clearGroup => new ClearGroup
                {
                    Id = clearGroup.Id,
                    Name = clearGroup.Name,
                    Layers = OutputRoutingDefaults.ResolveClearGroupLayers(clearGroup),
                })
                .Where(static clearGroup => clearGroup.Layers.Count > 0)
                .ToArray(),
        };
    }

    private HashSet<OutputLayerKind> ResolveClearGroupLayers(string clearGroupId)
    {
        OutputLookClearGroupDefinition? clearGroup = _routingService.ActiveLook.ClearGroups.FirstOrDefault(group =>
            string.Equals(group.Id, clearGroupId, StringComparison.OrdinalIgnoreCase));

        return clearGroup == null
            ? new HashSet<OutputLayerKind>()
            : OutputRoutingDefaults.ResolveClearGroupLayers(clearGroup)
                .Where(static layer => layer != OutputLayerKind.Mask)
                .ToHashSet();
    }

    private LayerTransitionState ResolveLayerTransition(
        PreparedCueRecord preparedCue,
        LayerTransitionState? explicitTransition)
    {
        if (explicitTransition != null && explicitTransition.Phase != LayerTransitionPhase.None)
            return explicitTransition;

        SlideTransition? resolvedTransition = preparedCue.Layer switch
        {
            OutputLayerKind.Slide or OutputLayerKind.Announcements =>
                preparedCue.Transition ?? _transitionDefaults.GlobalSlideFallback,
            OutputLayerKind.Media =>
                preparedCue.Transition ?? _transitionDefaults.GlobalMediaFallback,
            OutputLayerKind.Messages or OutputLayerKind.Props or OutputLayerKind.LiveVideo =>
                preparedCue.Transition,
            _ => null,
        };

        LayerTransitionState layerTransition = TransitionResolver.ToLayerTransition(resolvedTransition);
        return layerTransition;
    }

    private LayerTransitionState ResolveClearTransition(OutputLayerKind layer, LayerSlot? existing)
    {
        SlideTransition? resolvedTransition = layer switch
        {
            OutputLayerKind.Slide or OutputLayerKind.Announcements =>
                existing?.PreparedCue?.Transition ?? _transitionDefaults.GlobalSlideFallback,
            OutputLayerKind.Media =>
                existing?.PreparedCue?.Transition ?? _transitionDefaults.GlobalMediaFallback,
            OutputLayerKind.Messages or OutputLayerKind.Props or OutputLayerKind.LiveVideo =>
                existing?.PreparedCue?.Transition,
            _ => null,
        };

        return TransitionResolver.ToLayerTransition(resolvedTransition);
    }
}
