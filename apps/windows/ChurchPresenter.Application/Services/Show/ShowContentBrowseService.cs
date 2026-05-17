using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
public sealed class ShowContentBrowseService(
    ICatalogService catalog,
    IWorkspaceService workspace,
    IShowSessionCache sessionCache,
    IPresentationDocumentService presentationDocuments,
    ICuePreparationService cuePreparation,
    IPlaybackEngine playback,
    ISlideActionExecutionService slideActions,
    ILogger<ShowContentBrowseService> logger,
    ILiveProductionFacade? liveProduction = null) : IShowContentBrowseService
{
    private const string LibraryPrefix = "library:";
    private const string PlaylistPrefix = "playlist:";

    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IWorkspaceService _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly IShowSessionCache _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
    private readonly IPresentationDocumentService _presentationDocuments = presentationDocuments ?? throw new ArgumentNullException(nameof(presentationDocuments));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly IPlaybackEngine _playback = playback ?? throw new ArgumentNullException(nameof(playback));
    private readonly ISlideActionExecutionService _slideActions = slideActions ?? throw new ArgumentNullException(nameof(slideActions));
    private readonly ILogger<ShowContentBrowseService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILiveProductionFacade? _liveProduction = liveProduction;

    private bool _initialized;

    /// <inheritdoc />
    public async Task<ShowContentBrowseSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_initialized)
        {
            await _workspace.LoadAsync().ConfigureAwait(false);
            await _catalog.LoadAsync(ContentMaintenanceTrigger.Startup).ConfigureAwait(false);
            _sessionCache.PruneMissingFiles();
            _initialized = true;
        }

        return await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ShowContentBrowseSnapshot> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(query))
            return await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);

        string normalizedQuery = query.Trim();
        IReadOnlyList<PresentationRefDto> candidates = GetAllCatalogPresentations();
        List<PresentationRefDto> matchedPresentations = new();
        PresentationDocument? selectedDocument = null;
        string? selectedPath = null;

        foreach (PresentationRefDto candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentationDocument? document = ResolveDocument(candidate.Path);
            if (!PresentationMatches(candidate, document, normalizedQuery))
                continue;

            matchedPresentations.Add(candidate);
            if (selectedDocument == null)
            {
                selectedDocument = document;
                selectedPath = document?.SourcePath ?? candidate.Path;
            }
        }

        return new ShowContentBrowseSnapshot
        {
            Sources = BuildSources(null),
            Presentations = BuildPresentations(matchedPresentations, selectedPath),
            Slides = BuildSlides(selectedDocument, selectedPath, null, null),
            SelectedPresentationPath = selectedDocument?.SourcePath ?? selectedPath,
            SelectedPresentationTitle = selectedDocument?.Manifest.Title
                ?? matchedPresentations.FirstOrDefault(item => PathsMatch(item.Path, selectedPath))?.Title
                ?? string.Empty,
            StatusMessage = matchedPresentations.Count == 0
                ? $"No content matched \"{normalizedQuery}\"."
                : $"Search found {matchedPresentations.Count} presentation(s) matching \"{normalizedQuery}\".",
        };
    }

    /// <inheritdoc />
    public async Task<ShowContentBrowseSnapshot> OpenPresentationAsync(
        string presentationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        PresentationDocument? document = ResolveDocument(presentationPath);
        if (document == null)
        {
            return new ShowContentBrowseSnapshot
            {
                Sources = BuildSources(null),
                StatusMessage = $"Could not open {presentationPath}.",
            };
        }

        _workspace.Update(state =>
        {
            state.ActivePage = "show";
            state.SelectedPresentationPath = document.SourcePath;
        });
        await _workspace.SaveAsync().ConfigureAwait(false);

        PresentationRefDto reference = new()
        {
            Path = document.SourcePath,
            Title = document.Manifest.Title,
        };

        return new ShowContentBrowseSnapshot
        {
            Sources = BuildSources(null),
            Presentations = BuildPresentations([reference], document.SourcePath),
            Slides = BuildSlides(document, document.SourcePath, null, null),
            SelectedPresentationPath = document.SourcePath,
            SelectedPresentationTitle = document.Manifest.Title,
            StatusMessage = $"Opened {document.Manifest.Title} from file activation.",
        };
    }

    /// <inheritdoc />
    public async Task<ShowContentBrowseSnapshot> SelectSourceAsync(
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        CatalogSelection selection = ResolveSelection(sourceKey)
            ?? ResolveDefaultSelection()
            ?? CatalogSelection.Empty;

        _workspace.Update(state =>
        {
            state.ActivePage = "show";
            state.SelectedLibraryId = selection.Kind == CatalogSourceKind.Library ? selection.Id : null;
            state.SelectedPlaylistId = selection.Kind == CatalogSourceKind.Playlist ? selection.Id : null;
            state.SelectedPresentationPath = selection.Presentations.FirstOrDefault()?.Path;
        });
        await _workspace.SaveAsync().ConfigureAwait(false);

        return await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ShowContentBrowseSnapshot> SelectPresentationAsync(
        string presentationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        _workspace.Update(state =>
        {
            state.ActivePage = "show";
            state.SelectedPresentationPath = presentationPath;
        });
        await _workspace.SaveAsync().ConfigureAwait(false);

        return await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TakeSlideLiveAsync(
        string presentationPath,
        string slideId,
        string? instanceKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        PresentationDocument? document = ResolveDocument(presentationPath);
        if (document == null)
        {
            _logger.LogWarning("Cannot take slide {SlideId} live because presentation {Path} could not be opened.", slideId, presentationPath);
            return false;
        }

        PreparedSlideCue? preparedCue = _cuePreparation.GetPreparedSlideCue(presentationPath, slideId, instanceKey)
            ?? await _cuePreparation.PrepareSlideCueAsync(presentationPath, slideId, instanceKey, document, cancellationToken)
                .ConfigureAwait(false);
        if (preparedCue == null)
        {
            _logger.LogWarning("Cannot take slide {SlideId} live because no prepared cue was available.", slideId);
            return false;
        }

        _playback.EnterPreparedSlideCue(preparedCue);
        _liveProduction?.ReleaseClearedLayers([preparedCue.LayerKind]);
        _slideActions.ExecuteForSlide(preparedCue.Slide);
        _sessionCache.SchedulePrefetch(preparedCue.PresentationPath);

        _workspace.Update(state =>
        {
            state.ActivePage = "show";
            state.SelectedPresentationPath = preparedCue.PresentationPath;
        });
        await _workspace.SaveAsync().ConfigureAwait(false);

        return true;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ShowContentBrowseSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CatalogSelection selection = ResolveWorkspaceSelection()
            ?? ResolveDefaultSelection()
            ?? CatalogSelection.Empty;

        string? selectedPath = ResolveSelectedPresentationPath(selection);
        if (selection.Kind != CatalogSourceKind.None)
            _sessionCache.SetSessionOrder(selection.Presentations);

        PresentationDocument? selectedDocument = string.IsNullOrWhiteSpace(selectedPath)
            ? null
            : ResolveDocument(selectedPath);

        string? selectedSourceKey = selection.Kind == CatalogSourceKind.None
            ? null
            : BuildSourceKey(selection.Kind, selection.Id);

        var snapshot = new ShowContentBrowseSnapshot
        {
            Sources = BuildSources(selectedSourceKey),
            Presentations = BuildPresentations(selection.Presentations, selectedPath),
            Slides = BuildSlides(selectedDocument, selectedPath, null, null),
            SelectedSourceKey = selectedSourceKey,
            SelectedPresentationPath = selectedDocument?.SourcePath ?? selectedPath,
            SelectedPresentationTitle = selectedDocument?.Manifest.Title
                ?? selection.Presentations.FirstOrDefault(item => PathsMatch(item.Path, selectedPath))?.Title
                ?? string.Empty,
            StatusMessage = BuildStatus(selection, selectedDocument),
        };

        await Task.CompletedTask.ConfigureAwait(false);
        return snapshot;
    }

    private PresentationDocument? ResolveDocument(string presentationPath)
    {
        try
        {
            PresentationDocument? document = _sessionCache.GetOrLoad(presentationPath);
            if (document != null)
                return document;

            document = _presentationDocuments.Open(presentationPath);
            _sessionCache.UpdateEntry(document.SourcePath, document);
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _logger.LogWarning(ex, "Could not open presentation {Path}.", presentationPath);
            return null;
        }
    }

    private CatalogSelection? ResolveWorkspaceSelection()
    {
        WorkspaceDto state = _workspace.Workspace;
        if (!string.IsNullOrWhiteSpace(state.SelectedPlaylistId))
        {
            PlaylistDto? playlist = _catalog.Catalog.Playlists.FirstOrDefault(item =>
                string.Equals(item.Id, state.SelectedPlaylistId, StringComparison.OrdinalIgnoreCase));
            if (playlist != null)
                return CatalogSelection.ForPlaylist(playlist);
        }

        if (!string.IsNullOrWhiteSpace(state.SelectedLibraryId))
        {
            LibraryDto? library = _catalog.Catalog.Libraries.FirstOrDefault(item =>
                string.Equals(item.Id, state.SelectedLibraryId, StringComparison.OrdinalIgnoreCase));
            if (library != null)
                return CatalogSelection.ForLibrary(library);
        }

        return null;
    }

    private CatalogSelection? ResolveSelection(string sourceKey)
    {
        if (sourceKey.StartsWith(LibraryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string id = sourceKey[LibraryPrefix.Length..];
            LibraryDto? library = _catalog.Catalog.Libraries.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            return library == null ? null : CatalogSelection.ForLibrary(library);
        }

        if (sourceKey.StartsWith(PlaylistPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string id = sourceKey[PlaylistPrefix.Length..];
            PlaylistDto? playlist = _catalog.Catalog.Playlists.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            return playlist == null ? null : CatalogSelection.ForPlaylist(playlist);
        }

        return null;
    }

    private CatalogSelection? ResolveDefaultSelection()
    {
        PlaylistDto? playlist = _catalog.Catalog.Playlists.FirstOrDefault(item => item.Items.Count > 0)
            ?? _catalog.Catalog.Playlists.FirstOrDefault();
        if (playlist != null)
            return CatalogSelection.ForPlaylist(playlist);

        LibraryDto? library = _catalog.Catalog.Libraries.FirstOrDefault(item => item.Presentations.Count > 0)
            ?? _catalog.Catalog.Libraries.FirstOrDefault();
        return library == null ? null : CatalogSelection.ForLibrary(library);
    }

    private string? ResolveSelectedPresentationPath(CatalogSelection selection)
    {
        if (selection.Presentations.Count == 0)
            return null;

        string? workspacePath = _workspace.Workspace.SelectedPresentationPath;
        if (!string.IsNullOrWhiteSpace(workspacePath)
            && selection.Presentations.Any(item => PathsMatch(item.Path, workspacePath)))
        {
            return workspacePath;
        }

        return selection.Presentations[0].Path;
    }

    private IReadOnlyList<ShowCatalogSource> BuildSources(string? selectedSourceKey)
    {
        return
        [
            .. _catalog.Catalog.Playlists.Select(playlist => new ShowCatalogSource
            {
                Key = BuildSourceKey(CatalogSourceKind.Playlist, playlist.Id),
                Name = string.IsNullOrWhiteSpace(playlist.Name) ? "Untitled playlist" : playlist.Name,
                Kind = "Playlist",
                PresentationCount = playlist.Items.Count,
                IsSelected = string.Equals(BuildSourceKey(CatalogSourceKind.Playlist, playlist.Id), selectedSourceKey, StringComparison.OrdinalIgnoreCase),
            }),
            .. _catalog.Catalog.Libraries.Select(library => new ShowCatalogSource
            {
                Key = BuildSourceKey(CatalogSourceKind.Library, library.Id),
                Name = string.IsNullOrWhiteSpace(library.Name) ? "Untitled library" : library.Name,
                Kind = "Library",
                PresentationCount = library.Presentations.Count,
                IsSelected = string.Equals(BuildSourceKey(CatalogSourceKind.Library, library.Id), selectedSourceKey, StringComparison.OrdinalIgnoreCase),
            }),
        ];
    }

    private IReadOnlyList<PresentationRefDto> GetAllCatalogPresentations()
    {
        Dictionary<string, PresentationRefDto> presentations = new(StringComparer.OrdinalIgnoreCase);
        foreach (PresentationRefDto item in _catalog.Catalog.Playlists.SelectMany(static playlist => playlist.Items))
            AddPresentation(presentations, item);

        foreach (PresentationRefDto item in _catalog.Catalog.Libraries.SelectMany(static library => library.Presentations))
            AddPresentation(presentations, item);

        return presentations.Values
            .OrderBy(static item => string.IsNullOrWhiteSpace(item.Title) ? Path.GetFileNameWithoutExtension(item.Path) : item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddPresentation(Dictionary<string, PresentationRefDto> presentations, PresentationRefDto item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
            return;

        presentations.TryAdd(item.Path.Trim().Replace('\\', '/'), item);
    }

    private static bool PresentationMatches(PresentationRefDto reference, PresentationDocument? document, string query)
    {
        if (Contains(reference.Title, query) || Contains(reference.Path, query))
            return true;

        if (document == null)
            return false;

        if (Contains(document.Manifest.Title, query))
            return true;

        return document.Project?.Slides.Any(slide => SlideMatches(slide, query)) == true
            || document.Slides.Any(slide => SlideMatches(slide, query));
    }

    private static bool SlideMatches(PresentationSlide slide, string query)
    {
        return Contains(slide.Section, query)
            || Contains(slide.SectionLabel, query)
            || Contains(slide.Notes, query)
            || slide.Layers.OfType<TextLayer>().Any(layer => Contains(layer.Content, query));
    }

    private static bool SlideMatches(SlideDto slide, string query)
    {
        return Contains(slide.Section, query)
            || Contains(slide.SectionLabel, query)
            || Contains(slide.Notes, query);
    }

    private static IReadOnlyList<ShowPresentationBrowseItem> BuildPresentations(
        IReadOnlyList<PresentationRefDto> presentations,
        string? selectedPath)
    {
        return presentations.Select(item => new ShowPresentationBrowseItem
        {
            Path = item.Path,
                Title = string.IsNullOrWhiteSpace(item.Title) ? Path.GetFileNameWithoutExtension(item.Path) : item.Title,
                Detail = item.Path,
            IsSelected = PathsMatch(item.Path, selectedPath),
        }).ToArray();
    }

    private static IReadOnlyList<ShowSlideBrowseItem> BuildSlides(
        PresentationDocument? document,
        string? selectedPath,
        string? livePresentationPath,
        string? liveSlideId)
    {
        if (document == null)
            return Array.Empty<ShowSlideBrowseItem>();

        string path = string.IsNullOrWhiteSpace(document.SourcePath) ? selectedPath ?? string.Empty : document.SourcePath;
        return document.Slides.Select((slide, index) =>
        {
            PresentationSlide? typedSlide = document.Project?.Slides.ElementAtOrDefault(index);
            string label = FirstNonWhiteSpace(typedSlide?.SectionLabel, typedSlide?.Section, slide.SectionLabel, slide.Section);
            string footerLabel = BuildFooterLabel(typedSlide, slide);
            return new ShowSlideBrowseItem
            {
                PresentationPath = path,
                SlideId = slide.Id,
                Ordinal = index + 1,
                Title = string.IsNullOrWhiteSpace(label) ? $"Slide {index + 1}" : $"{index + 1}. {label}",
                FooterLabel = footerLabel,
                RawText = BuildRawText(typedSlide),
                SectionKey = FirstNonWhiteSpace(typedSlide?.Section, slide.Section, footerLabel),
                HasTransitionOverride = !string.IsNullOrWhiteSpace(typedSlide?.Animations?.Transition?.Type),
                TransitionLabel = MediaCueTransitionFormatter.FormatLabel(typedSlide?.Animations?.Transition),
                IsLive = PathsMatch(path, livePresentationPath) && string.Equals(slide.Id, liveSlideId, StringComparison.OrdinalIgnoreCase),
                Disabled = typedSlide?.Disabled ?? false,
            };
        }).ToArray();
    }

    private static string BuildFooterLabel(PresentationSlide? typedSlide, SlideDto slide)
    {
        if (!string.IsNullOrWhiteSpace(typedSlide?.SectionLabel))
            return typedSlide.SectionLabel.Trim();
        if (!string.IsNullOrWhiteSpace(slide.SectionLabel))
            return slide.SectionLabel.Trim();
        if (!string.IsNullOrWhiteSpace(typedSlide?.Section))
            return PresentationModelUtilities.FormatSectionLabel(typedSlide.Section, typedSlide.SectionIndex);
        if (!string.IsNullOrWhiteSpace(slide.Section))
            return slide.Section.Trim();
        return "Slide";
    }

    private static string BuildRawText(PresentationSlide? slide)
    {
        if (slide == null)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            slide.Layers
                .OfType<TextLayer>()
                .Where(static layer => layer.Visible && !string.IsNullOrWhiteSpace(layer.Content))
                .Select(static layer => layer.Content.Trim()));
    }

    private static string BuildStatus(CatalogSelection selection, PresentationDocument? document)
    {
        if (selection.Kind == CatalogSourceKind.None)
            return "No libraries or playlists are available yet.";
        if (selection.Presentations.Count == 0)
            return $"{selection.Name} has no presentations.";
        if (document == null)
            return "Select a presentation to load slides.";

        return $"Loaded {document.Slides.Count} slide(s) from {document.Manifest.Title}.";
    }

    private static string BuildSourceKey(CatalogSourceKind kind, string id)
    {
        string prefix = kind == CatalogSourceKind.Library ? LibraryPrefix : PlaylistPrefix;
        return string.Concat(prefix, id);
    }

    private static bool PathsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            left.Trim().Replace('\\', '/'),
            right.Trim().Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private enum CatalogSourceKind
    {
        None,
        Library,
        Playlist,
    }

    private sealed record CatalogSelection(
        CatalogSourceKind Kind,
        string Id,
        string Name,
        IReadOnlyList<PresentationRefDto> Presentations)
    {
        public static CatalogSelection Empty { get; } = new(
            CatalogSourceKind.None,
            string.Empty,
            string.Empty,
            Array.Empty<PresentationRefDto>());

        public static CatalogSelection ForLibrary(LibraryDto library) =>
            new(CatalogSourceKind.Library, library.Id, library.Name, library.Presentations);

        public static CatalogSelection ForPlaylist(PlaylistDto playlist) =>
            new(CatalogSourceKind.Playlist, playlist.Id, playlist.Name, playlist.Items);
    }
}