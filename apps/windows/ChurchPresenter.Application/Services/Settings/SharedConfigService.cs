using System.Text.Json;
using System.Text.Json.Serialization;

using ChurchPresenter.Backend.Stage;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Settings;

/// <inheritdoc />
public sealed class SharedConfigService : ISharedConfigService
{
    private readonly IContentDirectoryService _paths;
    private readonly IContentStore _contentStore;
    private readonly ILogger<SharedConfigService> _logger;
    private readonly IContentChangeBus? _contentChanges;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates the shared-config service with the shared content store abstraction.
    /// </summary>
    public SharedConfigService(
        IContentDirectoryService paths,
        IContentStore contentStore,
        ILogger<SharedConfigService> logger,
        IContentChangeBus? contentChanges = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contentChanges = contentChanges;
    }

    /// <summary>
    /// Creates the shared-config service with the default file-system content store.
    /// </summary>
    public SharedConfigService(
        IContentDirectoryService paths,
        ILogger<SharedConfigService> logger)
        : this(paths, ContentStoreDefaults.Instance, logger)
    {
    }

    /// <inheritdoc />
    public OutputConfig Output { get; private set; } = new();

    /// <inheritdoc />
    public ShowConfig Show { get; private set; } = new();

    /// <inheritdoc />
    public StageConfig Stage { get; private set; } = new();

    /// <inheritdoc />
    public EditorConfig Editor { get; private set; } = new();

    /// <inheritdoc />
    public ReflowConfig Reflow { get; private set; } = new();

    /// <inheritdoc />
    public IntegrationsConfig Integrations { get; private set; } = new();

    /// <inheritdoc />
    public AppearanceConfig Appearance { get; private set; } = new();

    /// <inheritdoc />
    public LibraryManagementConfig LibraryManagement { get; private set; } = new();

    /// <inheritdoc />
    public SupportConfig Support { get; private set; } = new();

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Output = await LoadConfigAsync<OutputConfig>("Output", cancellationToken).ConfigureAwait(false);
        Show = await LoadConfigAsync<ShowConfig>("Show", cancellationToken).ConfigureAwait(false);
        Stage = await LoadConfigAsync<StageConfig>("Stage", cancellationToken).ConfigureAwait(false);
        Editor = await LoadConfigAsync<EditorConfig>("Editor", cancellationToken).ConfigureAwait(false);
        Reflow = await LoadConfigAsync<ReflowConfig>("Reflow", cancellationToken).ConfigureAwait(false);
        Integrations = await LoadConfigAsync<IntegrationsConfig>("Integrations", cancellationToken).ConfigureAwait(false);
        Appearance = await LoadConfigAsync<AppearanceConfig>("Appearance", cancellationToken).ConfigureAwait(false);
        LibraryManagement = await LoadConfigAsync<LibraryManagementConfig>("LibraryManagement", cancellationToken).ConfigureAwait(false);
        Support = await LoadConfigAsync<SupportConfig>("Support", cancellationToken).ConfigureAwait(false);

        Normalize();
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var configDir = _paths.GetConfigurationsDirectory();
        _contentStore.EnsureDirectory(configDir);

