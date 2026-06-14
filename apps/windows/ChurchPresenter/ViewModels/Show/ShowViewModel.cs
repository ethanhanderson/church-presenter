using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Hosting;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Show;

using ChurchPresenter.Services.Pipeline;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;

using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// Show mode state for libraries, playlists, slide browsing, live output, and operator preview.
/// </summary>
public partial class ShowViewModel : ObservableObject
{
    private const int MaxVisibleDeckCueWarmups = 8;

    private readonly IContentDirectoryService _content;
    private readonly ISettingsService _settings;
    private readonly ICatalogService _catalog;
    private readonly IPresentationDocumentService _presentationDocs;
    private readonly IPresentationProjectService _projects;
    private readonly IWorkspaceService _workspace;
    private readonly ICueDispatcher _cueDispatcher;
    private readonly ILiveSessionQueryService _liveSessionQuery;
    private readonly IOperatorSelectionService _operatorSelection;
    private readonly IOutputTargetService _outputTargets;
    private readonly IRapidSeekCoordinator _rapidSeek;
    private readonly CuePipelineRuntime _pipeline;
    private readonly IMediaTransportService _mediaTransport;
    private readonly IOutputRoutingService _outputRouting;
    private readonly IAppActivationService _activation;
    private readonly IOutputWindowService _outputWindows;
    private readonly OutputViewModel _outputViewModel;
    private readonly IActivePresentationService _activePresentation;
    private readonly ISlideActionExecutionService _slideActions;
    private readonly ISlideItemActionService _slideItemActions;
    private readonly IMonitorService _monitors;
    private readonly IMediaLibraryService _mediaLibrary;
    private readonly IMediaStartupWarmupService _mediaWarmup;
    private readonly IMediaThumbnailCache _mediaThumbnailCache;
    private readonly MediaTransportPresenter _mediaTransportPresenter;
    private readonly ICuePreparationService _cuePreparation;
    private readonly IShowTransitionDefaults _transitionDefaults;
    private readonly IShowControlsService _showControls;
    private readonly IShowTimerService _showTimers;
    private readonly IShowSessionCache _sessionCache;
    private readonly IContentStartupMaintenanceService _contentStartupMaintenance;
    private readonly ILogger<ShowViewModel> _logger;
    private CancellationTokenSource? _sessionLoadCts;

    private bool _initialized;
    private Task? _initializeTask;

    // ── Media panel state ────────────────────────────────────────────────────

    /// <summary>Whether the bottom media browser panel is currently open.</summary>
    [ObservableProperty]
    private bool _mediaPanelOpen;

    /// <summary>Panel height in device-independent pixels when open; persisted across open/close cycles.</summary>
    [ObservableProperty]
    private double _mediaPanelHeight = 308;

    /// <summary>Width of the Show output preview column in device-independent pixels (clamped in the view).</summary>
    [ObservableProperty]
    private double _outputPanelWidth = WorkspaceDto.ShowOutputPanelDefaultWidthDpi;

    /// <summary>Layout mode for the media panel content area: <c>grid</c> or <c>list</c>.</summary>
    [ObservableProperty]
    private string _mediaPanelLayoutMode = "grid";

    /// <summary>Id of the currently selected media playlist in the panel sidebar, or <c>null</c> for all.</summary>
    [ObservableProperty]
    private string? _mediaPanelSelectedPlaylistId;

    /// <summary>Search/filter text applied to the media panel item list.</summary>
    [ObservableProperty]
    private string _mediaPanelSearchText = "";

    // ── Media library observable collections ─────────────────────────────────

    /// <summary>Media playlists loaded for the panel sidebar.</summary>
    public ObservableCollection<MediaPlaylistManifest> MediaPlaylists { get; } = new();

    /// <summary>Items visible in the media panel content area (filtered by playlist + search).</summary>
    public ObservableCollection<MediaPanelItemViewModel> MediaPanelItems { get; } = new();
    private readonly List<MediaLibraryItem> _mediaRootItems = new();

    // ── Deck toolbar persisted state ─────────────────────────────────────────
    [ObservableProperty]
    private string _deckViewMode = "thumbnail";

    [ObservableProperty]
    private bool _groupBySection;

    /// <summary>When true, thumbnails use <see cref="TransparentThumbnailColor"/> and opacity; when false, checkerboard for transparent slide backgrounds.</summary>
    [ObservableProperty]
    private bool _transparentThumbnailBackgroundEnabled;

    [ObservableProperty]
    private string _transparentThumbnailColor = "#000000";

    /// <summary>Opacity of the thumbnail background, 0–100.</summary>
    [ObservableProperty]
    private int _transparentThumbnailOpacity = 100;

    /// <summary>0–4 scale step for deck card size; 2 is the default medium size.</summary>
    [ObservableProperty]
    private int _deckScaleStep = 2;

    /// <summary>0–7 scale step for media drawer grid cards (smaller than slide deck at each index).</summary>
    [ObservableProperty]
    private int _mediaPanelScaleStep = 4;

    /// <summary>Number of seconds the output-panel media seek buttons move backward or forward.</summary>
    [ObservableProperty]
    private int _mediaSeekSeconds = 5;

    /// <summary>How inactive layer clear buttons look while still using accent button chrome.</summary>
    [ObservableProperty]
    private OutputClearInactiveAppearance _outputClearInactiveAppearance = OutputClearInactiveAppearance.DisabledAccent;

    /// <summary>When true, the section-group strip card is visible below the presentation header (single-deck mode).</summary>
    [ObservableProperty]
    private bool _arrangementSectionExpanded;

    private bool _loadingDeckPreferences;
    private bool _deckPreferencesHydrated;
    private CancellationTokenSource? _deckPrefDebounceCts;

    private static readonly double[] DeckMinItemWidths = { 160, 190, 220, 260, 300 };
    private static readonly double[] DeckListItemHeights = { 50, 60, 70, 90, 110 };

    /// <summary>Minimum grid cell widths for media drawer cards (8 steps; smaller than slide deck min widths).</summary>
    private static readonly double[] MediaPanelGridMinItemWidths =
        { 116, 132, 150, 168, 186, 204, 220, 236 };

    private const int MediaPanelScaleStepMax = 7;

    private const double MediaPanelGridTitleBarHeight = 37;

    /// <summary>Inset between the media grid card outline and its thumbnail frame (matches slide deck preview inset).</summary>
    public const double MediaPanelGridCardInset = 4;

    private string? _selectedLibraryId;
    private string? _selectedPlaylistId;
    private string? _selectedPresentationPath;
    private PresentationDocument? _openDocument;
    private string? _selectedSlideId;
    private string? _selectedSlidePresentationPath;
    private string? _selectedSlideInstanceKey;
    private readonly List<SlideDeckSelectionKey> _selectedSlideKeys = new();
    private SlideDeckSelectionKey? _selectionAnchor;

    // ── Arrangement + auto-advance runtime state ─────────────────────────────
    private PlaybackSequence _playbackSequence = PlaybackSequence.Empty;
    /// <summary>Re-entrancy depth for arrangement-state notifications; &gt;0 suppresses UI-driven callbacks.</summary>
    private int _arrangementUpdateDepth;
    private readonly ObservableCollection<SectionGroupChipDisplay> _activeArrangementGroupChips = new();
    private readonly Dictionary<string, ShowPresentationHeaderState> _browseStackHeaderStates = new(StringComparer.OrdinalIgnoreCase);
    private System.Timers.Timer? _autoAdvanceTimer;
    private CancellationTokenSource? _autoAdvanceCts;

