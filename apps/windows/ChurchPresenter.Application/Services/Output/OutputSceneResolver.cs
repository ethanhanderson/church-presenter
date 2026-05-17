
namespace ChurchPresenter.Services.Output;

/// <summary>
/// Resolves live output and snapshot scene descriptors from backend-adapted frames and slide models.
/// This is the native Windows replacement for legacy renderer-side behavior inference.
/// </summary>
public static class OutputSceneResolver
{
    /// <summary>Resolves a live output scene from a pre-resolved render frame.</summary>
    public static OutputScene ResolveFromRenderFrame(RenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var presentation = BuildPresentationScene(
            frame.Slide,
            frame.BuildIndex,
            frame.VisibleLayerIds,
            frame.SuppressPresentation);
        var media = BuildMediaScene(frame.MediaLayers, frame.SuppressMedia);
        var web = new WebScene
        {
            Layers = presentation.Layers
                .Where(static layer => layer.Kind == PresentationSceneLayerKind.Web)
                .Select(static layer => new WebSceneLayer
                {
                    Id = layer.Id,
                    Layer = (WebLayer)layer.Layer,
                })
                .ToArray(),
        };

        return new OutputScene
        {
            Project = frame.Project,
            ProgramSlideId = frame.ProgramSlideId,
            Presentation = presentation,
            Media = media,
            Web = web,
            Transition = CloneTransition(frame.Transition),
            MediaTransition = CloneTransition(frame.MediaTransition),
            IsBlackout = frame.IsBlackout,
            IsClear = frame.IsClear,
            OutputAspectRatioOverride = frame.OutputAspectRatioOverride,
            OutputScaleMode = PresentationModelUtilities.NormalizeOutputScaleMode(frame.OutputScaleMode),
        };
    }

    /// <summary>
    /// Resolves a static snapshot scene for thumbnail/editor/export surfaces.
    /// Unlike live scenes, this preserves only capturable/static descriptors.
    /// </summary>
    public static SnapshotScene ResolveSnapshot(
        PresentationProject? project,
        PresentationSlide? slide,
        IReadOnlyList<string>? visibleLayerIds = null,
        MediaLayersState? mediaLayers = null,
        bool suppressPresentation = false,
        bool suppressMedia = false,
        bool isBlackout = false,
        bool isClear = false,
        string? outputAspectRatioOverride = null,
        string? outputScaleMode = null)
    {
        var presentation = BuildPresentationScene(slide, -1, visibleLayerIds, suppressPresentation);
        var media = BuildMediaScene(mediaLayers ?? SlideMediaLayerBuilder.Build(slide), suppressMedia);
        var web = new WebScene
        {
            Layers = presentation.Layers
                .Where(static layer => layer.Kind == PresentationSceneLayerKind.Web)
                .Select(static layer => new WebSceneLayer
                {
                    Id = layer.Id,
                    Layer = (WebLayer)layer.Layer,
                })
                .ToArray(),
        };

        return new SnapshotScene
        {
            Project = project,
            Presentation = presentation,
            Media = media,
            Web = web,
            IsBlackout = isBlackout,
            IsClear = isClear,
            OutputAspectRatioOverride = outputAspectRatioOverride,
            OutputScaleMode = PresentationModelUtilities.NormalizeOutputScaleMode(outputScaleMode),
        };
    }

    private static PresentationScene BuildPresentationScene(
        PresentationSlide? slide,
        int buildIndex,
        IReadOnlyList<string>? visibleLayerIds,
        bool suppressed)
    {
        var visibleSet = visibleLayerIds == null || visibleLayerIds.Count == 0
            ? null
            : visibleLayerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var layers = slide?.Layers
            .Where(static layer => layer.Visible)
            .Where(layer => visibleSet == null || visibleSet.Contains(layer.Id))
            .Select(MapPresentationLayer)
            .ToArray()
            ?? Array.Empty<PresentationSceneLayer>();

        return new PresentationScene
        {
            Slide = slide,
            BuildIndex = buildIndex,
            VisibleLayerIds = visibleLayerIds == null ? null : visibleLayerIds.ToArray(),
            Background = BuildBackgroundScene(slide?.Background),
            Layers = layers,
            Suppressed = suppressed,
        };
    }

    private static MediaScene BuildMediaScene(MediaLayersState? mediaLayers, bool suppressed)
    {
        mediaLayers ??= new MediaLayersState();

        return new MediaScene
        {
            Underlay = new MediaSceneSlot("mediaUnderlay", CloneOutputMedia(mediaLayers.MediaUnderlay)),
            Overlay = new MediaSceneSlot("mediaOverlay", CloneOutputMedia(mediaLayers.MediaOverlay)),
            Audio = new MediaSceneSlot("audio", CloneOutputMedia(mediaLayers.Audio)),
            Suppressed = suppressed,
        };
    }

    private static PresentationSceneLayer MapPresentationLayer(SlideLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        return layer switch
        {
            TextLayer => new PresentationSceneLayer
            {
                Id = layer.Id,
                Layer = layer,
                Kind = PresentationSceneLayerKind.Text,
            },
            ShapeLayer => new PresentationSceneLayer
            {
                Id = layer.Id,
                Layer = layer,
                Kind = PresentationSceneLayerKind.Shape,
            },
            MediaLayer => new PresentationSceneLayer
            {
                Id = layer.Id,
                Layer = layer,
                Kind = PresentationSceneLayerKind.Media,
                UsesExternalContent = true,
            },
            WebLayer => new PresentationSceneLayer
            {
                Id = layer.Id,
                Layer = layer,
                Kind = PresentationSceneLayerKind.Web,
                UsesExternalContent = true,
            },
            VectorLayer => new PresentationSceneLayer
            {
                Id = layer.Id,
                Layer = layer,
                Kind = PresentationSceneLayerKind.Vector,
            },
            _ => throw new NotSupportedException($"Unsupported slide layer type '{layer.GetType().Name}'."),
        };
    }

    private static PresentationBackgroundScene BuildBackgroundScene(SlideBackground? background)
    {
        return new PresentationBackgroundScene
        {
            Background = background,
            Media = background switch
            {
                ImageSlideBackground image => new BackgroundMediaScene
                {
                    MediaId = image.MediaId,
                    MediaType = "image",
                    Fit = image.Fit,
                    Opacity = image.Opacity,
                },
                VideoSlideBackground video => new BackgroundMediaScene
                {
                    MediaId = video.MediaId,
                    MediaType = "video",
                    Fit = video.Fit,
                    Opacity = video.Opacity,
                    Loop = video.Loop,
                    Muted = video.Muted,
                },
                _ => null,
            },
        };
    }

    private static OutputLayerMedia? CloneOutputMedia(OutputLayerMedia? media)
    {
        if (media == null)
            return null;

        return new OutputLayerMedia
        {
            MediaId = media.MediaId,
            MediaType = media.MediaType,
            DisplayName = media.DisplayName,
            Fit = media.Fit,
            Loop = media.Loop,
            Muted = media.Muted,
            Autoplay = media.Autoplay,
            Transition = CloneTransition(media.Transition),
            ResolvedSourcePath = media.ResolvedSourcePath,
        };
    }

    private static SlideTransition? CloneTransition(SlideTransition? transition)
    {
        if (transition == null)
            return null;

        return new SlideTransition
        {
            Type = transition.Type,
            Duration = transition.Duration,
            Easing = transition.Easing,
            Parameters = transition.Parameters == null
                ? null
                : new Dictionary<string, string>(transition.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }
}