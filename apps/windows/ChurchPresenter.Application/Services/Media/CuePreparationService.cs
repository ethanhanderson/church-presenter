using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Services.Media;

/// <summary>
/// Resolves slide and media selections into immediately enterable cues for the output engine.
/// </summary>
public sealed class CuePreparationService(
    IShowSessionCache sessionCache,
    IPresentationDocumentService presentationDocs,
    IMediaLibraryService mediaLibrary,
    IContentStore? contentStore = null,
    ISlideSceneCompiler? slideSceneCompiler = null,
    IThemeResolutionService? themeResolution = null) : ICuePreparationService, IContentCacheInvalidator
{
    private readonly IShowSessionCache _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
    private readonly IPresentationDocumentService _presentationDocs = presentationDocs ?? throw new ArgumentNullException(nameof(presentationDocs));
    private readonly IMediaLibraryService _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    private readonly IContentStore? _contentStore = contentStore;
    private readonly ISlideSceneCompiler _slideSceneCompiler = slideSceneCompiler ?? new SlideSceneCompiler();
    private readonly IThemeResolutionService _themeResolution = themeResolution ?? new ThemeResolutionService();

    private readonly Dictionary<string, PreparedSlideCue> _preparedSlides = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<PreparedSlideCue?> PrepareSlideCueAsync(
        string? presentationPath,
        string slideId,
        string? instanceKey = null,
        PresentationDocument? fallbackDocument = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(slideId))
            return null;

        var resolvedDocument = ResolvePresentationDocument(presentationPath, fallbackDocument);
        if (resolvedDocument == null)
            return null;

        var resolvedPath = string.IsNullOrWhiteSpace(resolvedDocument.SourcePath)
            ? presentationPath ?? string.Empty
            : resolvedDocument.SourcePath;
        var key = BuildSlideKey(resolvedPath, slideId, instanceKey);

        var cached = GetFreshCachedCue(key, resolvedDocument, slideId, instanceKey);
        if (cached != null)
            return cached;

        var slideIndex = resolvedDocument.Slides.ToList().FindIndex(slide =>
            string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
        if (slideIndex < 0)
            return null;

        var slideDto = resolvedDocument.Slides[slideIndex];
        PresentationSlide? typedSlide = resolvedDocument.Project?.Slides.ElementAtOrDefault(slideIndex);
        ThemeResolutionResult theme = _themeResolution.ResolveThemeSlide(resolvedDocument.Project, typedSlide);
        SceneCompileResult? scene = typedSlide == null
            ? null
            : _slideSceneCompiler.Compile(new SceneCompileRequest
            {
                Project = resolvedDocument.Project,
                Slide = typedSlide,
                ThemeSlide = theme.ThemeSlide,
                ArrangementInstanceKey = NormalizeInstanceKey(slideDto.Id, instanceKey),
                Intent = RenderIntent.AudienceOutput,
                DependencyStamps = BuildDependencyStamps(resolvedDocument),
            });

        var cue = new PreparedSlideCue
        {
            Presentation = resolvedDocument,
            PresentationPath = resolvedPath,
            SlideId = slideDto.Id,
            InstanceKey = NormalizeInstanceKey(slideDto.Id, instanceKey),
            SlideIndex = slideIndex,
            SlideDocument = slideDto,
            Slide = typedSlide,
            Scene = scene?.Scene,
            DependencyDiagnostics = scene?.Scene.Dependencies
                .Where(static dependency => !dependency.IsResolved || dependency.FailureKind != null || !string.IsNullOrWhiteSpace(dependency.Message))
                .ToArray() ?? Array.Empty<SceneDependency>(),
            MediaLayers = SlideMediaLayerBuilder.Build(slideDto),
        };

        _preparedSlides[key] = cue;
        await Task.CompletedTask.ConfigureAwait(false);
        return cue;
    }

    /// <inheritdoc />
    public PreparedSlideCue? GetPreparedSlideCue(string? presentationPath, string slideId, string? instanceKey = null)
    {
        if (string.IsNullOrWhiteSpace(slideId))
            return null;

        if (string.IsNullOrWhiteSpace(presentationPath))
        {
            var matched = _preparedSlides
                .FirstOrDefault(entry =>
                    string.Equals(entry.Value.SlideId, slideId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Value.InstanceKey ?? entry.Value.SlideId, NormalizeInstanceKey(slideId, instanceKey), StringComparison.OrdinalIgnoreCase));
            if (matched.Equals(default(KeyValuePair<string, PreparedSlideCue>)))
                return null;

            if (!IsPreparedCueCurrent(matched.Value, null, slideId, instanceKey))
            {
                _preparedSlides.Remove(matched.Key);
                return null;
            }

            return matched.Value;
        }

        var resolvedDocument = _sessionCache.TryGet(presentationPath);
        var resolvedPath = string.IsNullOrWhiteSpace(resolvedDocument?.SourcePath)
            ? presentationPath
            : resolvedDocument.SourcePath;
        return GetFreshCachedCue(BuildSlideKey(resolvedPath, slideId, instanceKey), resolvedDocument, slideId, instanceKey);
    }

    /// <inheritdoc />
    public void InvalidatePresentationCues(string? presentationPath)
    {
        if (string.IsNullOrWhiteSpace(presentationPath))
            return;

        var normalizedPath = NormalizePresentationPath(_sessionCache.TryGet(presentationPath)?.SourcePath ?? presentationPath);
        var prefix = string.Concat(normalizedPath, "::");
        var keys = _preparedSlides.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keys)
            _preparedSlides.Remove(key);
    }

    /// <inheritdoc />
    public void HandleContentChanged(ContentChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);

        switch (change.Kind)
        {
            case ContentChangeKind.PresentationDeleted:
            case ContentChangeKind.PresentationReplaced:
            case ContentChangeKind.PresentationRenamed:
            case ContentChangeKind.PresentationUpdated:
                InvalidatePresentationCues(change.PreviousSubjectId);
                InvalidatePresentationCues(change.SubjectId);
                break;
            case ContentChangeKind.PackageImportCompleted:
            case ContentChangeKind.RepairCompleted:
                _preparedSlides.Clear();
                break;
        }
    }

    private PreparedSlideCue? GetFreshCachedCue(
        string key,
        PresentationDocument? resolvedDocument,
        string slideId,
        string? instanceKey)
    {
        if (!_preparedSlides.TryGetValue(key, out var cached))
            return null;

        if (IsPreparedCueCurrent(cached, resolvedDocument, slideId, instanceKey))
            return cached;

        _preparedSlides.Remove(key);
        return null;
    }

    private static bool IsPreparedCueCurrent(
        PreparedSlideCue cue,
        PresentationDocument? resolvedDocument,
        string slideId,
        string? instanceKey)
    {
        ArgumentNullException.ThrowIfNull(cue);

        if (!string.Equals(cue.SlideId, slideId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(cue.InstanceKey ?? cue.SlideId, NormalizeInstanceKey(slideId, instanceKey), StringComparison.OrdinalIgnoreCase))
            return false;

        var document = resolvedDocument ?? cue.Presentation;
        if (document == null)
            return false;

        if (resolvedDocument != null && !ReferenceEquals(cue.Presentation, resolvedDocument))
            return false;

        if (cue.SlideIndex < 0 || cue.SlideIndex >= document.Slides.Count)
            return false;

        return string.Equals(document.Slides[cue.SlideIndex].Id, slideId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePresentationPath(string? presentationPath)
    {
        if (string.IsNullOrWhiteSpace(presentationPath))
            return string.Empty;

        return presentationPath.Trim().Replace('\\', '/');
    }

    private static string NormalizeInstanceKey(string slideId, string? instanceKey) =>
        string.IsNullOrWhiteSpace(instanceKey) ? slideId.Trim() : instanceKey.Trim();

    private static string BuildSlideKey(string? presentationPath, string slideId, string? instanceKey)
    {
        var normalizedPath = NormalizePresentationPath(presentationPath);
        var normalizedInstance = NormalizeInstanceKey(slideId, instanceKey);
        return string.Concat(normalizedPath, "::", slideId.Trim(), "::", normalizedInstance);
    }

    private IReadOnlyDictionary<string, ContentResourceStamp> BuildDependencyStamps(PresentationDocument document)
    {
        if (_contentStore == null || string.IsNullOrWhiteSpace(document.Project?.SourcePath))
            return new Dictionary<string, ContentResourceStamp>(StringComparer.OrdinalIgnoreCase);

        var stamp = _contentStore.GetStamp(document.Project.SourcePath);
        if (!stamp.Succeeded || stamp.Value == null)
            return new Dictionary<string, ContentResourceStamp>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, ContentResourceStamp>(StringComparer.OrdinalIgnoreCase)
        {
            [document.Project.SourcePath] = stamp.Value,
        };
    }

    /// <inheritdoc />
    public PreparedMediaCue? PrepareMediaCue(MediaLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            return null;

        var mediaType = MediaInference.ResolveEffectiveMediaType(item.Type, resolvedPath);
        var media = new OutputLayerMedia
        {
            MediaId = resolvedPath,
            MediaType = mediaType,
            DisplayName = MediaCueDisplayNameResolver.Normalize(item.Name),
            Fit = item.CueDefaults.Fit,
            Loop = item.CueDefaults.Loop,
            Muted = item.CueDefaults.Muted,
            Autoplay = item.CueDefaults.Autoplay,
            Transition = CloneTransition(item.CueDefaults.Transition),
            ResolvedSourcePath = resolvedPath,
        };

        return new PreparedMediaCue
        {
            Target = SlideMediaLayerBuilder.MapCueTarget(item.CueDefaults.Target),
            Media = media,
        };
    }

    private PresentationDocument? ResolvePresentationDocument(string? presentationPath, PresentationDocument? fallbackDocument)
    {
        if (!string.IsNullOrWhiteSpace(presentationPath))
        {
            var cached = _sessionCache.TryGet(presentationPath) ?? _sessionCache.GetOrLoad(presentationPath);
            if (cached != null)
                return cached;

            return _presentationDocs.Open(presentationPath);
        }

        return fallbackDocument;
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