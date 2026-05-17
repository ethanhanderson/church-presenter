using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

using ChurchPresenter;
using ChurchPresenter.Backend.Rendering;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// Shared settings editor state for the Windows settings hub and detail pages.
/// Changes are debounced and written to <see cref="ISettingsService"/> (JSON under local app data).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const int PersistDebounceMs = 300;
    private const double MonitorRowPreviewMaxWidth = 156;
    private const double MonitorRowPreviewMaxHeight = 88;
    private const double MonitorRowPreviewInset = 6;

    private static readonly HashSet<string> PersistablePropertyNames = new(StringComparer.Ordinal)
    {
        nameof(ShowDefaultCenterView),
        nameof(ShowThumbnailSize),
        nameof(ShowSlideLabels),
        nameof(ShowAutoTakeOnDoubleClick),
        nameof(ShowMediaSeekSeconds),
        nameof(EditorAutoSaveEnabled),
        nameof(EditorAutoSaveOnCreate),
        nameof(EditorShowGrid),
        nameof(EditorSnapToGrid),
        nameof(EditorGridSize),
        nameof(EditorAutosaveInterval),
        nameof(ReflowTextSize),
        nameof(ReflowPreviewDensity),
        nameof(ReflowShowSlideLabels),
        nameof(MusicSupabaseUrl),
        nameof(MusicPublishableKey),
        nameof(MusicDefaultSongAction),
        nameof(MusicPreferSetImportView),
        nameof(AppTheme),
        nameof(MaxRecentFiles),
    };

    private readonly IMonitorService _monitors;
    private readonly ISettingsService _settings;
    private readonly IContentDirectoryService _content;
    private readonly IContentMaintenanceLogService _contentMaintenanceLog;
    private readonly IContentAuditService _contentAudit;
    private readonly IContentDiagnosticsQueryService _contentDiagnostics;
    private readonly IContentStartupMaintenanceService _contentStartupMaintenance;
    private readonly ISettingsHealthService _settingsHealth;
    private readonly IOutputRoutingService _routing;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ShowViewModel _show;
    private int? _selectedAudienceMonitorIndex;
    private int? _selectedStageMonitorIndex;

    private bool _suspendAutoSave;
    private bool _pendingOutputSideEffects;
    private CancellationTokenSource? _debounceCts;

    public SettingsViewModel(
        IMonitorService monitors,
        ISettingsService settings,
        IContentDirectoryService content,
        IContentMaintenanceLogService contentMaintenanceLog,
        IContentAuditService contentAudit,
        IContentDiagnosticsQueryService contentDiagnostics,
        IContentStartupMaintenanceService contentStartupMaintenance,
        ISettingsHealthService settingsHealth,
        IOutputRoutingService routing,
        ILogger<SettingsViewModel> logger,
        ShowViewModel show)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _contentMaintenanceLog = contentMaintenanceLog ?? throw new ArgumentNullException(nameof(contentMaintenanceLog));
        _contentAudit = contentAudit ?? throw new ArgumentNullException(nameof(contentAudit));
        _contentDiagnostics = contentDiagnostics ?? throw new ArgumentNullException(nameof(contentDiagnostics));
        _contentStartupMaintenance = contentStartupMaintenance ?? throw new ArgumentNullException(nameof(contentStartupMaintenance));
        _settingsHealth = settingsHealth ?? throw new ArgumentNullException(nameof(settingsHealth));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _contentStartupMaintenance.Changed += ContentStartupMaintenance_Changed;
    }

    public string HubSubtitle { get; } =
        "Configure Church Presenter for output, show operation, editing, integrations, and appearance.";

    public IReadOnlyList<string> ShowCenterViewChoices { get; } = new[] { "slides", "playlist", "library" };

    public IReadOnlyList<string> ReflowDensityChoices { get; } = new[] { "comfortable", "compact" };

    public IReadOnlyList<string> MusicSongActionChoices { get; } = new[] { "import", "link" };

    public IReadOnlyList<string> ThemeChoices { get; } = new[] { "system", "light", "dark" };

    /// <summary>Audience output: picker flyout rows (arrangement diagram).</summary>
    public ObservableCollection<MonitorLayoutItem> MonitorLayoutItems { get; } = new();

    /// <summary>Stage output: picker flyout rows (arrangement diagram).</summary>
    public ObservableCollection<MonitorLayoutItem> StageMonitorLayoutItems { get; } = new();

    /// <summary>Audience output: horizontal monitor-selection card strip.</summary>
    public ObservableCollection<MonitorCardItem> AudienceMonitorCards { get; } = new();

    /// <summary>Stage output: horizontal monitor-selection card strip.</summary>
    public ObservableCollection<MonitorCardItem> StageMonitorCards { get; } = new();

    /// <summary>Configured clear groups for the active output look.</summary>
    public ObservableCollection<ClearGroupSettingsItem> ClearGroups { get; } = new();

    /// <summary>Per-audience-screen route settings for the active output look.</summary>
    public ObservableCollection<OutputLookScreenRouteSettingsItem> LookScreenRoutes { get; } = new();

    public ObservableCollection<ContentMaintenanceLogEntry> ContentMaintenanceEntries { get; } = new();

    /// <summary>Summary health cards shown at the top of the Library storage page (always visible).</summary>
    public ObservableCollection<ContentHealthSummaryCard> HealthSummaryCards { get; } = new();

    /// <summary>Compact view of the last 3 maintenance checks shown below the action controls.</summary>
    public ObservableCollection<ContentMaintenanceLogEntry> RecentChecks { get; } = new();

    /// <summary>Settings-level health issues for the hub warning badges and per-page inline warnings.</summary>
    public ObservableCollection<SettingsHealthIssue> HealthIssues { get; } = new();

    /// <summary>Library-management health issues shown on the Library storage page.</summary>
    public ObservableCollection<SettingsHealthIssue> LibraryHealthIssues { get; } = new();

    /// <summary>Content root, catalog, cache, and media diagnostics shown on the Library storage page.</summary>
    public ObservableCollection<ContentDiagnosticItem> ContentDiagnostics { get; } = new();

    /// <summary>Issues from the last persisted or just-run content audit.</summary>
    public ObservableCollection<AuditIssue> AuditIssues { get; } = new();

    [ObservableProperty]
    private bool _hasAudienceSelectedMonitor;

    [ObservableProperty]
    private string _audienceSelectedMonitorButtonTitle = "Choose a display";

    [ObservableProperty]
    private bool _hasStageSelectedMonitor;

    [ObservableProperty]
    private string _stageSelectedMonitorButtonTitle = "Choose a display";

    [ObservableProperty]
    private string _showDefaultCenterView = "slides";

    [ObservableProperty]
    private int _showThumbnailSize = 200;

    [ObservableProperty]
    private bool _showSlideLabels = true;

    [ObservableProperty]
    private bool _showAutoTakeOnDoubleClick;

    [ObservableProperty]
    private int _showMediaSeekSeconds = 5;

    [ObservableProperty]
    private bool _editorAutoSaveEnabled = true;

    [ObservableProperty]
    private bool _editorAutoSaveOnCreate = true;

    [ObservableProperty]
    private bool _editorShowGrid;

    [ObservableProperty]
    private bool _editorSnapToGrid;

    [ObservableProperty]
    private int _editorGridSize = 8;

    [ObservableProperty]
    private int _editorAutosaveInterval = 30;

    [ObservableProperty]
    private string _contentFolderPath = "";

    [ObservableProperty]
    private string _defaultContentFolderPath = "";

    [ObservableProperty]
    private bool _isUsingDefaultContentFolder = true;

    [ObservableProperty]
    private string _contentMaintenanceLogPath = "";

    [ObservableProperty]
    private string _contentMaintenanceStatus = "Ready";

    [ObservableProperty]
    private string _contentMaintenanceEmptyState = "No maintenance entries yet.";

    [ObservableProperty]
    private bool _isContentMaintenanceBusy;

    [ObservableProperty]
    private bool _isHistoryExpanded;

    [ObservableProperty]
    private string _lastAuditSummary = "No audit has been run yet.";

    [ObservableProperty]
    private string _lastAuditAt = "Never";

    [ObservableProperty]
    private string _backgroundContentMaintenanceStatus = "Startup maintenance has not run yet.";

    [ObservableProperty]
    private string _libraryHealthIssuesEmptyState = "No library settings issues detected.";

    [ObservableProperty]
    private string _contentDiagnosticsEmptyState = "No content diagnostics reported.";

    [ObservableProperty]
    private string _auditIssuesEmptyState = "No audit issues reported.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryHealthBadgeVisibility))]
    private int _outstandingIssueCount;

    public Microsoft.UI.Xaml.Visibility LibraryHealthBadgeVisibility =>
        OutstandingIssueCount > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    private string _outputHealthStatus = "info";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedClearGroup))]
    private ClearGroupSettingsItem? _selectedClearGroup;

    /// <summary>Whether the clear-groups editor has a selected group.</summary>
    public bool HasSelectedClearGroup => SelectedClearGroup != null;

    [ObservableProperty]
    private int _reflowTextSize = 14;

    [ObservableProperty]
    private string _reflowPreviewDensity = "comfortable";

    [ObservableProperty]
    private bool _reflowShowSlideLabels = true;

    [ObservableProperty]
    private string _musicSupabaseUrl = "";

    [ObservableProperty]
    private string _musicPublishableKey = "";

    [ObservableProperty]
    private string _musicDefaultSongAction = "import";

    [ObservableProperty]
    private bool _musicPreferSetImportView;

    [ObservableProperty]
    private string _appTheme = "system";

    [ObservableProperty]
    private int _maxRecentFiles = 10;

    /// <summary>Loads every editable field from <see cref="ISettingsService.Settings"/> (hub + before navigating to detail).</summary>
    public void LoadAllFromSettings()
    {
        _suspendAutoSave = true;
        try
        {
            LoadOutputSectionFromSettings();
            LoadClearGroupsFromRouting();
            AppSettingsDto s = _settings.Settings;
            ShowDefaultCenterView = string.IsNullOrWhiteSpace(s.Show.DefaultCenterView) ? "slides" : s.Show.DefaultCenterView;
            ShowThumbnailSize = Math.Clamp(s.Show.ThumbnailSize, 140, 320);
            ShowSlideLabels = s.Show.ShowSlideLabels;
            ShowAutoTakeOnDoubleClick = s.Show.AutoTakeOnDoubleClick;
            ShowMediaSeekSeconds = s.Show.MediaSeekSeconds <= 0 ? 5 : Math.Clamp(s.Show.MediaSeekSeconds, 1, 60);

            EditorAutoSaveEnabled = s.Editor.AutoSaveEnabled;
            EditorAutoSaveOnCreate = s.Editor.AutoSaveOnCreate;
            EditorShowGrid = s.Editor.ShowGrid;
            EditorSnapToGrid = s.Editor.SnapToGrid;
            EditorGridSize = s.Editor.GridSize is >= 5 and <= 50 ? s.Editor.GridSize : 8;
            EditorAutosaveInterval = s.Editor.AutosaveInterval > 0 ? s.Editor.AutosaveInterval : 30;

            RefreshContentLocationProperties();

            ReflowTextSize = s.Reflow.TextSize is >= 11 and <= 18 ? s.Reflow.TextSize : 14;
            ReflowPreviewDensity = string.IsNullOrWhiteSpace(s.Reflow.PreviewDensity) ? "comfortable" : s.Reflow.PreviewDensity;
            ReflowShowSlideLabels = s.Reflow.ShowSlideLabels;

            MusicSupabaseUrl = s.Integrations.MusicManager.SupabaseUrl ?? string.Empty;
            MusicPublishableKey = s.Integrations.MusicManager.PublishableKey ?? string.Empty;
            MusicDefaultSongAction = string.IsNullOrWhiteSpace(s.Integrations.MusicManager.DefaultSongAction)
                ? "import"
                : s.Integrations.MusicManager.DefaultSongAction;
            MusicPreferSetImportView = s.Integrations.MusicManager.PreferSetImportView;

            AppTheme = string.IsNullOrWhiteSpace(s.Theme) ? "system" : s.Theme;
            MaxRecentFiles = s.MaxRecentFiles is >= 5 and <= 25 ? s.MaxRecentFiles : 10;
        }
        finally
        {
            _suspendAutoSave = false;
        }
    }

    /// <summary>
    /// Loads the library management section state, including current location, health summary cards,
    /// recent maintenance events, and settings health issues.
    /// </summary>
    public async Task LoadLibraryManagementSectionAsync(CancellationToken cancellationToken = default)
    {
        RefreshContentLocationProperties();
        ApplyContentStartupMaintenanceSnapshot(_contentStartupMaintenance.Current);
        await RefreshContentMaintenanceLogAsync(cancellationToken).ConfigureAwait(true);
        await RefreshHealthAsync(cancellationToken).ConfigureAwait(true);
        await RefreshLastAuditAsync(cancellationToken).ConfigureAwait(true);
        await RefreshContentDiagnosticsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reloads recent maintenance entries from local app data.
    /// </summary>
    public async Task RefreshContentMaintenanceLogAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _contentMaintenanceLog.ReadRecentEntriesAsync(cancellationToken: cancellationToken).ConfigureAwait(true);

        ContentMaintenanceEntries.Clear();
        foreach (var entry in entries)
            ContentMaintenanceEntries.Add(entry);

        ContentMaintenanceEmptyState = ContentMaintenanceEntries.Count == 0
            ? "No maintenance events have been recorded yet."
            : string.Empty;
        ContentMaintenanceStatus = ContentMaintenanceEntries.Count == 0
            ? "No maintenance activity recorded yet."
            : $"Showing {ContentMaintenanceEntries.Count} recent maintenance entr{(ContentMaintenanceEntries.Count == 1 ? "y" : "ies")}.";

        RecentChecks.Clear();
        foreach (var entry in ContentMaintenanceEntries.Take(3))
            RecentChecks.Add(entry);
    }

    /// <summary>Refreshes settings health issues and rebuilds the summary health cards.</summary>
    public async Task RefreshHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _settingsHealth.ValidateAsync(cancellationToken).ConfigureAwait(true);

            HealthIssues.Clear();
            foreach (var issue in snapshot.Issues)
                HealthIssues.Add(issue);

            LibraryHealthIssues.Clear();
            foreach (var issue in snapshot.Issues.Where(IsLibraryHealthIssue))
                LibraryHealthIssues.Add(issue);
            LibraryHealthIssuesEmptyState = LibraryHealthIssues.Count == 0
                ? "No library settings issues detected."
                : string.Empty;

            OutstandingIssueCount = snapshot.Issues.Count(i =>
                string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.Severity, "warning", StringComparison.OrdinalIgnoreCase));

            var outputIssues = snapshot.Issues.Where(i => string.Equals(i.Area, "output", StringComparison.OrdinalIgnoreCase)).ToList();
            OutputHealthStatus = outputIssues.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)) ? "error"
                : outputIssues.Any(i => string.Equals(i.Severity, "warning", StringComparison.OrdinalIgnoreCase)) ? "warning"
                : "healthy";

            RebuildHealthSummaryCards();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh settings health.");
        }
    }

    /// <summary>Refreshes operator-facing content diagnostics and cache health projections.</summary>
    public async Task RefreshContentDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _contentDiagnostics.GetSnapshotAsync(cancellationToken).ConfigureAwait(true);
            ApplyContentDiagnostics(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh content diagnostics.");
            ContentDiagnosticsEmptyState = $"Could not load content diagnostics: {ex.Message}";
        }
    }

    /// <summary>Loads the last audit result and refreshes the audit display fields.</summary>
    public async Task RefreshLastAuditAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var audit = await _contentAudit.LoadLastAuditResultAsync(cancellationToken).ConfigureAwait(true);
            ApplyAuditResult(audit);
            RebuildHealthSummaryCards();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load last audit result.");
        }
    }

    /// <summary>
    /// Runs a full content audit and refreshes all audit-related display fields.
    /// </summary>
    public async Task RunAuditAsync(CancellationToken cancellationToken = default)
    {
        if (IsContentMaintenanceBusy)
            return;

        IsContentMaintenanceBusy = true;
        try
        {
            ContentMaintenanceStatus = "Running content audit...";
            var audit = await _contentAudit.RunAuditAsync(cancellationToken).ConfigureAwait(true);
            ApplyAuditResult(audit);
            await RefreshContentMaintenanceLogAsync(cancellationToken).ConfigureAwait(true);
            await RefreshHealthAsync(cancellationToken).ConfigureAwait(true);
            await RefreshContentDiagnosticsAsync(cancellationToken).ConfigureAwait(true);
            RebuildHealthSummaryCards();
            ContentMaintenanceStatus = audit.Issues.Count == 0
                ? "Audit complete. No issues found."
                : $"Audit complete. {audit.Issues.Count} issue{(audit.Issues.Count == 1 ? string.Empty : "s")} found.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run content audit.");
            ContentMaintenanceStatus = $"Audit failed: {ex.Message}";
        }
        finally
        {
            IsContentMaintenanceBusy = false;
        }
    }

    /// <summary>
    /// Points the app at a new managed content root and refreshes the in-memory catalog from that location.
    /// </summary>
    public async Task ChangeContentLibraryLocationAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (IsContentMaintenanceBusy)
            return;

        IsContentMaintenanceBusy = true;
        try
        {
            await _content.SetDocumentsDataDirectoryOverrideAsync(path, cancellationToken).ConfigureAwait(true);
            RefreshContentLocationProperties();

            await _contentMaintenanceLog.AppendEntriesAsync(
                    new[]
                    {
                        new ContentMaintenanceLogEntry
                        {
                            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                            Trigger = ContentMaintenanceTrigger.LocationChanged.ToString(),
                            Severity = "info",
                            EventType = "content-root-updated",
                            Message = IsUsingDefaultContentFolder
                                ? "Library location reset to the default Documents folder."
                                : "Library location updated.",
                            Path = ContentFolderPath,
                        },
                    },
                    cancellationToken)
                .ConfigureAwait(true);

            await _contentStartupMaintenance.StartAsync(ContentMaintenanceTrigger.LocationChanged, cancellationToken).ConfigureAwait(true);
            await RefreshContentMaintenanceLogAsync(cancellationToken).ConfigureAwait(true);
            await RefreshHealthAsync(cancellationToken).ConfigureAwait(true);
            await RefreshContentDiagnosticsAsync(cancellationToken).ConfigureAwait(true);
            RebuildHealthSummaryCards();
            ContentMaintenanceStatus = $"Using content root: {ContentFolderPath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change the managed content directory.");
            ContentMaintenanceStatus = $"Could not update the library location: {ex.Message}";
        }
        finally
        {
            IsContentMaintenanceBusy = false;
        }
    }

    /// <summary>
    /// Re-runs the managed content scan and repair workflow and refreshes the recent event list.
    /// </summary>
    public async Task ScanContentLibraryAsync(CancellationToken cancellationToken = default)
    {
        if (IsContentMaintenanceBusy)
            return;

        IsContentMaintenanceBusy = true;
        try
        {
            ContentMaintenanceStatus = "Scanning for changes and repairs...";
            await _contentStartupMaintenance.StartAsync(ContentMaintenanceTrigger.ManualScan, cancellationToken).ConfigureAwait(true);
            await RefreshContentMaintenanceLogAsync(cancellationToken).ConfigureAwait(true);
            await RefreshHealthAsync(cancellationToken).ConfigureAwait(true);
            await RefreshContentDiagnosticsAsync(cancellationToken).ConfigureAwait(true);
            ContentMaintenanceStatus = _contentStartupMaintenance.Current.StatusMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan the managed content directory.");
            ContentMaintenanceStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsContentMaintenanceBusy = false;
        }
    }

    /// <summary>Output-only: loads monitor selections for both roles from persisted settings.</summary>
    public void LoadOutputSectionFromSettings()
    {
        _suspendAutoSave = true;
        try
        {
            OutputSettingsDto o = _settings.Settings.Output;
            IReadOnlyList<MonitorInfoDto> monitors = _monitors.GetMonitors();

            _selectedAudienceMonitorIndex = AudienceOutputMonitorSelection.ResolvePreferredMonitorIndex(
                ParseMonitorIds(o.AudienceMonitorIds),
                monitors);

            // Stage does not auto-fallback to first monitor — only pick if an explicit assignment exists.
            _selectedStageMonitorIndex = ResolveStageMonitorIndex(
                ParseMonitorIds(o.StageMonitorIds),
                monitors);

            RefreshMonitors();
            LoadLookRoutesFromRouting();
            LoadClearGroupsFromRouting();
        }
        finally
        {
            _suspendAutoSave = false;
        }
    }

    /// <summary>Loads configured clear groups from the current output look.</summary>
    public void LoadClearGroupsFromRouting()
    {
        ClearGroups.Clear();
        foreach (OutputLookClearGroupDefinition group in _routing.ActiveLook.ClearGroups)
            ClearGroups.Add(ClearGroupSettingsItem.FromDefinition(group));

        SelectedClearGroup = ClearGroups.FirstOrDefault();
    }

    /// <summary>Loads per-screen Look route settings from the active output look.</summary>
    public void LoadLookRoutesFromRouting()
    {
        LookScreenRoutes.Clear();
        OutputLookDefinition activeLook = _routing.ActiveLook;
        foreach (OutputFeedDefinition feed in _routing.Feeds)
        {
            OutputLookFeedRouting route = activeLook.ResolveRouting(feed.Id);
            OutputLayerRouteDefinition? slide = route.ResolveLayerRoute(OutputLayerKind.Slide);
            OutputLayerRouteDefinition? mask = route.ResolveLayerRoute(OutputLayerKind.Mask);
            LookScreenRoutes.Add(new OutputLookScreenRouteSettingsItem
            {
                FeedId = feed.Id,
                DisplayName = feed.DisplayName,
                SlideEnabled = route.Routes(OutputLayerKind.Slide),
                MediaEnabled = route.Routes(OutputLayerKind.Media),
                MaskEnabled = route.Routes(OutputLayerKind.Mask),
                SlideThemeVariantId = slide?.ThemeVariantId ?? string.Empty,
                MaskId = mask?.MaskId ?? string.Empty,
            });
        }
    }

    /// <summary>Persists per-screen layer, theme, and mask route settings for the active output look.</summary>
    public Task PersistLookRoutesAsync(CancellationToken cancellationToken = default) =>
        _routing.SetRoutesAsync(
            LookScreenRoutes.Select(static route => route.ToDefinition()),
            cancellationToken);

    /// <summary>Adds a new custom clear group and persists the active look.</summary>
    public async Task AddClearGroupAsync(CancellationToken cancellationToken = default)
    {
        var item = ClearGroupSettingsItem.FromDefinition(new OutputLookClearGroupDefinition
        {
            Id = $"clear-{Guid.NewGuid():N}",
            Name = "New Clear Group",
            Icon = "\uE894",
            Scopes = [OutputClearScope.Presentation],
            Layers = OutputRoutingDefaults.CreateClearGroupLayers(OutputClearScope.Presentation),
        });

        ClearGroups.Add(item);
        SelectedClearGroup = item;
        await PersistClearGroupsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Deletes the selected clear group and persists the active look.</summary>
    public async Task DeleteSelectedClearGroupAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedClearGroup == null)
            return;

        int index = ClearGroups.IndexOf(SelectedClearGroup);
        ClearGroups.Remove(SelectedClearGroup);
        SelectedClearGroup = ClearGroups.Count == 0
            ? null
            : ClearGroups[Math.Clamp(index, 0, ClearGroups.Count - 1)];
        await PersistClearGroupsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Persists clear groups after the selected item was edited.</summary>
    public Task PersistClearGroupsAsync(CancellationToken cancellationToken = default) =>
        _routing.SetClearGroupsAsync(
            ClearGroups.Select(static group => group.ToDefinition()),
            cancellationToken);

    /// <summary>Refreshes both audience and stage monitor item collections from the current display topology.</summary>
    public void RefreshMonitors()
    {
        IReadOnlyList<MonitorInfoDto> monitors = _monitors.GetMonitors();

        if (monitors.Count == 0)
        {
            _selectedAudienceMonitorIndex = null;
            _selectedStageMonitorIndex = null;
            MonitorLayoutItems.Clear();
            StageMonitorLayoutItems.Clear();
            AudienceMonitorCards.Clear();
            StageMonitorCards.Clear();
            UpdateAudienceMonitorDetails(null);
            UpdateStageMonitorDetails(null);
            return;
        }

        // Revalidate selections against current topology.
        _selectedAudienceMonitorIndex = AudienceOutputMonitorSelection.ResolvePreferredMonitorIndex(
            _selectedAudienceMonitorIndex is int ai ? new[] { ai } : Array.Empty<int>(),
            monitors);

        _selectedStageMonitorIndex = ResolveStageMonitorIndex(
            _selectedStageMonitorIndex is int si ? new[] { si } : Array.Empty<int>(),
            monitors);

        // Enforce exclusivity: if both roles ended up on the same monitor, clear stage.
        if (_selectedAudienceMonitorIndex.HasValue
            && _selectedAudienceMonitorIndex == _selectedStageMonitorIndex)
            _selectedStageMonitorIndex = null;

        // Build the shared layout geometry for flyout arrangement previews.
        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        int maxX = monitors.Max(m => m.X + m.Width);
        int maxY = monitors.Max(m => m.Y + m.Height);
        double rawWidth = Math.Max(1, maxX - minX);
        double rawHeight = Math.Max(1, maxY - minY);
        double availW = Math.Max(1, MonitorRowPreviewMaxWidth - (MonitorRowPreviewInset * 2));
        double availH = Math.Max(1, MonitorRowPreviewMaxHeight - (MonitorRowPreviewInset * 2));
        double previewScale = Math.Min(availW / rawWidth, availH / rawHeight);
        if (double.IsNaN(previewScale) || double.IsInfinity(previewScale) || previewScale <= 0)
            previewScale = 1;

        double offsetX = Math.Max(MonitorRowPreviewInset,
            Math.Round(((MonitorRowPreviewMaxWidth - Math.Round(rawWidth * previewScale, 2)) / 2), 2));
        double offsetY = Math.Max(MonitorRowPreviewInset,
            Math.Round(((MonitorRowPreviewMaxHeight - Math.Round(rawHeight * previewScale, 2)) / 2), 2));

        // Build flyout rows and card items for both roles.
        MonitorLayoutItems.Clear();
        StageMonitorLayoutItems.Clear();
        AudienceMonitorCards.Clear();
        StageMonitorCards.Clear();

        foreach (MonitorInfoDto m in monitors)
        {
            string resolutionText = $"{m.Width}×{m.Height}";
            if (m.RefreshRate is uint hz and > 0)
                resolutionText += $" · {hz} Hz";
            string positionText = $"({m.X}, {m.Y})";
            string roleText = m.IsPrimary ? "Primary display" : "Additional display";

            IReadOnlyList<MonitorPreviewTile> tiles = monitors
                .Select(c => new MonitorPreviewTile(
                    offsetX + Math.Round((c.X - minX) * previewScale, 2),
                    offsetY + Math.Round((c.Y - minY) * previewScale, 2),
                    Math.Max(10, Math.Round(c.Width * previewScale, 2)),
                    Math.Max(10, Math.Round(c.Height * previewScale, 2)),
                    c.Index == m.Index))
                .ToList();

            bool isAudienceSelected = _selectedAudienceMonitorIndex == m.Index;
            bool isStageSelected = _selectedStageMonitorIndex == m.Index;

            MonitorLayoutItems.Add(new MonitorLayoutItem(
                m.Index, m.Name, resolutionText, positionText, roleText,
                m.Width, m.Height, m.X, m.Y, m.IsPrimary, m.RefreshRate,
                MonitorRowPreviewMaxWidth, MonitorRowPreviewMaxHeight, tiles, isAudienceSelected));

            StageMonitorLayoutItems.Add(new MonitorLayoutItem(
                m.Index, m.Name, resolutionText, positionText, roleText,
                m.Width, m.Height, m.X, m.Y, m.IsPrimary, m.RefreshRate,
                MonitorRowPreviewMaxWidth, MonitorRowPreviewMaxHeight, tiles, isStageSelected));

            // Cards for the top strip — each marks the other role's selection as excluded.
            AudienceMonitorCards.Add(new MonitorCardItem(
                m.Index, m.Name, resolutionText,
                m.Width, m.Height,
                isSelected: isAudienceSelected,
                isExcluded: isStageSelected));

            StageMonitorCards.Add(new MonitorCardItem(
                m.Index, m.Name, resolutionText,
                m.Width, m.Height,
                isSelected: isStageSelected,
                isExcluded: isAudienceSelected));
        }

        UpdateAudienceMonitorDetails(MonitorLayoutItems.FirstOrDefault(item => item.Index == _selectedAudienceMonitorIndex));
        UpdateStageMonitorDetails(StageMonitorLayoutItems.FirstOrDefault(item => item.Index == _selectedStageMonitorIndex));
    }

    /// <summary>Selects a monitor for audience output, enforcing exclusive assignment.</summary>
    public void SelectAudienceMonitor(int index)
    {
        if (_selectedAudienceMonitorIndex == index)
            return;

        // Enforce exclusivity — clear stage if it owns this monitor.
        if (_selectedStageMonitorIndex == index)
            _selectedStageMonitorIndex = null;

        _selectedAudienceMonitorIndex = index;
        RefreshMonitors();
        SchedulePersist(outputSideEffects: true);
    }

    /// <summary>Selects a monitor for stage output, enforcing exclusive assignment.</summary>
    public void SelectStageMonitor(int index)
    {
        if (_selectedStageMonitorIndex == index)
            return;

        // Enforce exclusivity — clear audience if it owns this monitor.
        if (_selectedAudienceMonitorIndex == index)
            _selectedAudienceMonitorIndex = null;

        _selectedStageMonitorIndex = index;
        RefreshMonitors();
        SchedulePersist(outputSideEffects: true);
    }

    /// <summary>Returns the monitor id list to persist for audience output.</summary>
    public IReadOnlyList<string> GetAudienceMonitorIdsForSave() =>
        _selectedAudienceMonitorIndex is int ai
            ? new[] { ai.ToString() }
            : Array.Empty<string>();

    /// <summary>Returns the monitor id list to persist for stage output.</summary>
    public IReadOnlyList<string> GetStageMonitorIdsForSave() =>
        _selectedStageMonitorIndex is int si
            ? new[] { si.ToString() }
            : Array.Empty<string>();

    /// <summary>
    /// Resolves the stage monitor index from requested indices without the audience fallback-to-first behavior;
    /// returns null when no valid stored assignment exists.
    /// </summary>
    private static int? ResolveStageMonitorIndex(
        IReadOnlyList<int> requestedIndices,
        IReadOnlyList<MonitorInfoDto> availableMonitors)
    {
        IReadOnlyList<int> valid = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            requestedIndices, availableMonitors);
        return valid.Count > 0 ? valid[0] : null;
    }

    /// <summary>Clears the recent-files list in settings and persists immediately.</summary>
    public async Task ClearRecentFilesAsync(CancellationToken cancellationToken = default)
    {
        _debounceCts?.Cancel();
        _debounceCts = null;
        _settings.Update(s => s.RecentFiles.Clear());
        await _settings.SaveAsync().ConfigureAwait(true);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suspendAutoSave || e.PropertyName is null)
            return;
        if (!PersistablePropertyNames.Contains(e.PropertyName))
            return;

        SchedulePersist();
    }

    private void SchedulePersist(bool outputSideEffects = false)
    {
        if (_suspendAutoSave)
            return;

        if (outputSideEffects)
            _pendingOutputSideEffects = true;

        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = DebouncedPersistAsync(token);
    }

    private async Task DebouncedPersistAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(PersistDebounceMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var dq = App.MainWindow?.DispatcherQueue;
        if (dq is null)
            return;

        dq.TryEnqueue(() => _ = PersistToStorageAsync());
    }

    private async Task PersistToStorageAsync()
    {
        var applyOutputEffects = _pendingOutputSideEffects;
        _pendingOutputSideEffects = false;

        try
        {
            CopyViewModelToSettings();
            await _settings.SaveAsync().ConfigureAwait(true);

            AppThemeHelper.ApplyToWindow(App.MainWindow, AppTheme);

            if (applyOutputEffects)
            {
                _show.NotifyAudienceOutputChanged();
                _show.NotifyStageOutputChanged();
                _show.SyncOutputWindowsAfterSettingsSave();
            }
        }
        catch (Exception ex)
        {
            _pendingOutputSideEffects |= applyOutputEffects;
            _logger.LogError(ex, "Failed to persist settings.");
        }
    }

    private void CopyViewModelToSettings()
    {
        _settings.Update(s =>
        {
            s.Output.AudienceMonitorIds = GetAudienceMonitorIdsForSave().ToList();
            s.Output.StageMonitorIds = GetStageMonitorIdsForSave().ToList();

            s.Show.DefaultCenterView = ShowDefaultCenterView;
            s.Show.ThumbnailSize = ShowThumbnailSize;
            s.Show.ShowSlideLabels = ShowSlideLabels;
            s.Show.AutoTakeOnDoubleClick = ShowAutoTakeOnDoubleClick;
            s.Show.MediaSeekSeconds = ShowMediaSeekSeconds <= 0 ? 5 : Math.Clamp(ShowMediaSeekSeconds, 1, 60);
            _show.MediaSeekSeconds = s.Show.MediaSeekSeconds;

            s.Editor.AutoSaveEnabled = EditorAutoSaveEnabled;
            s.Editor.AutoSaveOnCreate = EditorAutoSaveOnCreate;
            s.Editor.ShowGrid = EditorShowGrid;
            s.Editor.SnapToGrid = EditorSnapToGrid;
            s.Editor.GridSize = EditorGridSize;
            s.Editor.AutosaveInterval = EditorAutosaveInterval;

            s.Reflow.TextSize = ReflowTextSize;
            s.Reflow.PreviewDensity = ReflowPreviewDensity;
            s.Reflow.ShowSlideLabels = ReflowShowSlideLabels;

            s.Integrations.MusicManager.SupabaseUrl = string.IsNullOrWhiteSpace(MusicSupabaseUrl) ? null : MusicSupabaseUrl.Trim();
            s.Integrations.MusicManager.PublishableKey = string.IsNullOrWhiteSpace(MusicPublishableKey) ? null : MusicPublishableKey.Trim();
            s.Integrations.MusicManager.DefaultSongAction = MusicDefaultSongAction;
            s.Integrations.MusicManager.PreferSetImportView = MusicPreferSetImportView;

            s.Theme = AppTheme;
            s.MaxRecentFiles = MaxRecentFiles;
            s.ContentDir = IsUsingDefaultContentFolder ? null : ContentFolderPath;
        });
    }

    private void RefreshContentLocationProperties()
    {
        DefaultContentFolderPath = _content.GetDefaultDocumentsDataDirectory();
        ContentFolderPath = _content.GetDocumentsDataDirectory();
        IsUsingDefaultContentFolder = string.Equals(
            Path.GetFullPath(ContentFolderPath),
            Path.GetFullPath(DefaultContentFolderPath),
            StringComparison.OrdinalIgnoreCase);
        ContentMaintenanceLogPath = _contentMaintenanceLog.GetLogPath();
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
        BackgroundContentMaintenanceStatus = snapshot.StatusMessage;
        if (!IsContentMaintenanceBusy)
            ContentMaintenanceStatus = snapshot.StatusMessage;

        if (snapshot.Diagnostics != null)
            ApplyContentDiagnostics(snapshot.Diagnostics);
    }

    private void ApplyContentDiagnostics(ContentDiagnosticsSnapshot snapshot)
    {
        ContentDiagnostics.Clear();
        foreach (var diagnostic in snapshot.Diagnostics)
            ContentDiagnostics.Add(diagnostic);

        ContentDiagnosticsEmptyState = ContentDiagnostics.Count == 0
            ? "No content diagnostics reported."
            : string.Empty;
    }

    private void ApplyAuditResult(ContentAuditResult? audit)
    {
        AuditIssues.Clear();
        if (audit is null)
        {
            LastAuditSummary = "No audit has been run yet.";
            LastAuditAt = "Never";
            AuditIssuesEmptyState = "No audit has been run yet.";
            return;
        }

        foreach (var issue in audit.Issues)
            AuditIssues.Add(issue);
        AuditIssuesEmptyState = AuditIssues.Count == 0
            ? "No audit issues reported."
            : string.Empty;

        if (DateTimeOffset.TryParse(audit.AuditedAt, out var ts))
            LastAuditAt = ts.ToLocalTime().ToString("g");
        else
            LastAuditAt = audit.AuditedAt;

        LastAuditSummary = audit.Issues.Count == 0
            ? "No issues found"
            : $"{audit.Issues.Count} issue{(audit.Issues.Count == 1 ? string.Empty : "s")} detected";
    }

    private void RebuildHealthSummaryCards()
    {
        HealthSummaryCards.Clear();

        var contentRootIssues = HealthIssues
            .Where(IsLibraryHealthIssue)
            .ToList();
        var contentRootLevel = contentRootIssues.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)) ? "error"
            : contentRootIssues.Any(i => string.Equals(i.Severity, "warning", StringComparison.OrdinalIgnoreCase)) ? "warning"
            : "healthy";
        HealthSummaryCards.Add(new ContentHealthSummaryCard
        {
            Title = "Content Library",
            Status = contentRootLevel == "healthy" ? "Healthy" : contentRootLevel == "warning" ? "Warning" : "Error",
            Description = contentRootLevel == "healthy"
                ? $"Located at {ContentFolderPath}"
                : contentRootIssues.FirstOrDefault()?.Message ?? "Check content root settings.",
            StatusLevel = contentRootLevel,
        });

        HealthSummaryCards.Add(new ContentHealthSummaryCard
        {
            Title = "Last Audit",
            Status = LastAuditAt == "Never" ? "Not run" : LastAuditSummary.Contains("issue") ? "Issues found" : "Passed",
            Description = $"Ran {LastAuditAt} — {LastAuditSummary}",
            StatusLevel = LastAuditAt == "Never" ? "info"
                : LastAuditSummary.Contains("issue") ? "warning"
                : "healthy",
        });

        HealthSummaryCards.Add(new ContentHealthSummaryCard
        {
            Title = "Machine Settings",
            Status = OutputHealthStatus == "healthy" ? "Healthy" : OutputHealthStatus == "warning" ? "Warning" : "Error",
            Description = OutputHealthStatus == "healthy"
                ? "Output display and integrations are configured."
                : HealthIssues.FirstOrDefault(i => string.Equals(i.Area, "output", StringComparison.OrdinalIgnoreCase))?.Message
                  ?? "One or more machine settings need attention.",
            StatusLevel = OutputHealthStatus,
        });

        RecentChecks.Clear();
        foreach (var entry in ContentMaintenanceEntries.Take(3))
            RecentChecks.Add(entry);
    }

    private static bool IsLibraryHealthIssue(SettingsHealthIssue issue) =>
        string.Equals(issue.Area, "content-root", StringComparison.OrdinalIgnoreCase)
        || string.Equals(issue.Area, "libraryManagement", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<int> ParseMonitorIds(IEnumerable<string> monitorIds) =>
        monitorIds
            .Select(id => int.TryParse(id, out var index) ? index : -1)
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

    private void UpdateAudienceMonitorDetails(MonitorLayoutItem? monitor)
    {
        if (monitor == null)
        {
            HasAudienceSelectedMonitor = false;
            AudienceSelectedMonitorButtonTitle = "Choose a display";
            return;
        }

        HasAudienceSelectedMonitor = true;
        AudienceSelectedMonitorButtonTitle = $"Display {monitor.DisplayIndex} · {monitor.DisplayName}";
    }

    private void UpdateStageMonitorDetails(MonitorLayoutItem? monitor)
    {
        if (monitor == null)
        {
            HasStageSelectedMonitor = false;
            StageSelectedMonitorButtonTitle = "Choose a display";
            return;
        }

        HasStageSelectedMonitor = true;
        StageSelectedMonitorButtonTitle = $"Display {monitor.DisplayIndex} · {monitor.DisplayName}";
    }
}

