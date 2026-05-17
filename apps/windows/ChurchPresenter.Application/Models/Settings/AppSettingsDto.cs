using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Settings;

public sealed class AppSettingsDto
{
    [JsonPropertyName("output")]
    public OutputSettingsDto Output { get; set; } = new();

    [JsonPropertyName("editor")]
    public EditorSettingsDto Editor { get; set; } = new();

    [JsonPropertyName("show")]
    public ShowSettingsDto Show { get; set; } = new();

    [JsonPropertyName("reflow")]
    public ReflowSettingsDto Reflow { get; set; } = new();

    [JsonPropertyName("integrations")]
    public IntegrationsSettingsDto Integrations { get; set; } = new();

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("recentFiles")]
    public List<PresentationRefDto> RecentFiles { get; set; } = new();

    [JsonPropertyName("maxRecentFiles")]
    public int MaxRecentFiles { get; set; } = 10;

    [JsonPropertyName("contentDir")]
    public string? ContentDir { get; set; }

    [JsonPropertyName("mediaLibraryDir")]
    public string? MediaLibraryDir { get; set; }

    [JsonPropertyName("updates")]
    public UpdatesSettingsDto Updates { get; set; } = new();
}

public sealed class OutputSettingsDto
{
    /// <summary>
    /// Legacy field preserved for migration. On load, the value is promoted to
    /// <see cref="AudienceMonitorIds"/> when that list is empty.
    /// </summary>
    [JsonPropertyName("monitorIds")]
    public List<string>? LegacyMonitorIds { get; set; }

    /// <summary>Machine-local monitor index strings routed to audience (fullscreen) output.</summary>
    [JsonPropertyName("audienceMonitorIds")]
    public List<string> AudienceMonitorIds { get; set; } = new();

    /// <summary>Machine-local monitor index strings routed to stage (operator) output.</summary>
    [JsonPropertyName("stageMonitorIds")]
    public List<string> StageMonitorIds { get; set; } = new();
}

public sealed class EditorSettingsDto
{
    [JsonPropertyName("autosaveInterval")]
    public int AutosaveInterval { get; set; } = 30;

    [JsonPropertyName("autoSaveEnabled")]
    public bool AutoSaveEnabled { get; set; } = true;

    [JsonPropertyName("autoSaveOnCreate")]
    public bool AutoSaveOnCreate { get; set; } = true;

    [JsonPropertyName("showGrid")]
    public bool ShowGrid { get; set; }

    [JsonPropertyName("snapToGrid")]
    public bool SnapToGrid { get; set; }

    [JsonPropertyName("gridSize")]
    public int GridSize { get; set; } = 8;
}

public sealed class ShowSettingsDto
{
    [JsonPropertyName("defaultCenterView")]
    public string DefaultCenterView { get; set; } = "slides";

    [JsonPropertyName("thumbnailSize")]
    public int ThumbnailSize { get; set; } = 120;

    [JsonPropertyName("showSlideLabels")]
    public bool ShowSlideLabels { get; set; } = true;

    [JsonPropertyName("autoTakeOnDoubleClick")]
    public bool AutoTakeOnDoubleClick { get; set; }

    /// <summary>Active deck layout: "thumbnail" | "text" | "list".</summary>
    [JsonPropertyName("deckViewMode")]
    public string DeckViewMode { get; set; } = "thumbnail";

    /// <summary>Whether to split the single-deck view into section groups.</summary>
    [JsonPropertyName("groupBySection")]
    public bool GroupBySection { get; set; }

    /// <summary>When true, slide thumbnails use the custom colour and opacity below; when false, transparent slide areas show the checkerboard.</summary>
    [JsonPropertyName("transparentThumbnailBackgroundEnabled")]
    public bool TransparentThumbnailBackgroundEnabled { get; set; } = true;

    /// <summary>Solid background colour shown behind transparent-background slide thumbnails (#RRGGBB). Empty string means use checkerboard.</summary>
    [JsonPropertyName("transparentThumbnailColor")]
    public string TransparentThumbnailColor { get; set; } = "#000000";

    /// <summary>Opacity of the thumbnail background colour, 0–100. 0 = fully transparent (checkerboard), 100 = fully opaque.</summary>
    [JsonPropertyName("transparentThumbnailOpacity")]
    public int TransparentThumbnailOpacity { get; set; } = 100;

