using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;

using AudienceFrame = ChurchPresenter.Backend.Rendering.AudienceRenderFrame;
using StageFrame = ChurchPresenter.Backend.Rendering.StageRenderFrame;

namespace ChurchPresenter.Backend.Pipeline.Rendering;

/// <summary>
/// Composes audience and stage frames from a pipeline live session snapshot.
/// </summary>
public sealed class FrameComposer(IRenderFrameResolver frameResolver)
{
    private readonly IRenderFrameResolver _frameResolver = frameResolver ?? throw new ArgumentNullException(nameof(frameResolver));

    /// <summary>
    /// Documented background-vs-media policy note (ProPresenter 7.11): a slide's own background
    /// media composites above the dedicated Media layer.
    /// </summary>
    internal const string BackgroundAboveMediaNote =
        "Slide background media composited above the Media layer (PP 7.11).";

    /// <summary>Resolves render frames for the supplied live session snapshot.</summary>
    public RenderFrameSet Compose(LiveSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RenderFrameSet frames = IncludeClearingLayerSlots(_frameResolver.Resolve(snapshot.SessionState), snapshot);
        return AnnotateBackgroundMediaPolicy(frames);
    }

    private static RenderFrameSet AnnotateBackgroundMediaPolicy(RenderFrameSet frames)
    {
        Dictionary<string, AudienceFrame> audienceFrames = new(frames.AudienceFrames, StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach ((string screenId, AudienceFrame frame) in frames.AudienceFrames)
        {
            bool mediaActive = frame.Layers.Any(static layer =>
                layer.Kind == OutputLayerKind.Media
                && layer.IsVisible
                && !layer.IsSuppressed
                && layer.Payload.Detail is MediaRenderPayload);

            bool slideBackgroundMedia = frame.Layers.Any(static layer =>
                layer.Kind == OutputLayerKind.Slide
                && layer.IsVisible
                && !layer.IsSuppressed
                && layer.Payload.Detail is PresentationRenderPayload presentation
                && !string.IsNullOrWhiteSpace(presentation.Scene.Background.MediaId));

            if (!mediaActive || !slideBackgroundMedia || frame.Diagnostics.Notes.Contains(BackgroundAboveMediaNote))
                continue;

            audienceFrames[screenId] = frame with
            {
                Diagnostics = frame.Diagnostics with
                {
                    Notes = [.. frame.Diagnostics.Notes, BackgroundAboveMediaNote],
                },
            };
            changed = true;
        }

        return changed ? frames with { AudienceFrames = audienceFrames } : frames;
    }

    private static RenderFrameSet IncludeClearingLayerSlots(RenderFrameSet frames, LiveSessionSnapshot snapshot)
    {
        Dictionary<string, AudienceFrame> audienceFrames = new(frames.AudienceFrames, StringComparer.OrdinalIgnoreCase);
        foreach ((string screenId, AudienceFrame frame) in frames.AudienceFrames)
        {
            ScreenLayerRouting route = snapshot.SessionState.ActiveLook.ResolveRoute(screenId);
            List<RenderLayerDescriptor> layers = frame.Layers.ToList();
            bool changed = false;

            foreach (LayerSlot slot in snapshot.LayerSlots.Values)
            {
                if (!ShouldComposeClearingSlot(slot, route, out RenderPayloadDescriptor? payload))
                    continue;

                RenderLayerDescriptor descriptor = new()
                {
                    Kind = slot.Kind,
                    Payload = payload,
                    IsVisible = false,
                    IsSuppressed = true,
                    ClearState = slot.State.ClearState == LayerClearState.None
                        ? LayerClearState.Cleared
                        : slot.State.ClearState,
                    SourceCommandId = slot.State.SourceCommandId,
                    Provenance = slot.State.Provenance,
                    Transition = slot.Transition,
                    PlayerState = slot.State.PlayerState,
                    RenderErrors = slot.State.RenderErrors,
                    Diagnostics = slot.State.Diagnostics,
                };

                int existingIndex = layers.FindIndex(layer => layer.Kind == slot.Kind);
                if (existingIndex >= 0)
                    layers[existingIndex] = descriptor;
                else
                    layers.Add(descriptor);
                changed = true;
            }

            if (changed)
            {
                audienceFrames[screenId] = frame with
                {
                    Layers = OutputLayerStack
                        .OrderByStack(layers, static layer => layer.Kind)
                        .ToArray(),
                };
            }
        }

        return frames with { AudienceFrames = audienceFrames };
    }

    private static bool ShouldComposeClearingSlot(
        LayerSlot slot,
        ScreenLayerRouting route,
        out RenderPayloadDescriptor payload)
    {
        payload = slot.PriorPayload ?? slot.PreparedCue?.Payload ?? slot.State.Payload ?? new RenderPayloadDescriptor();

        if (slot.Kind is OutputLayerKind.Audio or OutputLayerKind.Mask)
            return false;

        bool isCleared = slot.ClearOverride
            || slot.State.IsCleared
            || slot.State.ClearState != LayerClearState.None;
        if (!isCleared || !route.Routes(slot.Kind))
            return false;

        return !string.IsNullOrWhiteSpace(payload.Id);
    }
}
