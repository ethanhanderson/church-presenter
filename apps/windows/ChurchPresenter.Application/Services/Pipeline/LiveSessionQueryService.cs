using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Output;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Projects the pipeline live session snapshot into operator query DTOs.
/// </summary>
public sealed class LiveSessionQueryService : ILiveSessionQueryService
{
    private readonly CuePipelineRuntime _runtime;
    private readonly IOutputRoutingService _routing;

    /// <summary>Creates the query service and begins tracking pipeline changes.</summary>
    public LiveSessionQueryService(CuePipelineRuntime runtime, IOutputRoutingService routing)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));

        _runtime.Changed += HandleChanged;
        _routing.Changed += HandleRoutingChanged;
        Current = BuildSnapshot(_runtime.Snapshot, _routing);
    }

    /// <inheritdoc />
    public event EventHandler<LiveSessionQueryChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LiveSessionQuerySnapshot Current { get; private set; }

    private void HandleChanged(object? sender, EventArgs args)
    {
        LiveSessionQuerySnapshot next = BuildSnapshot(_runtime.Snapshot, _routing);
        if (AreEquivalent(Current, next))
            return;

        Current = next;
        Changed?.Invoke(this, new LiveSessionQueryChangedEventArgs { Snapshot = Current });
    }

    private void HandleRoutingChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        LiveSessionQuerySnapshot next = BuildSnapshot(_runtime.Snapshot, _routing);
        if (AreEquivalent(Current, next))
            return;

        Current = next;
        Changed?.Invoke(this, new LiveSessionQueryChangedEventArgs { Snapshot = Current });
    }

    private static bool AreEquivalent(LiveSessionQuerySnapshot left, LiveSessionQuerySnapshot right)
    {
        return left.Version == right.Version
            && string.Equals(left.ActiveLookId, right.ActiveLookId, StringComparison.Ordinal)
            && string.Equals(left.LivePresentationPath, right.LivePresentationPath, StringComparison.Ordinal)
            && string.Equals(left.LiveSlideId, right.LiveSlideId, StringComparison.Ordinal)
            && left.Looks.SequenceEqual(right.Looks)
            && AreLayerSlotsEquivalent(left.LayerSlots, right.LayerSlots)
            && left.ActiveLayers.SequenceEqual(right.ActiveLayers)
            && AreClearGroupsEquivalent(left.ClearGroups, right.ClearGroups);
    }

    private static bool AreLayerSlotsEquivalent(
        IReadOnlyDictionary<BackendOutputLayerKind, LiveLayerSlotQuery> left,
        IReadOnlyDictionary<BackendOutputLayerKind, LiveLayerSlotQuery> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (KeyValuePair<BackendOutputLayerKind, LiveLayerSlotQuery> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out LiveLayerSlotQuery? other)
                || pair.Value != other)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreClearGroupsEquivalent(
        IReadOnlyList<LiveClearGroupQuery> left,
        IReadOnlyList<LiveClearGroupQuery> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            LiveClearGroupQuery current = left[i];
            LiveClearGroupQuery next = right[i];
            if (!string.Equals(current.Id, next.Id, StringComparison.Ordinal)
                || !string.Equals(current.Name, next.Name, StringComparison.Ordinal)
                || !string.Equals(current.Icon, next.Icon, StringComparison.Ordinal)
                || current.TintEnabled != next.TintEnabled
                || !string.Equals(current.TintColor, next.TintColor, StringComparison.Ordinal)
                || !current.Scopes.SequenceEqual(next.Scopes)
                || !current.Layers.SequenceEqual(next.Layers)
                || current.StopPresentationTimeline != next.StopPresentationTimeline
                || current.StopAnnouncementTimeline != next.StopAnnouncementTimeline)
            {
                return false;
            }
        }

        return true;
    }

    internal static LiveSessionQuerySnapshot BuildSnapshot(
        LiveSessionSnapshot snapshot,
        IOutputRoutingService routing)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(routing);

        LiveRenderSessionState sessionState = snapshot.SessionState;
        LayerSlot? slideSlot = snapshot.LayerSlots.TryGetValue(BackendOutputLayerKind.Slide, out LayerSlot? slot)
            ? slot
            : null;
        PresentationRenderPayload? slidePayload = slideSlot?.PreparedCue?.Payload.Detail as PresentationRenderPayload
            ?? slideSlot?.State.Payload?.Detail as PresentationRenderPayload;

        return new LiveSessionQuerySnapshot
        {
            Version = snapshot.Version,
            ActiveLookId = routing.ActiveLookId,
            LivePresentationPath = slidePayload?.PresentationPath,
            LiveSlideId = slidePayload?.SlideId,
            Looks = routing.Looks
                .Select(look => new LiveLookQueryOption
                {
                    Id = look.Id,
                    Name = look.Name,
                    IsActive = string.Equals(look.Id, routing.ActiveLookId, StringComparison.OrdinalIgnoreCase),
                })
                .ToArray(),
            LayerSlots = snapshot.LayerSlots
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => new LiveLayerSlotQuery
                    {
                        Kind = pair.Key,
                        ClearOverride = pair.Value.ClearOverride,
                        PreparedContentId = pair.Value.PreparedCue?.ContentRef.Id,
                        PayloadId = pair.Value.State.Payload?.Id,
                        EnterTimestamp = pair.Value.EnterTimestamp,
                    }),
            ActiveLayers = BuildActiveLayerQueries(sessionState).ToArray(),
            ClearGroups = routing.ActiveLook.ClearGroups
                .Select(clearGroup => new LiveClearGroupQuery
                {
                    Id = clearGroup.Id,
                    Name = clearGroup.Name,
                    Icon = clearGroup.Icon,
                    TintEnabled = clearGroup.TintEnabled,
                    TintColor = clearGroup.TintColor,
                    Scopes = clearGroup.Scopes.ToArray(),
                    Layers = OutputRoutingDefaults.ResolveClearGroupLayers(clearGroup)
                        .OrderBy(static layer => layer)
                        .ToArray(),
                    StopPresentationTimeline = clearGroup.StopPresentationTimeline,
                    StopAnnouncementTimeline = clearGroup.StopAnnouncementTimeline,
                })
                .Where(static clearGroup => clearGroup.Layers.Count > 0)
                .OrderBy(static clearGroup => clearGroup.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private static IEnumerable<LiveLayerStateQuery> BuildActiveLayerQueries(LiveRenderSessionState state)
    {
        foreach (LayerState layerState in state.Layers.Values
                     .OrderBy(static layer => layer.Kind)
                     .Where(static layer => layer.Payload != null || layer.IsSuppressed || layer.IsCleared || !string.IsNullOrWhiteSpace(layer.Diagnostics)))
        {
            OutputLayerDefinition definition = OutputRoutingDefaults.GetLayerDefinition(layerState.Kind);
            PresentationRenderPayload? presentationPayload = layerState.Payload?.Detail as PresentationRenderPayload;
            yield return new LiveLayerStateQuery
            {
                Kind = layerState.Kind,
                Id = definition.Id,
                DisplayName = definition.DisplayName,
                IsLive = layerState.Payload != null && layerState.IsVisible && !layerState.IsSuppressed && !layerState.IsCleared,
                IsSuppressed = layerState.IsSuppressed,
                IsCleared = layerState.IsCleared,
                PayloadId = layerState.Payload?.Id,
                PayloadName = layerState.Payload?.DisplayName,
                SourceReference = layerState.Payload?.SourceReference,
                PayloadPresentationPath = presentationPayload?.PresentationPath,
                PayloadSlideId = presentationPayload?.SlideId,
                PayloadInstanceKey = presentationPayload?.ArrangementInstanceKey,
                SourceCommandId = layerState.SourceCommandId,
                Diagnostics = layerState.Diagnostics,
            };
        }
    }
}