        await SaveConfigAsync("Output", Output, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Show", Show, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Stage", Stage, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Editor", Editor, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Reflow", Reflow, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Integrations", Integrations, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Appearance", Appearance, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("LibraryManagement", LibraryManagement, cancellationToken).ConfigureAwait(false);
        await SaveConfigAsync("Support", Support, cancellationToken).ConfigureAwait(false);

        await EnsureConfigurationsManifestAsync(cancellationToken).ConfigureAwait(false);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.SharedConfigChanged,
            SubjectId = _paths.GetConfigurationsDirectory(),
            Source = nameof(SharedConfigService),
        });
    }

    /// <inheritdoc />
    public void UpdateOutput(Action<OutputConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Output); }

    /// <inheritdoc />
    public void UpdateShow(Action<ShowConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Show); }

    /// <inheritdoc />
    public void UpdateStage(Action<StageConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Stage); }

    /// <inheritdoc />
    public void UpdateEditor(Action<EditorConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Editor); }

    /// <inheritdoc />
    public void UpdateReflow(Action<ReflowConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Reflow); }

    /// <inheritdoc />
    public void UpdateIntegrations(Action<IntegrationsConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Integrations); }

    /// <inheritdoc />
    public void UpdateAppearance(Action<AppearanceConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Appearance); }

    /// <inheritdoc />
    public void UpdateLibraryManagement(Action<LibraryManagementConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(LibraryManagement); }

    /// <inheritdoc />
    public void UpdateSupport(Action<SupportConfig> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Support); }

    private async Task<T> LoadConfigAsync<T>(string name, CancellationToken cancellationToken) where T : class, new()
    {
        var path = _paths.GetSharedConfigPath(name);
        return await _contentStore.ReadJsonAsync<T>(path, JsonOptions, cancellationToken).ConfigureAwait(false) ?? new T();
    }

    private async Task SaveConfigAsync<T>(string name, T config, CancellationToken cancellationToken)
    {
        var path = _paths.GetSharedConfigPath(name);
        try
        {
            await _contentStore.WriteJsonAsync(path, config, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save shared config {Name}.json.", name);
        }
    }

    private async Task EnsureConfigurationsManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = _paths.GetConfigurationsManifestPath();
        if (_contentStore.FileExists(manifestPath))
            return;

        var manifest = new ConfigurationsManifest
        {
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        try
        {
            await _contentStore.WriteJsonAsync(manifestPath, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write Configurations/Manifest.json.");
        }
    }

    private void Normalize()
    {
        Output.Looks ??= new List<OutputLookDefinition>();
        Output.LogicalScreens ??= new List<LogicalScreenDefinition>();
        Output.Masks ??= new List<OutputMaskDefinition>();
        Show.Timers ??= new List<ShowTimerDefinition>();
        Show.Messages ??= new List<ShowMessageDefinition>();
        Show.Props ??= new List<ShowPropDefinition>();
        Show.Macros ??= new List<ShowMacroDefinition>();
        Show.AudioPlaylists ??= new List<ShowAudioPlaylistDefinition>();
        foreach (ShowMessageDefinition message in Show.Messages)
        {
            message.Tokens ??= new List<ShowMessageTokenDefinition>();
            message.Dismiss ??= new ShowMessageDismissDefinition();
        }

        foreach (ShowMacroDefinition macro in Show.Macros)
        {
            macro.CommandIds ??= new List<string>();
            macro.Commands ??= new List<ShowMacroCommandDefinition>();
        }

        foreach (ShowAudioPlaylistDefinition playlist in Show.AudioPlaylists)
        {
            playlist.ItemIds ??= new List<string>();
            if (playlist.TransitionSeconds < 0)
                playlist.TransitionSeconds = 0;
        }

        Show.FavoriteTransitions ??= new List<string>();
        Show.RecentTransitions ??= new List<string>();
        Show.RecentThemeIds ??= new List<string>();
        Show.GlobalSlideTransition ??= new ShowToolbarTransitionDto();
        Show.GlobalMediaTransition ??= new ShowToolbarTransitionDto();
        Stage.Layouts ??= new List<StageLayout>();
        Stage.DefaultLayoutIdsByScreenId ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Integrations.MusicManager ??= new MusicManagerIntegrationDto();
        Support.ThemeBindings ??= new List<ThemeBindingDefinition>();
        Support.Labels ??= new List<SupportLabelDefinition>();
        Support.Notes ??= new List<string>();
        if (string.IsNullOrWhiteSpace(Show.TransparentThumbnailColor))
            Show.TransparentThumbnailColor = "#000000";
        Show.TransparentThumbnailOpacity = Math.Clamp(Show.TransparentThumbnailOpacity, 0, 100);
        Show.DeckScaleStep = Math.Clamp(Show.DeckScaleStep, 0, 4);
        if (Show.SchemaVersion < 2)
        {
            // Older Show.json omitted mediaPanelScaleStep; JSON deserializes missing ints as 0.
            if (Show.MediaPanelScaleStep == 0)
                Show.MediaPanelScaleStep = 4;
        }

        Show.MediaPanelScaleStep = Math.Clamp(Show.MediaPanelScaleStep, 0, 7);
        Show.MediaSeekSeconds = Show.MediaSeekSeconds <= 0 ? 5 : Math.Clamp(Show.MediaSeekSeconds, 1, 60);
        NormalizeToolbarTransition(Show.GlobalSlideTransition);
        NormalizeToolbarTransition(Show.GlobalMediaTransition);
        if (Show.SchemaVersion < 3)
            Show.SchemaVersion = 3;
        if (Show.DeckViewMode is not ("thumbnail" or "text" or "list"))
            Show.DeckViewMode = "thumbnail";
    }

    private static void NormalizeToolbarTransition(ShowToolbarTransitionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        dto.Mode = string.IsNullOrWhiteSpace(dto.Mode) ? string.Empty : dto.Mode.Trim().ToLowerInvariant();
        dto.DissolveDurationMs = dto.DissolveDurationMs <= 0 ? 200 : Math.Clamp(dto.DissolveDurationMs, 50, 10_000);
        dto.Custom = TransitionStorageNormalizer.NormalizeForStorage(dto.Custom);
    }
}