/// <summary>Editable per-screen route row for Settings output Looks.</summary>
public sealed partial class OutputLookScreenRouteSettingsItem : ObservableObject
{
    /// <summary>Stable audience feed id.</summary>
    public string FeedId { get; init; } = string.Empty;

    /// <summary>Display name shown in Settings.</summary>
    public string DisplayName { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _slideEnabled = true;

    [ObservableProperty]
    private bool _mediaEnabled = true;

    [ObservableProperty]
    private bool _maskEnabled = true;

    [ObservableProperty]
    private string _slideThemeVariantId = string.Empty;

    [ObservableProperty]
    private string _maskId = string.Empty;

    /// <summary>Creates a persisted route definition from the editable row.</summary>
    public OutputLookFeedRouting ToDefinition()
    {
        string? themeVariantId = string.IsNullOrWhiteSpace(SlideThemeVariantId)
            ? null
            : SlideThemeVariantId.Trim();
        string? maskId = string.IsNullOrWhiteSpace(MaskId)
            ? null
            : MaskId.Trim();

        OutputLookFeedRouting route = new()
        {
            FeedId = FeedId,
            Slide = SlideEnabled,
            Media = MediaEnabled,
            Layers =
            [
                CreateRoute(OutputLayerKind.Slide, SlideEnabled, themeVariantId: themeVariantId),
                CreateRoute(OutputLayerKind.Media, MediaEnabled),
                CreateRoute(OutputLayerKind.Audio, MediaEnabled),
                CreateRoute(OutputLayerKind.Mask, MaskEnabled, maskId: maskId),
            ],
        };
        OutputRoutingDefaults.EnsureLayerRoutes(route);
        return route;
    }

    private static OutputLayerRouteDefinition CreateRoute(
        OutputLayerKind layerKind,
        bool enabled,
        string? themeVariantId = null,
        string? maskId = null) =>
        new()
        {
            Layer = OutputRoutingDefaults.GetLayerId(layerKind),
            Enabled = enabled,
            ThemeVariantId = themeVariantId,
            MaskId = maskId,
        };
}

/// <summary>Editable clear group row for Settings.</summary>
public sealed partial class ClearGroupSettingsItem : ObservableObject
{
    /// <summary>Stable clear group id.</summary>
    public string Id { get; init; } = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "\uE894";

