using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Loads and persists the library/playlist catalog.
/// The canonical source of truth is the structured registry (Libraries/ + Playlists/ folders with
/// Index.json and per-item manifests). Legacy aggregate files (libraries.json / playlists.json) are
/// read during a one-time migration and never written again once the registry exists.
/// </summary>
public sealed partial class CatalogService(
    IContentDirectoryService paths,
    ILibraryRegistryService libraryRegistry,
    IPlaylistRegistryService playlistRegistry,
    ICpresDocumentService cpres,
    IContentMaintenanceLogService maintenanceLog,
    ILogger<CatalogService> logger,
    IContentChangeBus? contentChanges = null) : ICatalogService
{
    private const string AutoLibraryId = "local-library";
    private const string AutoLibraryName = "Library";

    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILibraryRegistryService _libraryRegistry = libraryRegistry ?? throw new ArgumentNullException(nameof(libraryRegistry));
    private readonly IPlaylistRegistryService _playlistRegistry = playlistRegistry ?? throw new ArgumentNullException(nameof(playlistRegistry));
    private readonly ICpresDocumentService _cpres = cpres ?? throw new ArgumentNullException(nameof(cpres));
    private readonly IContentMaintenanceLogService _maintenanceLog = maintenanceLog ?? throw new ArgumentNullException(nameof(maintenanceLog));
    private readonly ILogger<CatalogService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IContentChangeBus? _contentChanges = contentChanges;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public CatalogDto Catalog { get; private set; } = new();

    // Set synchronously (before the first await) when a Startup-trigger scan begins so that a
    // second Startup call — e.g. from ShellViewModel after ShowViewModel has already kicked off
    // the scan — returns immediately rather than running the full filesystem scan twice.
    private bool _startupLoadComplete;

    /// <inheritdoc />
    public async Task LoadAsync(ContentMaintenanceTrigger trigger = ContentMaintenanceTrigger.Default)
    {
        var guardedStartupLoad = false;
        if (trigger == ContentMaintenanceTrigger.Startup)
        {
            if (_startupLoadComplete)
                return;
            // Guard must be set before the first await so a second synchronous caller
            // that reaches this method before any async continuation fires sees the flag.
            _startupLoadComplete = true;
            guardedStartupLoad = true;
        }

        try
        {
            var maintenance = new MaintenanceRunState(trigger);

            await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);
            await RepairContentLayoutAsync(maintenance).ConfigureAwait(false);

            var discoveredPresentations = DiscoverPresentations();
            var libraries = CoalesceLibraries(await LoadLibrariesAsync(maintenance).ConfigureAwait(false), maintenance);
            var playlists = CoalescePlaylists(await LoadPlaylistsAsync(maintenance).ConfigureAwait(false), maintenance);

            NormalizeLibraries(libraries, discoveredPresentations);
            NormalizePlaylists(playlists, discoveredPresentations);
            EnsureAutoLibraryForUnassignedPresentations(libraries, discoveredPresentations, maintenance);

            Catalog = new CatalogDto
            {
                Libraries = libraries,
                Playlists = playlists,
            };

            await PersistToRegistriesAsync(Catalog).ConfigureAwait(false);
            await FlushMaintenanceRunAsync(maintenance, discoveredPresentations.Count).ConfigureAwait(false);

            _logger.LogInformation(
                "Catalog loaded with {LibraryCount} libraries, {PlaylistCount} playlists, and {PresentationCount} presentations.",
                Catalog.Libraries.Count,
                Catalog.Playlists.Count,
                discoveredPresentations.Count);
            _contentChanges?.Publish(new ContentChangeEvent
            {
                Kind = ContentChangeKind.CatalogRefreshed,
                SubjectId = _paths.GetDocumentsDataDirectory(),
                Source = nameof(CatalogService),
            });
        }
        catch
        {
            if (guardedStartupLoad)
                _startupLoadComplete = false;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);
        await PersistToRegistriesAsync(Catalog).ConfigureAwait(false);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.CatalogRefreshed,
            SubjectId = _paths.GetDocumentsDataDirectory(),
            Source = nameof(CatalogService),
        });
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private async Task<List<LibraryDto>> LoadLibrariesAsync(MaintenanceRunState maintenance)
    {
        // Primary: structured registry
        if (_libraryRegistry.RegistryExists())
        {
            var manifests = await _libraryRegistry.LoadAllAsync().ConfigureAwait(false);
            return manifests
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(ManifestToLibraryDto)
                .ToList();
        }

        maintenance.Record(
            "warning",
            "library-registry-missing",
            "Canonical library registry is missing. Run startup migration or reset the content root.",
            _paths.GetLibrariesIndexPath());
        return new List<LibraryDto>();
    }

    private async Task<List<PlaylistDto>> LoadPlaylistsAsync(MaintenanceRunState maintenance)
    {
        // Primary: structured registry
        if (_playlistRegistry.RegistryExists())
        {
            var manifests = await _playlistRegistry.LoadAllAsync().ConfigureAwait(false);
            return manifests
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(ManifestToPlaylistDto)
                .ToList();
        }

        maintenance.Record(
            "warning",
            "playlist-registry-missing",
            "Canonical playlist registry is missing. Run startup migration or reset the content root.",
            _paths.GetPlaylistsIndexPath());
        return new List<PlaylistDto>();
    }

    private static LibraryDto ManifestToLibraryDto(LibraryManifest m) =>
        new()
        {
            Id = m.Id,
            Name = m.Name,
            Description = m.Description,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            DefaultFolder = m.DefaultFolder,
            Presentations = m.Presentations.Select(ClonePresentationRef).ToList(),
        };

    private static PlaylistDto ManifestToPlaylistDto(PlaylistManifest m) =>
        new()
        {
            Id = m.Id,
            Name = m.Name,
            Description = m.Description,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            Items = m.Items.Select(ClonePresentationRef).ToList(),
            ExternalSet = m.ExternalSet == null ? null : CloneExternalSet(m.ExternalSet),
            Sync = m.Sync == null ? null : CloneSyncMetadata(m.Sync),
        };

}