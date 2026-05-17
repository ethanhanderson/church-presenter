using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Settings;

/// <summary>
/// Loads and saves application settings.
///
/// Storage layout:
/// <list type="bullet">
///   <item>Portable per-tab settings → <c>Configurations/</c> via <see cref="ISharedConfigService"/>.</item>
///   <item>Machine-local bindings (monitor, recent files) → <c>MachineState/</c> via <see cref="IMachineStateService"/>.</item>
///   <item>Legacy monolithic <c>settings.json</c> is read once on first run and promoted to the split layout.</item>
/// </list>
///
/// <see cref="AppSettingsDto"/> remains the in-memory aggregate model used by all consumers; this
/// service assembles and disassembles it from the split files transparently.
/// </summary>
public sealed class SettingsService(
    IContentDirectoryService paths,
    ISharedConfigService sharedConfig,
    IMachineStateService machineState,
    ILogger<SettingsService> logger) : ISettingsService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ISharedConfigService _sharedConfig = sharedConfig ?? throw new ArgumentNullException(nameof(sharedConfig));
    private readonly IMachineStateService _machineState = machineState ?? throw new ArgumentNullException(nameof(machineState));
    private readonly ILogger<SettingsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private const string LegacyFileName = "settings.json";

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <inheritdoc />
    public AppSettingsDto Settings { get; private set; } = new();

    /// <inheritdoc />
    public async Task LoadAsync()
    {
        await _machineState.LoadAsync().ConfigureAwait(false);
        await _sharedConfig.LoadAsync().ConfigureAwait(false);

        // If no portable configs exist yet, try to promote from legacy settings.json
        var configsExist = File.Exists(_paths.GetSharedConfigPath("Show"))
                           || File.Exists(_paths.GetSharedConfigPath("Editor"));
        if (!configsExist)
            await TryPromoteLegacySettingsAsync().ConfigureAwait(false);

        Settings = Assemble();
        _logger.LogInformation("Settings loaded from split config/machine-state layout.");
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        Disassemble(Settings);
        await _sharedConfig.SaveAsync().ConfigureAwait(false);
        await _machineState.SaveAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Update(Action<AppSettingsDto> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        mutator(Settings);
        // Keep split services in sync with the in-memory aggregate
        Disassemble(Settings);
    }

    // ── Assembly / Disassembly ────────────────────────────────────────────────

    private AppSettingsDto Assemble() =>
        new()
        {
            Output = new OutputSettingsDto
            {
                AudienceMonitorIds = new List<string>(_machineState.OutputBinding.AudienceMonitorIds),
                StageMonitorIds = new List<string>(_machineState.OutputBinding.StageMonitorIds),
            },
            Show = new ShowSettingsDto
            {
                DefaultCenterView = _sharedConfig.Show.DefaultCenterView,
                ThumbnailSize = _sharedConfig.Show.ThumbnailSize,
                ShowSlideLabels = _sharedConfig.Show.ShowSlideLabels,
                AutoTakeOnDoubleClick = _sharedConfig.Show.AutoTakeOnDoubleClick,
                DeckViewMode = _sharedConfig.Show.DeckViewMode,
                GroupBySection = _sharedConfig.Show.GroupBySection,
                TransparentThumbnailBackgroundEnabled = _sharedConfig.Show.TransparentThumbnailBackgroundEnabled,
                TransparentThumbnailColor = _sharedConfig.Show.TransparentThumbnailColor,
                TransparentThumbnailOpacity = _sharedConfig.Show.TransparentThumbnailOpacity,
                DeckScaleStep = _sharedConfig.Show.DeckScaleStep,
                MediaPanelScaleStep = _sharedConfig.Show.MediaPanelScaleStep,
                MediaSeekSeconds = _sharedConfig.Show.MediaSeekSeconds,
                Timers = new List<ShowTimerDefinition>(_sharedConfig.Show.Timers),
                FavoriteTransitions = new List<string>(_sharedConfig.Show.FavoriteTransitions),
                RecentTransitions = new List<string>(_sharedConfig.Show.RecentTransitions),
                RecentThemeIds = new List<string>(_sharedConfig.Show.RecentThemeIds),
                ApplyMediaActionsWithThemeSlide = _sharedConfig.Show.ApplyMediaActionsWithThemeSlide,
                GlobalSlideTransition = CloneToolbarTransition(_sharedConfig.Show.GlobalSlideTransition),
                GlobalMediaTransition = CloneToolbarTransition(_sharedConfig.Show.GlobalMediaTransition),
            },
            Editor = new EditorSettingsDto
            {
                AutosaveInterval = _sharedConfig.Editor.AutosaveInterval,
                AutoSaveEnabled = _sharedConfig.Editor.AutoSaveEnabled,
                AutoSaveOnCreate = _sharedConfig.Editor.AutoSaveOnCreate,
                ShowGrid = _sharedConfig.Editor.ShowGrid,
                SnapToGrid = _sharedConfig.Editor.SnapToGrid,
                GridSize = _sharedConfig.Editor.GridSize,
            },
            Reflow = new ReflowSettingsDto
            {
                TextSize = _sharedConfig.Reflow.TextSize,
                PreviewDensity = _sharedConfig.Reflow.PreviewDensity,
                ShowSlideLabels = _sharedConfig.Reflow.ShowSlideLabels,
            },
            Integrations = new IntegrationsSettingsDto
            {
                MusicManager = new MusicManagerIntegrationDto
                {
                    SupabaseUrl = _sharedConfig.Integrations.MusicManager.SupabaseUrl,
                    PublishableKey = _sharedConfig.Integrations.MusicManager.PublishableKey,
                    DefaultSongAction = _sharedConfig.Integrations.MusicManager.DefaultSongAction,
                    PreferSetImportView = _sharedConfig.Integrations.MusicManager.PreferSetImportView,
                },
            },
            Theme = _sharedConfig.Appearance.Theme,
            MaxRecentFiles = _sharedConfig.Appearance.MaxRecentFiles,
            RecentFiles = new List<PresentationRefDto>(_machineState.RecentFiles.Entries),
            Updates = new UpdatesSettingsDto
            {
                AutoCheck = _machineState.Updates.AutoCheck,
                LastCheckedAt = _machineState.Updates.LastCheckedAt,
            },
        };

    private void Disassemble(AppSettingsDto s)
    {
        _machineState.UpdateOutputBinding(ob =>
        {
            ob.AudienceMonitorIds = new List<string>(s.Output?.AudienceMonitorIds ?? new List<string>());
            ob.StageMonitorIds = new List<string>(s.Output?.StageMonitorIds ?? new List<string>());
        });

        _sharedConfig.UpdateShow(sc =>
        {
            sc.DefaultCenterView = s.Show?.DefaultCenterView ?? sc.DefaultCenterView;
            sc.ThumbnailSize = s.Show?.ThumbnailSize ?? sc.ThumbnailSize;
            sc.ShowSlideLabels = s.Show?.ShowSlideLabels ?? sc.ShowSlideLabels;
            sc.AutoTakeOnDoubleClick = s.Show?.AutoTakeOnDoubleClick ?? sc.AutoTakeOnDoubleClick;
            sc.DeckViewMode = s.Show?.DeckViewMode ?? sc.DeckViewMode;
            sc.GroupBySection = s.Show?.GroupBySection ?? sc.GroupBySection;
            sc.TransparentThumbnailBackgroundEnabled = s.Show?.TransparentThumbnailBackgroundEnabled ?? sc.TransparentThumbnailBackgroundEnabled;
            if (s.Show != null)
            {
                sc.TransparentThumbnailColor = string.IsNullOrWhiteSpace(s.Show.TransparentThumbnailColor)
                    ? "#000000"
                    : s.Show.TransparentThumbnailColor;
                sc.TransparentThumbnailOpacity = Math.Clamp(s.Show.TransparentThumbnailOpacity, 0, 100);
                sc.DeckScaleStep = Math.Clamp(s.Show.DeckScaleStep, 0, 4);
                sc.MediaPanelScaleStep = Math.Clamp(s.Show.MediaPanelScaleStep, 0, 7);
                sc.MediaSeekSeconds = s.Show.MediaSeekSeconds <= 0 ? 5 : Math.Clamp(s.Show.MediaSeekSeconds, 1, 60);
            }
            if (s.Show?.Timers != null)
                sc.Timers = new List<ShowTimerDefinition>(s.Show.Timers);
            if (s.Show?.FavoriteTransitions != null)
                sc.FavoriteTransitions = new List<string>(s.Show.FavoriteTransitions);
            if (s.Show?.RecentTransitions != null)
                sc.RecentTransitions = new List<string>(s.Show.RecentTransitions);
            if (s.Show?.RecentThemeIds != null)
                sc.RecentThemeIds = new List<string>(s.Show.RecentThemeIds);
            if (s.Show != null)
                sc.ApplyMediaActionsWithThemeSlide = s.Show.ApplyMediaActionsWithThemeSlide;
            sc.GlobalSlideTransition = CloneToolbarTransition(s.Show?.GlobalSlideTransition);
            sc.GlobalMediaTransition = CloneToolbarTransition(s.Show?.GlobalMediaTransition);
        });

        _sharedConfig.UpdateEditor(ec =>
        {
            ec.AutosaveInterval = s.Editor?.AutosaveInterval ?? ec.AutosaveInterval;
            ec.AutoSaveEnabled = s.Editor?.AutoSaveEnabled ?? ec.AutoSaveEnabled;
            ec.AutoSaveOnCreate = s.Editor?.AutoSaveOnCreate ?? ec.AutoSaveOnCreate;
            ec.ShowGrid = s.Editor?.ShowGrid ?? ec.ShowGrid;
            ec.SnapToGrid = s.Editor?.SnapToGrid ?? ec.SnapToGrid;
            ec.GridSize = s.Editor?.GridSize ?? ec.GridSize;
        });

        _sharedConfig.UpdateReflow(rc =>
        {
            rc.TextSize = s.Reflow?.TextSize ?? rc.TextSize;
            rc.PreviewDensity = s.Reflow?.PreviewDensity ?? rc.PreviewDensity;
            rc.ShowSlideLabels = s.Reflow?.ShowSlideLabels ?? rc.ShowSlideLabels;
        });

        _sharedConfig.UpdateIntegrations(ic =>
        {
            if (s.Integrations?.MusicManager != null)
            {
                ic.MusicManager.SupabaseUrl = s.Integrations.MusicManager.SupabaseUrl;
                ic.MusicManager.PublishableKey = s.Integrations.MusicManager.PublishableKey;
                ic.MusicManager.DefaultSongAction = s.Integrations.MusicManager.DefaultSongAction;
                ic.MusicManager.PreferSetImportView = s.Integrations.MusicManager.PreferSetImportView;
            }
        });

        _sharedConfig.UpdateAppearance(ac =>
        {
            ac.Theme = s.Theme ?? ac.Theme;
            ac.MaxRecentFiles = s.MaxRecentFiles > 0 ? s.MaxRecentFiles : ac.MaxRecentFiles;
        });

        _machineState.UpdateRecentFiles(rf =>
        {
            if (s.RecentFiles != null)
                rf.Entries = new List<PresentationRefDto>(s.RecentFiles);
        });

        _machineState.UpdateUpdates(u =>
        {
            u.AutoCheck = s.Updates?.AutoCheck ?? u.AutoCheck;
            u.LastCheckedAt = s.Updates?.LastCheckedAt ?? u.LastCheckedAt;
        });
    }

    // ── Legacy promotion ──────────────────────────────────────────────────────

    private async Task TryPromoteLegacySettingsAsync()
    {
        var legacyPath = Path.Combine(_paths.GetAppDataDirectory(), LegacyFileName);
        if (!File.Exists(legacyPath))
            return;

        try
        {
            await using var fs = File.OpenRead(legacyPath);
            var legacy = await JsonSerializer.DeserializeAsync<AppSettingsDto>(fs, LegacyJsonOptions).ConfigureAwait(false);
            if (legacy == null)
                return;

            legacy = NormalizeLegacy(legacy);

            // Push legacy values into the split services so they get persisted on next save
            Disassemble(legacy);
            await _sharedConfig.SaveAsync().ConfigureAwait(false);
            await _machineState.SaveAsync().ConfigureAwait(false);

            _logger.LogInformation("Promoted legacy settings.json to split configuration layout.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not promote legacy {File}; continuing with defaults.", LegacyFileName);
        }
    }

    private static AppSettingsDto NormalizeLegacy(AppSettingsDto s)
    {
        s.Output ??= new OutputSettingsDto();
        s.Output.AudienceMonitorIds ??= new List<string>();
        s.Output.StageMonitorIds ??= new List<string>();
        // Migrate legacy monitorIds → audienceMonitorIds (one-way promotion).
        if (s.Output.AudienceMonitorIds.Count == 0 && s.Output.LegacyMonitorIds?.Count > 0)
        {
            s.Output.AudienceMonitorIds = new List<string>(s.Output.LegacyMonitorIds);
            s.Output.LegacyMonitorIds = null;
        }
        s.Editor ??= new EditorSettingsDto();
        s.Show ??= new ShowSettingsDto();
        s.Show.Timers ??= new List<ShowTimerDefinition>();
        s.Reflow ??= new ReflowSettingsDto();
        s.Integrations ??= new IntegrationsSettingsDto();
        s.Integrations.MusicManager ??= new MusicManagerIntegrationDto();
        s.RecentFiles ??= new List<PresentationRefDto>();
        s.Updates ??= new UpdatesSettingsDto();
        return s;
    }

    private static ShowToolbarTransitionDto CloneToolbarTransition(ShowToolbarTransitionDto? source)
    {
        source ??= new ShowToolbarTransitionDto();
        return new ShowToolbarTransitionDto
        {
            Mode = string.IsNullOrWhiteSpace(source.Mode) ? string.Empty : source.Mode,
            DissolveDurationMs = source.DissolveDurationMs <= 0 ? 200 : source.DissolveDurationMs,
            Custom = source.Custom == null ? null : PresentationModelUtilities.DeepClone(source.Custom),
        };
    }
}