    [ObservableProperty]
    private bool _tintEnabled;

    [ObservableProperty]
    private string _tintColor = "#C42B1C";

    [ObservableProperty]
    private bool _stopPresentationTimeline;

    [ObservableProperty]
    private bool _stopAnnouncementTimeline;

    /// <summary>Editable scope checklist.</summary>
    public ObservableCollection<ClearScopeSettingsItem> Scopes { get; } = new();

    /// <summary>Creates an editable settings item from a persisted definition.</summary>
    public static ClearGroupSettingsItem FromDefinition(OutputLookClearGroupDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        HashSet<OutputClearScope> selectedScopes = definition.Scopes.Count > 0
            ? definition.Scopes.ToHashSet()
            : InferScopesFromLayers(definition.Layers);

        var item = new ClearGroupSettingsItem
        {
            Id = string.IsNullOrWhiteSpace(definition.Id) ? $"clear-{Guid.NewGuid():N}" : definition.Id,
            Name = string.IsNullOrWhiteSpace(definition.Name) ? "Clear Group" : definition.Name,
            Icon = string.IsNullOrWhiteSpace(definition.Icon) ? "\uE894" : definition.Icon,
            TintEnabled = definition.TintEnabled,
            TintColor = string.IsNullOrWhiteSpace(definition.TintColor) ? "#C42B1C" : definition.TintColor!,
            StopPresentationTimeline = definition.StopPresentationTimeline,
            StopAnnouncementTimeline = definition.StopAnnouncementTimeline,
        };

        foreach (OutputClearScope scope in Enum.GetValues<OutputClearScope>())
        {
            item.Scopes.Add(new ClearScopeSettingsItem
            {
                Scope = scope,
                DisplayName = ToDisplayName(scope),
                IsSelected = selectedScopes.Contains(scope),
            });
        }

        return item;
    }

