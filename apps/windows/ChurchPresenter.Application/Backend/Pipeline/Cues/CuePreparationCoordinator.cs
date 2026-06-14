using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Themes;
using ChurchPresenter.Services.Media;
using ChurchPresenter.Services.Themes;

namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Coordinates cue preparation, caching, and invalidation.
/// </summary>
public interface ICuePreparationCoordinator
{
    /// <summary>Prepares a cue request into a cached prepared cue record.</summary>
    Task<PreparedCueRecord?> PrepareAsync(CueRequest request, CancellationToken cancellationToken = default);

    /// <summary>Prepares a slide cue that was already resolved by the legacy preparation service.</summary>
    Task<PreparedCueRecord?> PrepareSlideAsync(PreparedSlideCue cue, CancellationToken cancellationToken = default);

    /// <summary>Prepares a media cue that was already resolved by the legacy preparation service.</summary>
    Task<PreparedCueRecord?> PrepareMediaAsync(PreparedMediaCue cue, CancellationToken cancellationToken = default);

    /// <summary>Invalidates cached entries and returns affected content references.</summary>
    IReadOnlyList<ContentRef> HandleInvalidation(ContentRef contentRef);
}

/// <summary>
/// Wraps cue preparation services and scene compilation to produce immutable prepared cue records.
/// </summary>
public sealed class CuePreparationCoordinator(
    ICuePreparationService cuePreparation,
    ISlideSceneCompiler slideSceneCompiler,
    IThemeResolutionService themeResolution,
    IPreparedCueCache cache,
    ILiveInputCatalog liveInputCatalog,
    IMaskAssetCatalog maskAssetCatalog) : ICuePreparationCoordinator
{
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly ISlideSceneCompiler _slideSceneCompiler = slideSceneCompiler ?? throw new ArgumentNullException(nameof(slideSceneCompiler));
    private readonly IThemeResolutionService _themeResolution = themeResolution ?? throw new ArgumentNullException(nameof(themeResolution));
    private readonly IPreparedCueCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ILiveInputCatalog _liveInputCatalog = liveInputCatalog ?? throw new ArgumentNullException(nameof(liveInputCatalog));
    private readonly IMaskAssetCatalog _maskAssetCatalog = maskAssetCatalog ?? throw new ArgumentNullException(nameof(maskAssetCatalog));

    /// <inheritdoc />
    public async Task<PreparedCueRecord?> PrepareAsync(CueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_cache.TryGet(request, out PreparedCueRecord cached, out _))
            return cached;

        PreparedCueRecord? record = request.CueKind switch
        {
            CueKind.Slide or CueKind.Announcement => await PrepareSlideRequestAsync(request, cancellationToken).ConfigureAwait(false),
            CueKind.Media or CueKind.Audio => await PrepareMediaRequestAsync(request, cancellationToken).ConfigureAwait(false),
            CueKind.LiveVideo => PrepareLiveVideoRequest(request),
            CueKind.Mask => PrepareMaskRequest(request),
            _ => null,
        };

        if (record != null)
            _cache.Set(request, record);

        return record;
    }

    /// <inheritdoc />
    public Task<PreparedCueRecord?> PrepareSlideAsync(PreparedSlideCue cue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cue);
        cancellationToken.ThrowIfCancellationRequested();

        CueRequest request = CreateSlideRequest(cue);
        if (_cache.TryGet(request, out PreparedCueRecord cached, out _))
            return Task.FromResult<PreparedCueRecord?>(cached);

        PreparedCueRecord record = BuildSlideRecord(cue);
        _cache.Set(request, record);
        return Task.FromResult<PreparedCueRecord?>(record);
    }

    /// <inheritdoc />
    public Task<PreparedCueRecord?> PrepareMediaAsync(PreparedMediaCue cue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cue);
        cancellationToken.ThrowIfCancellationRequested();

        CueRequest request = CreateMediaRequest(cue);
        if (_cache.TryGet(request, out PreparedCueRecord cached, out _))
            return Task.FromResult<PreparedCueRecord?>(cached);

        PreparedCueRecord record = BuildMediaRecord(cue);
        _cache.Set(request, record);
        return Task.FromResult<PreparedCueRecord?>(record);
    }

    /// <inheritdoc />
    public IReadOnlyList<ContentRef> HandleInvalidation(ContentRef contentRef)
    {
        ArgumentNullException.ThrowIfNull(contentRef);

        _cache.Invalidate(contentRef);

        switch (contentRef.Kind)
        {
            case ContentRefKind.Presentation:
            case ContentRefKind.Slide:
                _cuePreparation.InvalidatePresentationCues(contentRef.Id);
                break;
        }

        return [contentRef];
    }

    private async Task<PreparedCueRecord?> PrepareSlideRequestAsync(CueRequest request, CancellationToken cancellationToken)
    {
        PreparedSlideCue? cue = await _cuePreparation.PrepareSlideCueAsync(
            request.ContentRef.Kind == ContentRefKind.Presentation ? request.ContentRef.Id : null,
            request.ContentRef.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return cue == null ? null : BuildSlideRecord(cue);
    }

    private Task<PreparedCueRecord?> PrepareMediaRequestAsync(CueRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return Task.FromResult<PreparedCueRecord?>(null);
    }

    private PreparedCueRecord? PrepareLiveVideoRequest(CueRequest request)
    {
        LiveInputSource? source = _liveInputCatalog.Resolve(request.ContentRef.Id);
        if (source == null)
            return null;

        RenderPayloadDescriptor payload = new()
        {
            Id = source.Id,
            Kind = RenderPayloadKind.LiveVideo,
            DisplayName = source.DisplayName,
            SourceReference = source.Id,
            Detail = new LiveVideoRenderPayload
            {
                SourceId = source.Id,
                Fit = source.Fit,
                HasAudio = source.HasAudio,
            },
        };

        return new PreparedCueRecord
        {
            Layer = OutputLayerKind.LiveVideo,
            ContentRef = request.ContentRef with { Kind = ContentRefKind.MediaAsset, Id = source.Id },
            ResourceStamp = new ResourceStamp { ContentVersion = source.Id, ResourceHash = source.Id },
            Payload = payload,
            SizeCostBytes = 1024,
            PreparedAt = DateTimeOffset.UtcNow,
        };
    }

    private PreparedCueRecord? PrepareMaskRequest(CueRequest request)
    {
        MaskAsset? mask = _maskAssetCatalog.Resolve(request.ContentRef.Id);
        if (mask == null)
            return null;

        RenderPayloadDescriptor payload = new()
        {
            Id = $"mask:{mask.Id}",
            Kind = RenderPayloadKind.Mask,
            DisplayName = mask.DisplayName,
            SourceReference = $"Mask:{mask.Id}",
            Detail = new MaskRenderPayload
            {
                MaskId = mask.Id,
                MaskKind = mask.MaskKind,
                SourcePath = mask.SourcePath,
                Scene = mask.Scene,
                Invert = mask.Invert,
            },
        };

        return new PreparedCueRecord
        {
            Layer = OutputLayerKind.Mask,
            ContentRef = request.ContentRef with { Kind = ContentRefKind.Mask, Id = mask.Id },
            ResourceStamp = new ResourceStamp { ContentVersion = mask.Id, ResourceHash = mask.Scene?.Version ?? mask.Id },
            Payload = payload,
            SizeCostBytes = mask.Scene == null ? 1024 : Math.Max(1024, mask.Scene.Nodes.Count * 256),
            PreparedAt = DateTimeOffset.UtcNow,
        };
    }

    private PreparedCueRecord BuildSlideRecord(PreparedSlideCue cue)
    {
        PresentationProject? project = cue.Presentation?.Project;
        PresentationSlide? slide = cue.Slide ?? project?.Slides.ElementAtOrDefault(cue.SlideIndex);
        ThemeResolutionResult theme = _themeResolution.ResolveThemeSlide(project, slide);
        SceneCompileResult? compileResult = slide == null && cue.Scene == null
            ? null
            : cue.Scene == null
                ? _slideSceneCompiler.Compile(new SceneCompileRequest
                {
                    Project = project,
                    Slide = slide,
                    ThemeSlide = theme.ThemeSlide,
                    ArrangementInstanceKey = cue.InstanceKey,
                    Intent = RenderIntent.AudienceOutput,
                })
                : null;

        SlideScene scene = cue.Scene ?? compileResult?.Scene ?? SlideScene.Empty;
        IReadOnlyDictionary<string, SlideScene> variantScenes = BuildVariantScenes(project, slide, cue, theme);
        string slideId = string.IsNullOrWhiteSpace(cue.SlideId)
            ? scene.SlideId ?? string.Empty
            : cue.SlideId;
        ResourceStamp stamp = new()
        {
            ContentVersion = cue.PresentationPath,
            ThemeVersion = theme.Binding?.ThemeId ?? project?.Manifest.ThemeId,
            ResourceHash = scene.Version,
        };

        RenderPayloadDescriptor payload = new()
        {
            Id = slideId,
            Kind = RenderPayloadKind.Presentation,
            DisplayName = FirstNonWhiteSpace(slide?.SectionLabel, slide?.Id, slideId),
            SourceReference = slideId,
            Detail = new PresentationRenderPayload
            {
                PresentationId = project?.Manifest.PresentationId,
                PresentationPath = cue.PresentationPath,
                SlideId = slideId,
                ArrangementInstanceKey = cue.InstanceKey,
                Scene = scene,
                VariantScenes = variantScenes,
                ThemeId = stamp.ThemeVersion,
            },
        };

        int sizeCost = EstimateSceneCost(scene);
        return new PreparedCueRecord
        {
            Layer = cue.LayerKind,
            ContentRef = new ContentRef
            {
                Kind = ContentRefKind.Slide,
                Id = slideId,
                Version = cue.PresentationPath,
            },
            ResourceStamp = stamp,
            Payload = payload,
            Transition = CloneTransition(TransitionResolver.Resolve(slide, project?.Arrangement)),
            RetainedResources = sizeCost > 0 ? [new ResourceHandle(slideId, sizeCost)] : Array.Empty<IResourceHandle>(),
            SizeCostBytes = sizeCost,
            PreparedAt = cue.PreparedAt,
        };
    }

    // Bound on the number of theme-variant scenes precompiled per slide cue. Looks route a small set
    // of named theme variants (e.g. a lower-third stream layout); compiling every theme slide for
    // every cue would be wasteful, so cap and rely on the prepared-cue cache to amortize the cost.
    private const int MaxVariantScenes = 12;

    private static readonly IReadOnlyDictionary<string, SlideScene> EmptyVariantScenes =
        new Dictionary<string, SlideScene>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, SlideScene> BuildVariantScenes(
        PresentationProject? project,
        PresentationSlide? slide,
        PreparedSlideCue cue,
        ThemeResolutionResult baseTheme)
    {
        // Variant scenes require a real project/slide to recompile against alternate theme slides;
        // a pre-supplied scene (cue.Scene) has no compile inputs.
        if (project == null || slide == null || cue.Scene != null || baseTheme.Theme == null)
            return EmptyVariantScenes;

        string? baseThemeSlideId = baseTheme.ThemeSlide?.Id;
        Dictionary<string, SlideScene> variants = new(StringComparer.OrdinalIgnoreCase);
        int budget = MaxVariantScenes;

        foreach (var variant in baseTheme.Theme.Slides)
        {
            if (budget <= 0)
                break;
            if (string.IsNullOrWhiteSpace(variant.Id)
                || string.Equals(variant.Id, baseThemeSlideId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ThemeResolutionResult variantTheme = _themeResolution.ResolveThemeSlide(project, slide, variant.Id);
            if (variantTheme.ThemeSlide == null)
                continue;

            SceneCompileResult? compiled = _slideSceneCompiler.Compile(new SceneCompileRequest
            {
                Project = project,
                Slide = slide,
                ThemeSlide = variantTheme.ThemeSlide,
                ArrangementInstanceKey = cue.InstanceKey,
                Intent = RenderIntent.AudienceOutput,
            });
            if (compiled?.Scene == null)
                continue;

            // Key by the theme slide id and any roles so Look-routed variant ids resolve either way.
            variants[variant.Id] = compiled.Scene;
            foreach (string role in variant.Roles)
            {
                if (!string.IsNullOrWhiteSpace(role))
                    variants.TryAdd(role, compiled.Scene);
            }

            budget--;
        }

        return variants.Count == 0 ? EmptyVariantScenes : variants;
    }

    private static PreparedCueRecord BuildMediaRecord(PreparedMediaCue cue)
    {
        OutputLayerMedia media = cue.Media;
        string mediaId = media.MediaId;
        ResourceStamp stamp = new()
        {
            ContentVersion = media.ResolvedSourcePath ?? mediaId,
            ResourceHash = mediaId,
        };

        RenderPayloadDescriptor payload = new()
        {
            Id = mediaId,
            Kind = ResolveMediaKind(media.MediaType),
            DisplayName = string.IsNullOrWhiteSpace(media.DisplayName) ? mediaId : media.DisplayName,
            SourceReference = cue.Target,
            Detail = new MediaRenderPayload
            {
                Media = media,
                Target = cue.Target,
            },
        };

        return new PreparedCueRecord
        {
            Layer = cue.LayerKind,
            ContentRef = new ContentRef
            {
                Kind = cue.LayerKind == OutputLayerKind.Audio ? ContentRefKind.AudioCue : ContentRefKind.MediaAsset,
                Id = mediaId,
                Version = stamp.ContentVersion,
            },
            ResourceStamp = stamp,
            Payload = payload,
            Transition = CloneTransition(media.Transition),
            SizeCostBytes = 1024,
            PreparedAt = cue.PreparedAt,
        };
    }

    private static CueRequest CreateSlideRequest(PreparedSlideCue cue) =>
        new()
        {
            CueKind = cue.LayerKind == OutputLayerKind.Announcements ? CueKind.Announcement : CueKind.Slide,
            Layer = cue.LayerKind,
            ContentRef = new ContentRef
            {
                Kind = ContentRefKind.Slide,
                Id = cue.SlideId,
                Version = cue.PresentationPath,
            },
        };

    private static CueRequest CreateMediaRequest(PreparedMediaCue cue) =>
        new()
        {
            CueKind = cue.LayerKind == OutputLayerKind.Audio ? CueKind.Audio : CueKind.Media,
            Layer = cue.LayerKind,
            ContentRef = new ContentRef
            {
                Kind = cue.LayerKind == OutputLayerKind.Audio ? ContentRefKind.AudioCue : ContentRefKind.MediaAsset,
                Id = cue.Media.MediaId,
                Version = cue.Media.ResolvedSourcePath,
            },
        };

    private static RenderPayloadKind ResolveMediaKind(string? mediaType) =>
        string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? RenderPayloadKind.Video
            : RenderPayloadKind.Image;

    private static int EstimateSceneCost(SlideScene scene) =>
        Math.Max(1024, scene.Nodes.Count * 256);

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static SlideTransition? CloneTransition(SlideTransition? transition)
    {
        SlideTransition? normalized = TransitionResolver.Normalize(transition);
        if (normalized == null)
            return null;

        return new SlideTransition
        {
            Type = normalized.Type,
            Duration = normalized.Duration,
            Easing = normalized.Easing,
            Parameters = normalized.Parameters == null
                ? null
                : new Dictionary<string, string>(normalized.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }
}