    /// <summary>True after the operator explicitly selected a slide card instead of only taking slides live.</summary>
    private bool _userOverrideSlideSelection;

    private string _statusMessage = "";
    private const int DefaultSelectionTransitionDurationMs = 400;
    private const int SelectionSeekTransitionBufferMs = 25;

    public ShowViewModel(
        IContentDirectoryService content,
        ISettingsService settings,
        ICatalogService catalog,
        IPresentationDocumentService presentationDocs,
        IPresentationProjectService projects,
        IWorkspaceService workspace,
        ICueDispatcher cueDispatcher,
        ILiveSessionQueryService liveSessionQuery,
        IOperatorSelectionService operatorSelection,
        IOutputTargetService outputTargets,
        IRapidSeekCoordinator rapidSeek,
        IMediaTransportService mediaTransport,
        CuePipelineRuntime pipelineRuntime,
        IOutputRoutingService outputRouting,
        IAppActivationService activation,
        IOutputWindowService outputWindows,
        OutputViewModel outputViewModel,
        IActivePresentationService activePresentation,
        ISlideActionExecutionService slideActions,
        ISlideItemActionService slideItemActions,
        IMonitorService monitors,
        IMediaLibraryService mediaLibrary,
        IMediaStartupWarmupService mediaWarmup,
        IMediaThumbnailCache mediaThumbnailCache,
        IShowSessionCache sessionCache,
        ICuePreparationService cuePreparation,
        IShowTransitionDefaults transitionDefaults,
        IShowControlsService showControls,
        IShowTimerService showTimers,
        IContentStartupMaintenanceService contentStartupMaintenance,
        ILogger<ShowViewModel> logger)
    {
        _content = content;
        _settings = settings;
        _catalog = catalog;
        _presentationDocs = presentationDocs;
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _workspace = workspace;
        _cueDispatcher = cueDispatcher ?? throw new ArgumentNullException(nameof(cueDispatcher));
        _liveSessionQuery = liveSessionQuery ?? throw new ArgumentNullException(nameof(liveSessionQuery));
        _operatorSelection = operatorSelection ?? throw new ArgumentNullException(nameof(operatorSelection));
        _outputTargets = outputTargets ?? throw new ArgumentNullException(nameof(outputTargets));
        _rapidSeek = rapidSeek ?? throw new ArgumentNullException(nameof(rapidSeek));
        _mediaTransport = mediaTransport ?? throw new ArgumentNullException(nameof(mediaTransport));
        _pipeline = pipelineRuntime ?? throw new ArgumentNullException(nameof(pipelineRuntime));
        _outputRouting = outputRouting ?? throw new ArgumentNullException(nameof(outputRouting));
        _activation = activation;
        _outputWindows = outputWindows;
        _outputViewModel = outputViewModel ?? throw new ArgumentNullException(nameof(outputViewModel));
        _activePresentation = activePresentation ?? throw new ArgumentNullException(nameof(activePresentation));
        _slideActions = slideActions ?? throw new ArgumentNullException(nameof(slideActions));
        _slideItemActions = slideItemActions ?? throw new ArgumentNullException(nameof(slideItemActions));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
        _mediaWarmup = mediaWarmup ?? throw new ArgumentNullException(nameof(mediaWarmup));
        _mediaThumbnailCache = mediaThumbnailCache ?? throw new ArgumentNullException(nameof(mediaThumbnailCache));
        _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
        _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
        _transitionDefaults = transitionDefaults ?? throw new ArgumentNullException(nameof(transitionDefaults));
        _showControls = showControls ?? throw new ArgumentNullException(nameof(showControls));
        _showTimers = showTimers ?? throw new ArgumentNullException(nameof(showTimers));
        _contentStartupMaintenance = contentStartupMaintenance ?? throw new ArgumentNullException(nameof(contentStartupMaintenance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mediaTransportPresenter = new MediaTransportPresenter(_mediaTransport, _pipeline);
        InitializeShowControlsCommands();
        Libraries = new ObservableCollection<LibraryDto>();
        Playlists = new ObservableCollection<PlaylistDto>();
        LibraryTreeItems = new ObservableCollection<ShowLibraryTreeItem>();
        PlaylistTreeItems = new ObservableCollection<ShowPlaylistTreeItem>();
        Slides = new BulkObservableCollection<PresentationSlide>();
        SlideDeckItems = new BulkObservableCollection<ShowSlideDeckItem>();
        BrowseStackSections = new ObservableCollection<ShowPresentationDeckSection>();
        _liveSessionQuery.Changed += LiveSessionQuery_Changed;
        _outputTargets.Changed += (_, _) => RunOnUiThread(OnLiveChanged, "output-targets-changed");
        _operatorSelection.Changed += (_, _) => RunOnUiThread(OnLiveChanged, "operator-selection-changed");
        _pipeline.Changed += (_, _) => RunOnUiThread(OnLiveChanged, "pipeline-changed");
        _contentStartupMaintenance.Changed += ContentStartupMaintenance_Changed;
        RefreshOutputClearActions(_liveSessionQuery.Current);
    }

    private static void RunOnUiThread(Action action, string reason)
    {
        ArgumentNullException.ThrowIfNull(action);

        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher =
            App.MainWindow?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _ = reason;
        _ = dispatcher.TryEnqueue(() => action());
    }

    public ObservableCollection<LibraryDto> Libraries { get; }

    public ObservableCollection<PlaylistDto> Playlists { get; }

    /// <summary>Sidebar tree: libraries with nested presentations.</summary>
    public ObservableCollection<ShowLibraryTreeItem> LibraryTreeItems { get; }

    /// <summary>Sidebar tree: playlists with nested presentations.</summary>
    public ObservableCollection<ShowPlaylistTreeItem> PlaylistTreeItems { get; }

    public BulkObservableCollection<PresentationSlide> Slides { get; }

    /// <summary>Show slide grid rows (slide + ordinal + footer chrome).</summary>
    public BulkObservableCollection<ShowSlideDeckItem> SlideDeckItems { get; }

    /// <summary>
    /// When a playlist is selected, all presentations in order. When a library is selected, only the
    /// <see cref="SelectedPresentationPath"/> presentation (if it belongs to that library).
    /// </summary>
    public ObservableCollection<ShowPresentationDeckSection> BrowseStackSections { get; }

    /// <summary>Primary output layer clear actions shown under the program preview.</summary>
    public ObservableCollection<ShowClearActionViewModel> PrimaryOutputClearActions { get; } = new();

    /// <summary>Secondary custom clear-group actions shown directly below the program preview.</summary>
    public ObservableCollection<ShowClearActionViewModel> SecondaryOutputClearActions { get; } = new();

    /// <summary>Legacy aggregate output clear actions kept for older bindings.</summary>
    public ObservableCollection<ShowClearActionViewModel> OutputClearActions { get; } = new();

    /// <summary>
    /// Section-grouped slide deck for single-presentation view when <see cref="GroupBySection"/> is true.
    /// Groups slides by consecutive <see cref="PresentationSlide.Section"/> runs within the open presentation.
    /// </summary>
    public ObservableCollection<ShowPresentationDeckSection> SlideDeckGroupedSections { get; } = new();

    private readonly record struct SlideDeckSelectionKey(
        string? PresentationPath,
        string SlideId,
        string InstanceKey);

    /// <summary>Raised after the browse stack is rebuilt so the view can scroll the selected presentation into view (playlist or single library selection).</summary>
    public event EventHandler? BrowseStackScrollToSelectionRequested;

    /// <summary>Slide deck row matching <see cref="SelectedSlideId"/> (for selection chrome and shortcuts).</summary>
    public ShowSlideDeckItem? SelectedDeckRowForView => FindPrimarySelectedDeckRow();

    public string? SelectedLibraryId
    {
        get => _selectedLibraryId;
        set
        {
            if (SetProperty(ref _selectedLibraryId, value))
            {
                NotifyCenterPanes();
                RefreshBrowseStackFromSelection();
            }
        }
    }

    public string? SelectedPlaylistId
    {
        get => _selectedPlaylistId;
        set
        {
            if (SetProperty(ref _selectedPlaylistId, value))
            {
                NotifyCenterPanes();
                RefreshBrowseStackFromSelection();
            }
        }
    }

    public string? SelectedPresentationPath
    {
        get => _selectedPresentationPath;
        set
        {
            if (SetProperty(ref _selectedPresentationPath, value))
            {
                ArrangementSectionExpanded = false;
                SyncActiveSectionData();
                OnPropertyChanged(nameof(ActivePresentationFilePath));
            }
        }
    }

    /// <summary>File path for the active single-deck presentation (open document or selected deck).</summary>
    public string? ActivePresentationFilePath => OpenDocument?.SourcePath ?? SelectedPresentationPath;

    public PresentationDocument? OpenDocument
    {
        get => _openDocument;
        set
        {
            if (SetProperty(ref _openDocument, value))
            {
                NotifyCenterPanes();
                OnPropertyChanged(nameof(OpenProject));
                OnPropertyChanged(nameof(ActivePresentationFilePath));
                OnPropertyChanged(nameof(HasPresentationDefaultSlideTransition));
                NotifyPreviewState();
            }
        }
    }

    public string? SelectedSlideId
    {
        get => _selectedSlideId;
        set => ApplySlideSelectionState(value, _selectedSlidePresentationPath);
    }

    public string? SelectedSlidePresentationPath => _selectedSlidePresentationPath;

    /// <summary>Stable deck-instance key for the current operator selection, used to disambiguate repeated arranged slides.</summary>
    public string? SelectedSlideInstanceKey => _selectedSlideInstanceKey;

    public bool UserOverrideSlideSelection
    {
        get => _userOverrideSlideSelection;
        set => SetProperty(ref _userOverrideSlideSelection, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private void ContentStartupMaintenance_Changed(object? sender, ContentStartupMaintenanceChangedEventArgs e)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            ApplyContentStartupMaintenanceSnapshot(e.Snapshot);
            return;
        }

        dispatcher.TryEnqueue(() => ApplyContentStartupMaintenanceSnapshot(e.Snapshot));
    }

    private void ApplyContentStartupMaintenanceSnapshot(ContentStartupMaintenanceSnapshot snapshot)
    {
        if (snapshot.Phase == ContentStartupMaintenancePhase.Failed)
        {
            StatusMessage = snapshot.StatusMessage;
            return;
        }

        if (snapshot.Phase != ContentStartupMaintenancePhase.Completed)
            return;

        RefreshCatalogCollections();
        CoerceSelectionToCatalog();
        EnsureDefaultSelection();
        NotifyCenterPanes();
        RefreshBrowseStackFromSelection();
        StatusMessage = snapshot.StatusMessage;
    }

    public bool HasOpenPresentation => OpenDocument != null;

    /// <summary>Whether the Show page has a loaded presentation that editor-style workspaces can safely use.</summary>
    public bool CanUsePresentationWorkspace =>
        OpenDocument?.Project != null && !string.IsNullOrWhiteSpace(OpenDocument.SourcePath);

    public PresentationProject? OpenProject => OpenDocument?.Project;

    public LibraryDto? SelectedLibrary =>
        string.IsNullOrEmpty(SelectedLibraryId)
            ? null
            : Libraries.FirstOrDefault(l => l.Id == SelectedLibraryId);

    public PlaylistDto? SelectedPlaylist =>
        string.IsNullOrEmpty(SelectedPlaylistId)
            ? null
            : Playlists.FirstOrDefault(p => p.Id == SelectedPlaylistId);

    /// <summary>Stacked browse (library or playlist selected).</summary>
    public bool ShowBrowseStack =>
        SelectedLibrary != null || SelectedPlaylist != null;

    /// <summary>Single-presentation grid when no library/playlist context (e.g. opened from file picker).</summary>
    public bool ShowSingleDeckSlideGrid =>
        OpenDocument != null && SelectedLibrary == null && SelectedPlaylist == null;

    public bool ShowHome =>
        OpenDocument == null && SelectedLibrary == null && SelectedPlaylist == null;

    public bool HasPresentationSources =>
        Libraries.Count > 0 || Playlists.Count > 0;

    public bool ShowPresentationSourcesEmpty =>
        ShowHome && !HasPresentationSources;

    public bool ShowPresentationHomePrompt =>
        ShowHome && HasPresentationSources;

    // ── Deck view-mode helpers (computed from DeckViewMode) ──────────────────

    public bool ShowThumbnailMode => DeckViewMode == "thumbnail";
    public bool ShowTextMode => DeckViewMode == "text";
    public bool ShowListMode => DeckViewMode == "list";

    /// <summary>Single deck in the standard slide layout (no lyric group headers).</summary>
    public bool ShowSingleDeckStandardLayout => ShowSingleDeckSlideGrid && !GroupBySection;

    /// <summary>Single deck in the lyric-group-header layout.</summary>
    public bool ShowSingleDeckLyricGroupHeaderLayout => ShowSingleDeckSlideGrid && GroupBySection;

    public bool ShowSingleDeckStandardThumbnail => ShowSingleDeckStandardLayout && ShowThumbnailMode;
    public bool ShowSingleDeckStandardText => ShowSingleDeckStandardLayout && ShowTextMode;
    public bool ShowSingleDeckStandardList => ShowSingleDeckStandardLayout && ShowListMode;

    public bool ShowSingleDeckLyricGroupHeaderThumbnail => ShowSingleDeckLyricGroupHeaderLayout && ShowThumbnailMode;
    public bool ShowSingleDeckLyricGroupHeaderText => ShowSingleDeckLyricGroupHeaderLayout && ShowTextMode;
    public bool ShowSingleDeckLyricGroupHeaderList => ShowSingleDeckLyricGroupHeaderLayout && ShowListMode;

    public bool ShowBrowseStackThumbnail => ShowBrowseStack && ShowThumbnailMode;
    public bool ShowBrowseStackText => ShowBrowseStack && ShowTextMode;
    public bool ShowBrowseStackList => ShowBrowseStack && ShowListMode;

    /// <summary>Minimum card width in pixels for the current scale step.</summary>
    public double DeckMinItemWidth => DeckMinItemWidths[Math.Clamp(DeckScaleStep, 0, 4)];

    /// <summary>Thumbnail height for list-view rows for the current scale step.</summary>
    public double DeckListItemHeight => DeckListItemHeights[Math.Clamp(DeckScaleStep, 0, 4)];

    /// <summary>Minimum width of each media grid cell for the current media scale step.</summary>
    public double MediaPanelGridMinItemWidth =>
        MediaPanelGridMinItemWidths[Math.Clamp(MediaPanelScaleStep, 0, MediaPanelScaleStepMax)];

    /// <summary>Grid-mode thumbnail width for the current media scale step.</summary>
    public double MediaPanelGridThumbnailWidth => MediaPanelGridMinItemWidth;

    /// <summary>Grid-mode thumbnail height for the current media scale step.</summary>
    public double MediaPanelGridThumbnailHeight =>
        Math.Round(MediaPanelGridThumbnailWidth * 9.0 / 16.0, 0);

    /// <summary>Width of the nested thumbnail frame inside a media grid card.</summary>
    public double MediaPanelGridThumbnailFrameWidth =>
        Math.Max(0, MediaPanelGridThumbnailWidth - (MediaPanelGridCardInset * 2));

    /// <summary>Height of the nested thumbnail frame inside a media grid card.</summary>
    public double MediaPanelGridThumbnailFrameHeight =>
        Math.Max(0, MediaPanelGridThumbnailHeight - MediaPanelGridCardInset);

    /// <summary>Minimum height of each media grid cell, driven by thumbnail height plus the title bar.</summary>
    public double MediaPanelGridMinItemHeight =>
        MediaPanelGridThumbnailHeight + MediaPanelGridTitleBarHeight;

    /// <summary>Default grid thumbnail width used while a thumbnail is loading or when fallback chrome is shown.</summary>
    public double MediaPanelGridFallbackThumbnailWidth => MediaPanelGridThumbnailFrameWidth;

    /// <summary>
    /// List-mode thumbnail height per scale step (0-7). Width follows 16:9 so the slider meaningfully grows table previews.
    /// </summary>
    private static readonly double[] MediaPanelListThumbHeights =
        { 36, 44, 52, 60, 72, 84, 96, 112 };

    /// <summary>List-mode thumbnail height for the current media scale step.</summary>
    public double MediaPanelListThumbHeight =>
        MediaPanelListThumbHeights[Math.Clamp(MediaPanelScaleStep, 0, MediaPanelScaleStepMax)];

    /// <summary>List-mode thumbnail width used by both the table header and each row.</summary>
    public double MediaPanelListThumbnailWidth =>
        Math.Round(MediaPanelListThumbHeight * 16.0 / 9.0, 0);

    /// <summary>Minimum list row height so thumbnails and metadata align when scale changes.</summary>
    public double MediaPanelListRowMinHeight => MediaPanelListThumbHeight + 20;

    /// <summary>Full-opacity RGB colour used to seed the ColorPicker and the swatch trigger (alpha always 255).</summary>
    public Color TransparentThumbnailColorWinUI => ParseHexToWinColor(TransparentThumbnailColor);

    /// <summary>Whether the browse stack has at least one loaded presentation.</summary>
    public bool HasBrowseStackContent => BrowseStackSections.Count > 0;

    /// <summary>Library/playlist selected but it contains no presentations (or all failed to load).</summary>
    public bool ShowBrowseStackEmpty => ShowBrowseStack && !HasBrowseStackContent;

    public string DocumentTitle => OpenDocument?.Manifest.Title ?? "";

    // ── Arrangement / playback-sequence properties ───────────────────────────

    /// <summary>Currently resolved playback sequence for the active arrangement.</summary>
    public PlaybackSequence PlaybackSequence => _playbackSequence;

    /// <summary>Named arrangements defined for the open presentation.</summary>
    public IReadOnlyList<NamedArrangement> Arrangements =>
        OpenProject?.Arrangement?.Arrangements ?? (IReadOnlyList<NamedArrangement>)Array.Empty<NamedArrangement>();

    /// <summary>The currently active <see cref="NamedArrangement"/>, or null when using natural order.</summary>
    public NamedArrangement? ActiveArrangement =>
        Arrangements.FirstOrDefault(a =>
            string.Equals(a.Id, OpenProject?.Arrangement?.ActiveArrangementId, StringComparison.OrdinalIgnoreCase))
        ?? Arrangements.FirstOrDefault(a => a.IsNatural);

    /// <summary>Display name of the active arrangement (e.g. Master) for the arrangements bar.</summary>
    public string ActiveArrangementDisplayName => ActiveArrangement?.Name ?? string.Empty;

    /// <summary>Two-way binding for the arrangement picker; changing selection updates playback and the slide deck.</summary>
    public NamedArrangement? ArrangementPickerSelectedItem
    {
        get => ActiveArrangement;
        set
        {
            if (_arrangementUpdateDepth > 0) return;
            if (value == null || OpenProject?.Arrangement == null)
                return;
            if (string.Equals(OpenProject.Arrangement.ActiveArrangementId, value.Id, StringComparison.OrdinalIgnoreCase))
                return;
            _ = SetActiveArrangementAsync(value.Id);
        }
    }

    /// <summary>Colored section-group chips for the arrangements bar (single-deck).</summary>
    public ObservableCollection<SectionGroupChipDisplay> ActiveArrangementGroupChips => _activeArrangementGroupChips;

    /// <summary>Section groups in the active arrangement, in playback order (tab buttons in the header).</summary>
    public IReadOnlyList<SectionGroup> ActiveArrangementGroups
    {
        get
        {
            var arr = ActiveArrangement;
            var sections = OpenProject?.Arrangement?.Sections;
            if (arr == null || sections == null)
                return Array.Empty<SectionGroup>();
            return arr.Groups
                .Select(r => sections.FirstOrDefault(g => string.Equals(g.Id, r.SectionGroupId, StringComparison.OrdinalIgnoreCase)))
                .Where(g => g != null)
                .Select(g => g!)
                .ToList();
        }
    }

    /// <summary>Duration text next to the clock icon when auto-advance is set (e.g. "4:10"); leading ~ removed — icon conveys estimate.</summary>
    public string PresentationDurationLabel
    {
        get
        {
            var secs = OpenProject?.Arrangement?.AutoAdvanceSeconds ?? 0;
            if (secs <= 0) return string.Empty;
            var slideCount = _playbackSequence.Instances.Count(i => !i.Slide.Disabled);
            if (slideCount == 0) return string.Empty;
            var totalSecs = secs * slideCount;
            return totalSecs >= 3600
                ? $"{totalSecs / 3600}:{(totalSecs % 3600) / 60:D2}:{totalSecs % 60:D2}"
                : $"{totalSecs / 60}:{totalSecs % 60:D2}";
        }
    }

    /// <summary>Auto-advance interval in seconds for the open presentation; 0 = disabled.</summary>
    public int AutoAdvanceSeconds => OpenProject?.Arrangement?.AutoAdvanceSeconds ?? 0;

    /// <summary>True when auto-advance is enabled for the open presentation.</summary>
    public bool IsAutoAdvanceEnabled => AutoAdvanceSeconds > 0;

    /// <summary>
    /// True when the open presentation has an arrangement-level default slide transition
    /// (overrides the Show toolbar global default for slides without per-slide overrides).
    /// </summary>
    public bool HasPresentationDefaultSlideTransition =>
        TransitionPresentationHelper.HasPresentationTransitionConfigured(OpenProject);

    public PresentationSlide? SelectedSlide =>
        OpenProject?.Slides.FirstOrDefault(slide => string.Equals(slide.Id, SelectedSlideId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Current program slide (live session), not the operator’s grid selection when audience is off.</summary>
    public PresentationSlide? LiveSlide
    {
        get
        {
            string? liveSlideId = _liveSessionQuery.Current.LiveSlideId;
            if (string.IsNullOrWhiteSpace(liveSlideId))
                return null;

            string? livePath = _liveSessionQuery.Current.LivePresentationPath;
            PresentationDocument? document = !string.IsNullOrWhiteSpace(livePath)
                ? _sessionCache.TryGet(livePath)
                : OpenDocument;
            return document?.Project?.Slides.FirstOrDefault(slide =>
                string.Equals(slide.Id, liveSlideId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Shared program-output surface used by the operator preview panel.</summary>
    public OutputViewModel ProgramOutput => _outputViewModel;

    /// <summary>Shared observable state for the active media cue playback (used by the output-panel transport row).</summary>
    public MediaTransportPresenter PlaybackCoordinator => _mediaTransportPresenter;

    /// <summary>Pipeline media transport service.</summary>
    public IMediaTransportService MediaTransport => _mediaTransport;

    /// <summary>Pipeline-backed live session query surface.</summary>
    public ILiveSessionQueryService LiveSessionQuery => _liveSessionQuery;

    /// <summary>Audience output (bound from Show page and shell title bar).</summary>
    public bool AudienceOutputEnabled
    {
        get => _outputTargets.AudienceEnabled;
        set
        {
            if (value == _outputTargets.AudienceEnabled)
                return;
            _ = SetAudienceEnabledAsync(value);
        }
    }

    /// <summary>Stage output (bound from shell title bar).</summary>
    public bool StageOutputEnabled
    {
        get => _outputTargets.StageEnabled;
        set
        {
            if (value == _outputTargets.StageEnabled)
                return;
            _ = SetStageEnabledAsync(value);
        }
    }

    /// <summary>Raises <see cref="AudienceOutputEnabled"/> when external runtime state changes.</summary>
    public void NotifyAudienceOutputChanged()
    {
        OnPropertyChanged(nameof(AudienceOutputEnabled));
    }

    /// <summary>Raises <see cref="StageOutputEnabled"/> when external runtime state changes.</summary>
    public void NotifyStageOutputChanged()
    {
        OnPropertyChanged(nameof(StageOutputEnabled));
    }

    /// <summary>Reopens audience and stage windows after output settings change and refreshes bound preview layout.</summary>
    public void SyncOutputWindowsAfterSettingsSave()
    {
        TryOpenAudienceWindows();
        TryOpenStageWindows();
        NotifyPreviewState();
    }

    /// <summary>Audience output toggle (runtime-only; monitor/layout preferences remain persisted separately).</summary>
    public async Task SetAudienceEnabledAsync(bool enabled)
    {
        if (enabled)
        {
            await _outputTargets.SetAudienceEnabledAsync(true).ConfigureAwait(true);
            await _outputWindows.OpenAudienceAsync().ConfigureAwait(true);
        }
        else
        {
            _outputWindows.CloseAll();
            await _outputTargets.SetAudienceEnabledAsync(false).ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(AudienceOutputEnabled));
    }

    /// <summary>Stage output toggle (runtime-only; monitor preferences remain persisted separately).</summary>
    public async Task SetStageEnabledAsync(bool enabled)
    {
        if (enabled)
        {
            await _outputTargets.SetStageEnabledAsync(true).ConfigureAwait(true);
            await _outputWindows.OpenStageAsync().ConfigureAwait(true);
        }
        else
        {
            _outputWindows.CloseStage();
            await _outputTargets.SetStageEnabledAsync(false).ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(StageOutputEnabled));
    }

    /// <summary>
    /// Reloads libraries and playlists from the local documents collection and refreshes the browse panes.
    /// </summary>
    public async Task RefreshCatalogAsync(ContentMaintenanceTrigger trigger = ContentMaintenanceTrigger.Default)
    {
        await _catalog.LoadAsync(trigger).ConfigureAwait(true);
        var prunedPresentationPaths = _sessionCache.PruneMissingFiles();
        foreach (var deletedPresentationPath in prunedPresentationPaths)
            _cuePreparation.InvalidatePresentationCues(deletedPresentationPath);

        RefreshCatalogCollections();
        CoerceSelectionToCatalog();
        NotifyCenterPanes();
        RefreshBrowseStackFromSelection();
    }

    /// <summary>
    /// Updates deck rows in place when a slide mutation only changes slide content (same deck shape).
    /// Avoids rebuilding <see cref="SlideDeckItems"/> and flashing thumbnails after Quick Edit apply.
    /// </summary>
    public Task<bool> TryApplyInPlaceSlideMutationAsync(SlideItemMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!PathsMatchNullable(SelectedPresentationPath, result.PresentationPath)
            || OpenDocument?.Project is null
            || result.Project.Slides.Count != OpenDocument.Project.Slides.Count)
            return Task.FromResult(false);

        var currentIds = OpenDocument.Project.Slides.Select(static slide => slide.Id).ToList();
        var updatedIds = result.Project.Slides.Select(static slide => slide.Id).ToList();
        if (!currentIds.SequenceEqual(updatedIds, StringComparer.OrdinalIgnoreCase))
            return Task.FromResult(false);

        var refreshedDocument = _sessionCache.GetOrLoad(result.PresentationPath) ?? OpenDocument;
        OpenDocument = refreshedDocument;
        Slides.ReplaceAll(result.Project.Slides);
        _activePresentation.SetCurrentPresentation(result.Project, result.PresentationPath);

        foreach (ShowSlideDeckItem item in EnumerateDeckThumbnailItems())
        {
            if (!PathsMatchNullable(ResolveItemPresentationPath(item), result.PresentationPath))
                continue;

            PresentationSlide? updatedSlide = result.Project.Slides.FirstOrDefault(slide =>
                string.Equals(slide.Id, item.Slide.Id, StringComparison.OrdinalIgnoreCase));
            if (updatedSlide is not null)
                item.ApplySlideModel(updatedSlide);
        }

        RefreshAllSlideDeckState();
        NotifyCenterPanes();
        NotifyArrangementState();
        return Task.FromResult(true);
    }

    /// <summary>Re-dispatches the live slide layer from the deck row's current preview slide.</summary>
    public async Task RefreshLiveSlidePreviewAsync(ShowSlideDeckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsLive)
            return;

        string? presentationPath = ResolveItemPresentationPath(item);
        if (string.IsNullOrWhiteSpace(presentationPath))
            return;

        PresentationDocument? document = _sessionCache.GetOrLoad(presentationPath) ?? OpenDocument;
        if (document?.Project == null)
            return;

        PreparedSlideCue? prepared = await _cuePreparation.PreparePreviewSlideCueAsync(
            document,
            item.ThumbnailPreviewSlide,
            item.InstanceKey).ConfigureAwait(true);
        if (prepared == null)
            return;

        _ = _cueDispatcher.DispatchSlideCueAsync(prepared with
        {
            LayerKind = ResolvePresentationDestinationLayer(presentationPath),
        });
    }

    /// <summary>Refreshes the live slide output after a saved in-place text mutation.</summary>
    public async Task RefreshLiveSlideAfterMutationAsync(SlideItemMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.SelectedSlideId))
            return;

        _cuePreparation.InvalidatePresentationCues(result.PresentationPath);

        ShowSlideDeckItem? liveItem = EnumerateDeckThumbnailItems().FirstOrDefault(item =>
            item.IsLive
            && string.Equals(item.Slide.Id, result.SelectedSlideId, StringComparison.OrdinalIgnoreCase)
            && PathsMatchNullable(ResolveItemPresentationPath(item), result.PresentationPath));
        if (liveItem == null)
            return;

        await RefreshLiveSlidePreviewAsync(liveItem).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a presentation path and persists the active workspace selection.
    /// </summary>
    /// <param name="path">Absolute path or content-root-relative presentation path.</param>
    /// <returns><c>true</c> when the presentation opened successfully; otherwise, <c>false</c>.</returns>
    public async Task<bool> OpenPresentationFromPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SelectedPresentationPath = path;
        var opened = await OpenPresentationPathAsync(path).ConfigureAwait(true);
        await PersistWorkspaceAsync().ConfigureAwait(true);
        return opened;
    }

    /// <summary>
    /// Clears the current open presentation state when the selected bundle has been deleted.
    /// </summary>
    public async Task ClearDeletedPresentationAsync(string deletedPresentationPath)
    {
        if (string.IsNullOrWhiteSpace(deletedPresentationPath))
            return;

        _cuePreparation.InvalidatePresentationCues(deletedPresentationPath);
        _sessionCache.Invalidate(deletedPresentationPath);

        if (!PathsMatchNullable(SelectedPresentationPath, deletedPresentationPath)
            && (OpenDocument == null || !PathsMatchNullable(OpenDocument.SourcePath, deletedPresentationPath)))
        {
            return;
        }

        SelectedPresentationPath = null;
        OpenDocument = null;
        Slides.Clear();
        SlideDeckItems.Clear();
        _playbackSequence = PlaybackSequence.Empty;
        StopAutoAdvance();
        ApplySlideSelectionState(null, null, userOverride: false);
        _activePresentation.SetCurrentPresentation(null, null);
        _ = _cueDispatcher.EndLiveAsync();
        RefreshBrowseStackFromSelection();
        NotifyPreviewState();
        NotifyArrangementState();
        NotifyCenterPanes();
        await PersistWorkspaceAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Updates the current selection to match an imported local item and opens it immediately.
    /// </summary>
    public async Task OpenImportedPresentationAsync(string path, string libraryId, string? playlistId)
    {
        if (!string.IsNullOrWhiteSpace(playlistId))
        {
            SelectedPlaylistId = playlistId;
            SelectedLibraryId = null;
        }
        else
        {
            SelectedLibraryId = libraryId;
            SelectedPlaylistId = null;
        }

        NotifyCenterPanes();
        await OpenPresentationFromPathAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Prompts for a presentation file and opens it without importing it into the local collection.
    /// </summary>
    public async Task<bool> PickAndOpenPresentationAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".cpres");

        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return false;

        return await OpenPresentationFromPathAsync(file.Path).ConfigureAwait(true);
    }

    /// <summary>True after the first <see cref="InitializeAsync"/> completes successfully.</summary>
    public bool IsWorkspaceInitialized => _initialized;

    public Task InitializeAsync()
    {
        if (_initializeTask is not null)
            return _initializeTask;

        _initializeTask = InitializeCoreAsync();
        return _initializeTask;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            // Deck prefs (global slide/media transitions, layout, etc.) must not read
            // default Settings before portable Show.json has been loaded from disk.
            _deckPreferencesHydrated = false;
            await _settings.LoadAsync().ConfigureAwait(true);
            await _workspace.LoadAsync().ConfigureAwait(true);

            LoadDeckPreferences();
            RefreshCatalogCollections();
            LoadWorkspaceIntoViewModel();

            var pending = _activation.ConsumePendingPresentationPath();
            if (!string.IsNullOrWhiteSpace(pending) && File.Exists(pending))
            {
                await OpenPresentationFromPathAsync(pending).ConfigureAwait(true);
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedPresentationPath)
                && string.IsNullOrWhiteSpace(SelectedLibraryId)
                && string.IsNullOrWhiteSpace(SelectedPlaylistId))
            {
                var initial = StartupWorkspaceSelector.TrySelectInitial(_catalog.Catalog, _settings.Settings);
                if (initial != null)
                    ApplyWorkspaceDto(initial);
            }

            EnsureDefaultSelection();

            // Tree was first built before workspace selection was applied; recompute expand state for the selected source.
            RebuildSidebarTree();
            NotifyCenterPanes();
            RefreshBrowseStackFromSelection();

            if (!string.IsNullOrWhiteSpace(SelectedPresentationPath))
                await OpenPresentationFromPathAsync(SelectedPresentationPath!).ConfigureAwait(true);
            else
                NotifyCenterPanes();
        }
        finally
        {
            _initialized = true;
        }
    }

    private void NotifyCenterPanes()
    {
        OnPropertyChanged(nameof(SelectedLibrary));
        OnPropertyChanged(nameof(SelectedPlaylist));
        OnPropertyChanged(nameof(ShowBrowseStack));
        OnPropertyChanged(nameof(ShowSingleDeckSlideGrid));
        OnPropertyChanged(nameof(ShowHome));
        OnPropertyChanged(nameof(HasPresentationSources));
        OnPropertyChanged(nameof(ShowPresentationSourcesEmpty));
        OnPropertyChanged(nameof(ShowPresentationHomePrompt));
        OnPropertyChanged(nameof(HasOpenPresentation));
        OnPropertyChanged(nameof(CanUsePresentationWorkspace));
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(OpenProject));
        OnPropertyChanged(nameof(ShowBrowseStackEmpty));
        NotifyDeckVisibilityChanged();
        RefreshTreeItemHighlights();
        NotifyPreviewState();
    }

    private void NotifyDeckVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowSingleDeckStandardLayout));
        OnPropertyChanged(nameof(ShowSingleDeckLyricGroupHeaderLayout));
        OnPropertyChanged(nameof(ShowSingleDeckStandardThumbnail));
        OnPropertyChanged(nameof(ShowSingleDeckStandardText));
        OnPropertyChanged(nameof(ShowSingleDeckStandardList));
        OnPropertyChanged(nameof(ShowSingleDeckLyricGroupHeaderThumbnail));
        OnPropertyChanged(nameof(ShowSingleDeckLyricGroupHeaderText));
        OnPropertyChanged(nameof(ShowSingleDeckLyricGroupHeaderList));
        OnPropertyChanged(nameof(ShowBrowseStackThumbnail));
        OnPropertyChanged(nameof(ShowBrowseStackText));
        OnPropertyChanged(nameof(ShowBrowseStackList));
    }

    /// <summary>Rebuilds stacked presentation decks when the sidebar library or playlist selection changes.</summary>
    private void RefreshBrowseStackFromSelection()
    {
        BrowseStackSections.Clear();
        OnPropertyChanged(nameof(HasBrowseStackContent));
        OnPropertyChanged(nameof(ShowBrowseStackEmpty));

        if (SelectedLibrary == null && SelectedPlaylist == null)
        {
            if (OpenDocument != null)
                RebuildSlideDeckItems();
            return;
        }

        if (SelectedLibrary != null)
        {
            if (string.IsNullOrWhiteSpace(SelectedPresentationPath))
            {
                OnPropertyChanged(nameof(HasBrowseStackContent));
                OnPropertyChanged(nameof(ShowBrowseStackEmpty));
                RefreshAllSlideDeckState();
                return;
            }

            PresentationRefDto? match = null;
            foreach (var p in SelectedLibrary.Presentations)
            {
                if (PathsMatch(p.Path, SelectedPresentationPath))
                {
                    match = p;
                    break;
                }
            }

            if (match != null)
            {
                // Eagerly warm the single library presentation in the cache.
                BeginSessionLoad(new[] { match });
                TryAddBrowseStackSection(match);
            }
        }
        else
        {
            var items = SelectedPlaylist!.Items;

            // Load every presentation synchronously (GetOrLoad per item) so the browse stack,
            // keyboard navigation, and output routing never race an incomplete session.
            foreach (var pref in items)
                TryAddBrowseStackSection(pref);

            // SessionPaths is used for prefetch look-ahead; set it immediately now that each
            // item is cached — no fire-and-forget LoadSessionAsync gap where SessionPaths is empty.
            _sessionCache.SetSessionOrder(items);
        }

        OnPropertyChanged(nameof(HasBrowseStackContent));
        OnPropertyChanged(nameof(ShowBrowseStackEmpty));
        RefreshAllSlideDeckState();
        SyncActiveSectionData();
        foreach (var section in BrowseStackSections)
            RefreshBrowseStackSectionHeaderState(section);

        if (BrowseStackSections.Count > 0 && !string.IsNullOrWhiteSpace(SelectedPresentationPath))
            BrowseStackScrollToSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cancels any in-progress session load and starts a new one for <paramref name="items"/>.
    /// The cache handles all background I/O; this method only wires the cancellation token.
    /// </summary>
    private void BeginSessionLoad(IReadOnlyList<PresentationRefDto> items)
    {
        _sessionLoadCts?.Cancel();
        _sessionLoadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _sessionLoadCts = cts;
        _ = _sessionCache.LoadSessionAsync(items, cts.Token);
    }

    private void TryAddBrowseStackSection(PresentationRefDto pref)
    {
        try
        {
            // GetOrLoad uses the session cache so subsequent calls to this method for the
            // same path return the already-loaded instance with no disk I/O.
            var doc = _sessionCache.GetOrLoad(pref.Path);
            if (doc?.Project == null)
                return;

            ApplyPresentationReferencePreferences(doc, pref);
            var title = string.IsNullOrWhiteSpace(doc.Manifest.Title) ? pref.Title : doc.Manifest.Title;
            var section = new ShowPresentationDeckSection(title, doc.SourcePath);
            var i = 1;
            foreach (var s in doc.Project.Slides)
                section.SlideRows.Add(new ShowSlideDeckItem(s, i++, doc.SourcePath, doc.Project));

            PopulateGroupedSlideSections(section.GroupedSlideSections, section.SlideRows);
            ApplyDeckSettingsToSection(section);
            ApplyBrowseStackHeaderState(section);
            BrowseStackSections.Add(section);
        }
        catch
        {
            // Skip unreadable entries so the rest of the library/playlist still loads.
        }
    }

    private void ApplyPresentationReferencePreferences(PresentationDocument doc, PresentationRefDto? reference)
    {
        if (doc.Project?.Arrangement == null || string.IsNullOrWhiteSpace(reference?.ArrangementId))
            return;

        if (doc.Project.Arrangement.Arrangements.Any(arrangement =>
                string.Equals(arrangement.Id, reference.ArrangementId, StringComparison.OrdinalIgnoreCase)))
        {
            doc.Project.Arrangement.ActiveArrangementId = reference.ArrangementId;
        }
    }

    private PresentationRefDto? ResolvePresentationReferenceForCurrentContext(string? presentationPath)
    {
        if (string.IsNullOrWhiteSpace(presentationPath))
            return null;

        if (!string.IsNullOrWhiteSpace(SelectedPlaylistId))
        {
            return SelectedPlaylist?.Items.FirstOrDefault(item => PathsMatch(item.Path, presentationPath));
        }

        if (!string.IsNullOrWhiteSpace(SelectedLibraryId))
        {
            return SelectedLibrary?.Presentations.FirstOrDefault(item => PathsMatch(item.Path, presentationPath));
        }

        return Libraries
            .SelectMany(library => library.Presentations)
            .FirstOrDefault(item => PathsMatch(item.Path, presentationPath));
    }

    private ChurchPresenter.Backend.Rendering.OutputLayerKind ResolvePresentationDestinationLayer(string? presentationPath)
    {
        var reference = ResolvePresentationReferenceForCurrentContext(presentationPath);
        return OutputRoutingDefaults.LayerIdEquals(
            reference?.DestinationLayerId,
            ChurchPresenter.Backend.Rendering.OutputLayerKind.Announcements)
            ? ChurchPresenter.Backend.Rendering.OutputLayerKind.Announcements
            : ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide;
    }

    /// <summary>
    /// Sets auto-advance on a specific presentation (by path) without changing the active selection.
    /// If the path matches the currently open presentation the live timer is rearmed immediately.
    /// </summary>
    public Task SetAutoAdvanceForPathAsync(string path, int seconds)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.CompletedTask;

        // Find the matching browse-stack section to update its cached value.
        var section = BrowseStackSections
            .FirstOrDefault(s => PathsMatch(s.PresentationPath, path));

        if (PathsMatch(path, SelectedPresentationPath))
        {
            // This IS the active presentation — use the normal method so everything
            // (timer, notifications, UI) is updated consistently.
            return SetAutoAdvanceAsync(seconds);
        }

        // Non-active presentation: load (from cache if available), modify, save directly.
        try
        {
            var doc = _sessionCache.GetOrLoad(path) ?? _presentationDocs.Open(path);
            if (doc.Project?.Arrangement != null)
            {
                doc.Project.Arrangement.AutoAdvanceSeconds = seconds;
                _projects.Save(doc.Project, doc.SourcePath);
                _sessionCache.UpdateEntry(path, doc);

                if (section != null)
                    RefreshBrowseStackSectionHeaderState(section);
            }
        }
        catch { /* non-critical */ }

        return Task.CompletedTask;
    }

    /// <summary>Formats duration text for the given auto-advance interval and project (no leading ~; UI shows a clock icon).</summary>
    private static string BuildDurationLabel(int autoAdvanceSeconds, PresentationProject project)
    {
        if (autoAdvanceSeconds <= 0) return string.Empty;
        var slideCount = project.Slides.Count(s => !s.Disabled);
        if (slideCount == 0) return string.Empty;
        var totalSecs = autoAdvanceSeconds * slideCount;
        return totalSecs >= 3600
            ? $"{totalSecs / 3600}:{(totalSecs % 3600) / 60:D2}:{totalSecs % 60:D2}"
            : $"{totalSecs / 60}:{totalSecs % 60:D2}";
    }

    private void LoadWorkspaceIntoViewModel()
    {
        var w = _workspace.Workspace;
        SelectedLibraryId = w.SelectedLibraryId;
        SelectedPlaylistId = w.SelectedPlaylistId;
        SelectedPresentationPath = w.SelectedPresentationPath;
        OutputPanelWidth = w.ShowOutputPanelWidth;
    }

    private void CoerceSelectionToCatalog()
    {
        if (!string.IsNullOrWhiteSpace(SelectedLibraryId)
            && Libraries.All(l => !string.Equals(l.Id, SelectedLibraryId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedLibraryId = null;
        }

        if (!string.IsNullOrWhiteSpace(SelectedPlaylistId)
            && Playlists.All(p => !string.Equals(p.Id, SelectedPlaylistId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPlaylistId = null;
        }
    }

    private void ApplyWorkspaceDto(WorkspaceDto dto)
    {
        _workspace.Update(ws =>
        {
            ws.ActivePage = dto.ActivePage;
            ws.SelectedLibraryId = dto.SelectedLibraryId;
            ws.SelectedPlaylistId = dto.SelectedPlaylistId;
            ws.SelectedPresentationPath = dto.SelectedPresentationPath;
        });
        SelectedLibraryId = dto.SelectedLibraryId;
        SelectedPlaylistId = dto.SelectedPlaylistId;
        SelectedPresentationPath = dto.SelectedPresentationPath;
    }

    private void EnsureDefaultSelection()
    {
        if (!string.IsNullOrWhiteSpace(SelectedPresentationPath))
            return;
        if (SelectedLibraryId != null || SelectedPlaylistId != null)
            return;
        var dv = _settings.Settings.Show.DefaultCenterView;
        if (dv == "playlist" && Playlists.Count > 0)
            SelectedPlaylistId = Playlists[0].Id;
        else if (Libraries.Count > 0)
            SelectedLibraryId = Libraries[0].Id;
    }

    private void RefreshCatalogCollections()
    {
        Libraries.Clear();
        foreach (var l in _catalog.Catalog.Libraries)
            Libraries.Add(l);
        Playlists.Clear();
        foreach (var p in _catalog.Catalog.Playlists)
            Playlists.Add(p);
        RebuildSidebarTree();
        OnPropertyChanged(nameof(HasPresentationSources));
        OnPropertyChanged(nameof(ShowPresentationSourcesEmpty));
        OnPropertyChanged(nameof(ShowPresentationHomePrompt));
    }

    private void RebuildSlideDeckItems()
    {
        // When a named (non-natural) arrangement is active, build the deck from the playback sequence
        // so that repeated groups appear multiple times and in arrangement order.
        if (_playbackSequence.Count > 0
            && !string.IsNullOrEmpty(_playbackSequence.ActiveArrangementId)
            && !string.Equals(_playbackSequence.ActiveArrangementId, "natural", StringComparison.OrdinalIgnoreCase))
        {
            RebuildSlideDeckItemsFromSequence();
            return;
        }

        var project = OpenProject;
        var path = GetCurrentOpenPresentationPath();
        var newItems = Slides.Select((s, idx) => new ShowSlideDeckItem(s, idx + 1, path, project));
        SlideDeckItems.ReplaceAll(newItems);
        ApplyDeckSettingsToItems(SlideDeckItems);
        SyncBrowseStackSlideRowsFromSlideDeck();
        RebuildGroupedSections();
        RefreshAllSlideDeckState();
        OnPropertyChanged(nameof(SelectedDeckRowForView));
        QueueVisibleDeckCueWarmup();
    }

    private void QueueVisibleDeckCueWarmup()
    {
        PresentationDocument? document = OpenDocument;
        if (document == null || SlideDeckItems.Count == 0)
            return;

        ShowSlideDeckItem[] items = SlideDeckItems
            .Where(static item => !item.Slide.Disabled)
            .Take(MaxVisibleDeckCueWarmups)
            .ToArray();
        if (items.Length == 0)
            items = SlideDeckItems.Take(MaxVisibleDeckCueWarmups).ToArray();

        _ = WarmVisibleDeckCueAsync(document, items);
    }

    private async Task WarmVisibleDeckCueAsync(PresentationDocument document, IReadOnlyList<ShowSlideDeckItem> items)
    {
        if (items.Count == 0)
            return;

        string presentationPath = document.SourcePath;
        foreach (ShowSlideDeckItem item in items)
        {
            try
            {
                PreparedSlideCue? prepared = _cuePreparation.GetPreparedSlideCue(presentationPath, item.Slide.Id, item.InstanceKey)
                    ?? await _cuePreparation.PrepareSlideCueAsync(presentationPath, item.Slide.Id, item.InstanceKey, document).ConfigureAwait(true);

                if (prepared != null)
                    await _mediaWarmup.WarmPreparedSlideMediaAsync(prepared).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not warm prepared cue for slide {SlideId}.", item.Slide.Id);
            }
        }
    }

    /// <summary>
    /// When library/playlist browse stack is visible, slide rows live on <see cref="BrowseStackSections"/>
    /// (seeded in file order from <see cref="TryAddBrowseStackSection"/>). Mirror the active
    /// <see cref="SlideDeckItems"/> order onto matching sections so arrangement changes update the visible deck.
    /// </summary>
    private void SyncBrowseStackSlideRowsFromSlideDeck()
    {
        if (OpenDocument == null)
            return;

        var path = OpenDocument.SourcePath;
        foreach (var section in BrowseStackSections)
        {
            if (!PathsMatch(section.PresentationPath, path))
                continue;

            section.SlideRows.ReplaceAll(SlideDeckItems);
            PopulateGroupedSlideSections(section.GroupedSlideSections, section.SlideRows);
            ApplyDeckSettingsToSection(section);
        }
    }

    private void RefreshAllSlideDeckState()
    {
        foreach (var item in SlideDeckItems)
        {
            item.IsSelected = IsSlideItemSelected(item);
            item.IsLive = IsSlideItemLive(item);
        }

        foreach (var section in BrowseStackSections)
        {
            foreach (var item in section.SlideRows)
            {
                item.IsSelected = IsSlideItemSelected(item);
                item.IsLive = IsSlideItemLive(item);
            }
        }
    }

    private bool IsSlideItemSelected(ShowSlideDeckItem item)
    {
        if (_selectedSlideKeys.Count == 0)
            return false;

        var itemPath = ResolveItemPresentationPath(item);
        var itemKey = new SlideDeckSelectionKey(itemPath, item.Slide.Id, item.InstanceKey);
        return _selectedSlideKeys.Any(key => SelectionKeysEqual(key, itemKey));
    }

    private ShowSlideDeckItem? FindPrimarySelectedDeckRow()
    {
        var primary = CreateSelectionKey(SelectedSlidePresentationPath, SelectedSlideId, SelectedSlideInstanceKey);
        if (primary == null)
            return null;

        var match = SlideDeckItems.FirstOrDefault(item => IsPrimarySelectedDeckRow(item, primary.Value));
        if (match != null)
            return match;

        return BrowseStackSections
            .SelectMany(section => section.SlideRows)
            .FirstOrDefault(item => IsPrimarySelectedDeckRow(item, primary.Value));
    }

    private bool IsPrimarySelectedDeckRow(ShowSlideDeckItem item, SlideDeckSelectionKey primary)
    {
        var itemPath = ResolveItemPresentationPath(item);
        var itemKey = new SlideDeckSelectionKey(itemPath, item.Slide.Id, item.InstanceKey);
        return SelectionKeysEqual(itemKey, primary);
    }

    private bool IsSlideItemLive(ShowSlideDeckItem item)
    {
        LiveLayerStateQuery? slideLayer = _liveSessionQuery.Current.ActiveLayers.FirstOrDefault(static layer =>
            layer.Kind == OutputLayerKind.Slide);
        if (slideLayer is not { IsLive: true }
            || string.IsNullOrWhiteSpace(slideLayer.PayloadId))
        {
            return false;
        }

        string slideId = slideLayer.PayloadSlideId ?? slideLayer.PayloadId;
        if (!string.Equals(item.Slide.Id, slideId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(slideLayer.PayloadInstanceKey)
            && !string.Equals(item.InstanceKey, slideLayer.PayloadInstanceKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var itemPath = ResolveItemPresentationPath(item);
        return PathsMatchNullable(itemPath, slideLayer.PayloadPresentationPath);
    }

    /// <summary>Refresh slide deck chrome after light/dark theme change (theme brush lookups; thumbnails unchanged).</summary>
    public void NotifySlideDeckThemeChromeChanged()
    {
        foreach (var item in SlideDeckItems)
            item.NotifyThemeChromeChanged();
        foreach (var section in BrowseStackSections)
        {
            foreach (var item in section.SlideRows)
                item.NotifyThemeChromeChanged();
        }
        foreach (var section in SlideDeckGroupedSections)
        {
            foreach (var item in section.SlideRows)
                item.NotifyThemeChromeChanged();
        }
    }

    /// <summary>All deck rows that display slide thumbnails (single deck, browse stack, grouped views).</summary>
    public IEnumerable<ShowSlideDeckItem> EnumerateDeckThumbnailItems()
    {
        var seen = new HashSet<ShowSlideDeckItem>();
        foreach (ShowSlideDeckItem item in SlideDeckItems)
        {
            if (seen.Add(item))
                yield return item;
        }

        foreach (ShowPresentationDeckSection section in BrowseStackSections)
        {
            foreach (ShowSlideDeckItem item in section.SlideRows)
            {
                if (seen.Add(item))
                    yield return item;
            }
        }

        foreach (ShowPresentationDeckSection section in SlideDeckGroupedSections)
        {
            foreach (ShowSlideDeckItem item in section.SlideRows)
            {
                if (seen.Add(item))
                    yield return item;
            }
        }
    }
}