    /// <summary>Creates a persisted definition from the editable item.</summary>
    public OutputLookClearGroupDefinition ToDefinition()
    {
        OutputClearScope[] selectedScopes = Scopes
            .Where(static scope => scope.IsSelected)
            .Select(static scope => scope.Scope)
            .ToArray();

        return new OutputLookClearGroupDefinition
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "Clear Group" : Name.Trim(),
            Icon = string.IsNullOrWhiteSpace(Icon) ? "\uE894" : Icon.Trim(),
            TintEnabled = TintEnabled,
            TintColor = TintEnabled && !string.IsNullOrWhiteSpace(TintColor) ? TintColor.Trim() : null,
            Scopes = selectedScopes.ToList(),
            StopPresentationTimeline = StopPresentationTimeline,
            StopAnnouncementTimeline = StopAnnouncementTimeline,
            Layers = OutputRoutingDefaults.CreateClearGroupLayers(selectedScopes),
        };
    }

    private static HashSet<OutputClearScope> InferScopesFromLayers(IEnumerable<string> layers)
    {
        HashSet<OutputLayerKind> layerKinds = layers
            .Select(layer => OutputRoutingDefaults.TryParseLayerKind(layer, out OutputLayerKind parsed) ? parsed : (OutputLayerKind?)null)
            .Where(static layer => layer.HasValue)
            .Select(static layer => layer!.Value)
            .ToHashSet();

        return Enum.GetValues<OutputClearScope>()
            .Where(scope => OutputRoutingDefaults.ExpandClearScope(scope).Any(layerKinds.Contains))
            .ToHashSet();
    }

    private static string ToDisplayName(OutputClearScope scope) =>
        scope switch
        {
            OutputClearScope.AudioEffects => "Audio Effects",
            OutputClearScope.PresentationMedia => "Presentation Media",
            OutputClearScope.VideoInput => "Video Input",
            _ => scope.ToString(),
        };
}

/// <summary>Editable clear scope checkbox row for Settings.</summary>
public sealed partial class ClearScopeSettingsItem : ObservableObject
{
    /// <summary>Scope represented by this row.</summary>
    public OutputClearScope Scope { get; init; }

    /// <summary>Operator-facing row label.</summary>
    public string DisplayName { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether this scope exposes timeline stop options.</summary>
    public bool HasTimelineOption => Scope is OutputClearScope.Presentation or OutputClearScope.Announcements;
}