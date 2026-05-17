using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Stores runtime-only backend overrides that should survive playback or routing snapshot rebuilds.
/// </summary>
internal sealed class LiveProductionRuntimeOverrides
{
    private readonly Dictionary<string, OverlayContentState> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimerSnapshot> _timers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CaptureSessionState> _captureSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StageLayoutOverride> _stageLayouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<OutputLayerKind, string?> _clearedLayerPayloadIds = new();

    /// <summary>Records the latest generated overlay state.</summary>
    public void SetOverlay(OverlayContentState overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlays[overlay.Id] = overlay;
    }

    /// <summary>Records the latest timer snapshot.</summary>
    public void SetTimer(TimerSnapshot timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        _timers[timer.Id] = timer;
    }

    /// <summary>Records the latest capture-session state.</summary>
    public void SetCaptureSession(CaptureSessionState captureSession)
    {
        ArgumentNullException.ThrowIfNull(captureSession);
        _captureSessions[captureSession.Metadata.Id] = captureSession;
    }

    /// <summary>Records generated-system and stage changes represented by an applied command batch.</summary>
    public void RecordAppliedBatch(ActionBatch batch, LookPreset activeLook, LiveRenderSessionState stateBeforeApply)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(activeLook);
        ArgumentNullException.ThrowIfNull(stateBeforeApply);

        foreach (LiveAction action in batch.Actions)
        {
            switch (action.Kind)
            {
                case LiveActionKind.SetLayerPayload when action.Target.LayerKind is OutputLayerKind layerKind:
                    _clearedLayerPayloadIds.Remove(layerKind);
                    break;
                case LiveActionKind.SetOverlayState when action.Overlay != null:
                    SetOverlay(action.Overlay);
                    break;
                case LiveActionKind.SetTimerState when action.Timer != null:
                    SetTimer(action.Timer);
                    break;
                case LiveActionKind.SetCaptureSessionState when action.CaptureSession != null:
                    SetCaptureSession(action.CaptureSession);
                    break;
                case LiveActionKind.SetStageLayout
                    when !string.IsNullOrWhiteSpace(action.Target.Id)
                        && !string.IsNullOrWhiteSpace(action.StageLayoutId):
                    SetStageLayout(action.Target.Id, action.StageLayoutId, action.DeliveryMode);
                    break;
                case LiveActionKind.ClearLayers:
                    ClearLayers(ResolveClearLayers(action.Clear, activeLook), stateBeforeApply);
                    break;
            }
        }
    }

    /// <summary>Removes runtime overlay overrides that belong to the specified backend layers.</summary>
    public void ClearLayers(IReadOnlySet<OutputLayerKind> layers, LiveRenderSessionState? stateBeforeApply = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            return;

        HashSet<OutputLayerKind> clearableLayers = layers
            .Where(static layer => layer != OutputLayerKind.Mask)
            .ToHashSet();

        foreach (OutputLayerKind layer in clearableLayers)
        {
            _clearedLayerPayloadIds[layer] = stateBeforeApply?.Layers.TryGetValue(layer, out LayerState? layerState) == true
                ? layerState.Payload?.Id
                : null;
        }

        string[] overlayIdsToRemove = _overlays
            .Where(static pair => OverlayLayerIdentity.TryGetAudienceLayer(pair.Value.Kind, out _))
            .Where(pair => OverlayLayerIdentity.TryGetAudienceLayer(pair.Value.Kind, out OutputLayerKind layerKind)
                && clearableLayers.Contains(layerKind))
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (string overlayId in overlayIdsToRemove)
            _overlays.Remove(overlayId);
    }

    /// <summary>Releases remembered clear overrides for layers the operator intentionally made live again.</summary>
    public void ReleaseClearedLayers(IReadOnlySet<OutputLayerKind> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        foreach (OutputLayerKind layer in layers)
            _clearedLayerPayloadIds.Remove(layer);
    }

    private static IReadOnlySet<OutputLayerKind> ResolveClearLayers(ClearCommand? clear, LookPreset activeLook)
    {
        if (clear == null)
            return new HashSet<OutputLayerKind>();

        if (clear.Layers.Count > 0)
            return clear.Layers.Where(static layer => layer != OutputLayerKind.Mask).ToHashSet();

        if (string.IsNullOrWhiteSpace(clear.ClearGroupId))
            return new HashSet<OutputLayerKind>();

        ClearGroup? clearGroup = activeLook.ClearGroups.FirstOrDefault(group =>
            string.Equals(group.Id, clear.ClearGroupId, StringComparison.OrdinalIgnoreCase));

        return clearGroup?.Layers.Where(static layer => layer != OutputLayerKind.Mask).ToHashSet()
            ?? new HashSet<OutputLayerKind>();
    }

    /// <summary>Records the latest stage-layout selection for a stage screen.</summary>
    public void SetStageLayout(string screenId, string stageLayoutId, StageAudienceCommandMode deliveryMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageLayoutId);

        _stageLayouts[screenId] = new StageLayoutOverride(stageLayoutId, deliveryMode);
    }

    /// <summary>Builds deterministic replay batches for the currently remembered runtime overrides.</summary>
    public IReadOnlyList<ActionBatch> CreateReplayBatches(ILiveCommandExecutor executor, LiveRenderSessionState baseState)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(baseState);

        List<ActionBatch> batches = [];

        foreach ((string screenId, StageLayoutOverride stageLayout) in _stageLayouts.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            batches.Add(executor.Expand(LiveCommandExecutor.SetStageLayout(
                screenId,
                stageLayout.LayoutId,
                stageLayout.DeliveryMode,
                source: new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "runtime-overrides" })));
        }

        PruneClearsForReplacedPayloads(baseState);

        if (_clearedLayerPayloadIds.Count > 0)
        {
            batches.Add(executor.Expand(LiveCommandExecutor.ClearLayers(
                _clearedLayerPayloadIds.Keys,
                source: new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "runtime-overrides" })));
        }

        foreach (OverlayContentState overlay in _overlays.Values.OrderBy(static overlay => overlay.Id, StringComparer.OrdinalIgnoreCase))
        {
            batches.Add(executor.Expand(LiveCommandExecutor.SetOverlay(
                overlay,
                source: new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "runtime-overrides" })));
        }

        foreach (TimerSnapshot timer in _timers.Values.OrderBy(static timer => timer.Id, StringComparer.OrdinalIgnoreCase))
        {
            batches.Add(executor.Expand(LiveCommandExecutor.SetTimer(
                timer,
                source: new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "runtime-overrides" })));
        }

        foreach (CaptureSessionState captureSession in _captureSessions.Values.OrderBy(static session => session.Metadata.Id, StringComparer.OrdinalIgnoreCase))
        {
            batches.Add(executor.Expand(LiveCommandExecutor.SetCaptureSession(
                captureSession,
                source: new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "runtime-overrides" })));
        }

        return batches;
    }

    private void PruneClearsForReplacedPayloads(LiveRenderSessionState baseState)
    {
        OutputLayerKind[] layersToRelease = _clearedLayerPayloadIds
            .Where(pair =>
            {
                string? currentPayloadId = baseState.Layers.TryGetValue(pair.Key, out LayerState? layerState)
                    ? layerState.Payload?.Id
                    : null;
                return !string.Equals(currentPayloadId, pair.Value, StringComparison.OrdinalIgnoreCase);
            })
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (OutputLayerKind layer in layersToRelease)
            _clearedLayerPayloadIds.Remove(layer);
    }

    private sealed record StageLayoutOverride(string LayoutId, StageAudienceCommandMode DeliveryMode);
}