    /// <summary>0–4 size steps for deck cards; 2 is the default medium.</summary>
    [JsonPropertyName("deckScaleStep")]
    public int DeckScaleStep { get; set; } = 2;

    /// <summary>0–7 size steps for media drawer cards (smaller than slide deck).</summary>
    [JsonPropertyName("mediaPanelScaleStep")]
    public int MediaPanelScaleStep { get; set; } = 4;

    /// <summary>Number of seconds the media transport seek buttons move backward or forward.</summary>
    [JsonPropertyName("mediaSeekSeconds")]
    public int MediaSeekSeconds { get; set; } = 5;

    [JsonPropertyName("timers")]
    public List<ShowTimerDefinition> Timers { get; set; } = new();

    /// <summary>Transition type keys the user has starred as favorites (in pick-order).</summary>
    [JsonPropertyName("favoriteTransitions")]
    public List<string> FavoriteTransitions { get; set; } = new();

    /// <summary>Most-recently used transition type keys, newest first; capped at 8.</summary>
    [JsonPropertyName("recentTransitions")]
    public List<string> RecentTransitions { get; set; } = new();

    /// <summary>Most-recently applied theme ids, newest first.</summary>
    [JsonPropertyName("recentThemeIds")]
    public List<string> RecentThemeIds { get; set; } = new();

    /// <summary>Whether applying a theme slide also copies that theme slide's media cues/actions.</summary>
    [JsonPropertyName("applyMediaActionsWithThemeSlide")]
    public bool ApplyMediaActionsWithThemeSlide { get; set; } = true;

    /// <summary>Global slide transition fallback from the Show toolbar (cut / dissolve / custom).</summary>
    [JsonPropertyName("globalSlideTransition")]
    public ShowToolbarTransitionDto GlobalSlideTransition { get; set; } = new();

    /// <summary>Global media-layer transition from the Show toolbar.</summary>
    [JsonPropertyName("globalMediaTransition")]
    public ShowToolbarTransitionDto GlobalMediaTransition { get; set; } = new();

}

public sealed class ShowTimerDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary><c>countdown</c>, <c>countdownToTime</c>, or <c>elapsed</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "countdown";

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; } = 0;

    [JsonPropertyName("targetTime")]
    public string? TargetTime { get; set; }

    [JsonPropertyName("startSeconds")]
    public int StartSeconds { get; set; }

    [JsonPropertyName("endSeconds")]
    public int? EndSeconds { get; set; }

    [JsonPropertyName("allowsOverrun")]
    public bool AllowsOverrun { get; set; }
}

public sealed class ReflowSettingsDto
{
    [JsonPropertyName("textSize")]
    public int TextSize { get; set; } = 14;

    [JsonPropertyName("previewDensity")]
    public string PreviewDensity { get; set; } = "comfortable";

    [JsonPropertyName("showSlideLabels")]
    public bool ShowSlideLabels { get; set; } = true;
}

public sealed class IntegrationsSettingsDto
{
    [JsonPropertyName("musicManager")]
    public MusicManagerIntegrationDto MusicManager { get; set; } = new();
}

public sealed class MusicManagerIntegrationDto
{
    [JsonPropertyName("supabaseUrl")]
    public string? SupabaseUrl { get; set; }

    [JsonPropertyName("publishableKey")]
    public string? PublishableKey { get; set; }

    [JsonPropertyName("defaultSongAction")]
    public string DefaultSongAction { get; set; } = "import";

    [JsonPropertyName("preferSetImportView")]
    public bool PreferSetImportView { get; set; }
}

public sealed class UpdatesSettingsDto
{
    [JsonPropertyName("autoCheck")]
    public bool AutoCheck { get; set; } = true;

    [JsonPropertyName("lastCheckedAt")]
    public string? LastCheckedAt { get; set; }
}

public sealed class PresentationRefDto : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonIgnore]
    public string PresentationPath => Path;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("arrangementId")]
    public string? ArrangementId { get; set; }

    [JsonPropertyName("destinationLayerId")]
    public string? DestinationLayerId { get; set; }

    [JsonPropertyName("thumbnailData")]
    public string? ThumbnailData { get; set; }
}