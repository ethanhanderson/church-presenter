using System.Text.Json.Serialization;

using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Models.Settings;

/// <summary>
/// Portable output configuration stored as <c>Configurations/Output.json</c> under the content root.
/// Monitor selection (machine-specific) lives in <c>MachineState/OutputBinding.json</c> instead.
/// </summary>
public sealed class OutputConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Portable output Looks and clear groups that travel with the support configuration.</summary>
    [JsonPropertyName("looks")]
    public List<OutputLookDefinition> Looks { get; set; } = new();

    /// <summary>Portable logical audience or stage screens; concrete monitor bindings stay machine-local.</summary>
    [JsonPropertyName("logicalScreens")]
    public List<LogicalScreenDefinition> LogicalScreens { get; set; } = new();

    /// <summary>Reusable output masks that Looks can assign per audience screen.</summary>
    [JsonPropertyName("masks")]
    public List<OutputMaskDefinition> Masks { get; set; } = new();
}

/// <summary>
/// Portable output mask definition selected from audience Looks.
/// </summary>
public sealed class OutputMaskDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional path data, asset id, or future mask source reference.</summary>
    [JsonPropertyName("sourceReference")]
    public string? SourceReference { get; set; }
}

/// <summary>
/// Portable logical screen definition used by Looks, stage layouts, and package/sync previews.
/// </summary>
public sealed class LogicalScreenDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary><c>audience</c>, <c>stage</c>, <c>capture</c>, or another future logical role.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "audience";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1080;
}

/// <summary>
/// Portable show operation settings stored as <c>Configurations/Show.json</c>.
/// </summary>
public sealed class ShowConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

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

    /// <summary>When true, slide thumbnails use the custom colour and opacity; when false, checkerboard for transparent slide backgrounds.</summary>
    [JsonPropertyName("transparentThumbnailBackgroundEnabled")]
    public bool TransparentThumbnailBackgroundEnabled { get; set; } = true;

    [JsonPropertyName("transparentThumbnailColor")]
    public string TransparentThumbnailColor { get; set; } = "#000000";

    /// <summary>Opacity of the thumbnail background colour, 0–100.</summary>
    [JsonPropertyName("transparentThumbnailOpacity")]
    public int TransparentThumbnailOpacity { get; set; } = 100;

    /// <summary>0–4 size steps for deck cards.</summary>
    [JsonPropertyName("deckScaleStep")]
    public int DeckScaleStep { get; set; } = 2;

    /// <summary>0–7 size steps for media drawer grid cards (smaller than slide deck min widths).</summary>
    [JsonPropertyName("mediaPanelScaleStep")]
    public int MediaPanelScaleStep { get; set; } = 4;

    /// <summary>Number of seconds the media transport seek buttons move backward or forward.</summary>
    [JsonPropertyName("mediaSeekSeconds")]
    public int MediaSeekSeconds { get; set; } = 5;

    [JsonPropertyName("timers")]
    public List<ShowTimerDefinition> Timers { get; set; } = new();

    /// <summary>Transition type keys the user has starred as favorites.</summary>
    [JsonPropertyName("favoriteTransitions")]
    public List<string> FavoriteTransitions { get; set; } = new();

    /// <summary>Most-recently used transition type keys, newest first.</summary>
    [JsonPropertyName("recentTransitions")]
    public List<string> RecentTransitions { get; set; } = new();

    /// <summary>Most-recently applied theme ids, newest first.</summary>
    [JsonPropertyName("recentThemeIds")]
    public List<string> RecentThemeIds { get; set; } = new();

    /// <summary>Whether applying a theme slide also copies that theme slide's media cues/actions.</summary>
    [JsonPropertyName("applyMediaActionsWithThemeSlide")]
    public bool ApplyMediaActionsWithThemeSlide { get; set; } = true;

    /// <summary>Global slide transition fallback from the Show toolbar.</summary>
    [JsonPropertyName("globalSlideTransition")]
    public ShowToolbarTransitionDto GlobalSlideTransition { get; set; } = new();

    /// <summary>Global media transition fallback from the Show toolbar.</summary>
    [JsonPropertyName("globalMediaTransition")]
    public ShowToolbarTransitionDto GlobalMediaTransition { get; set; } = new();

    /// <summary>Reusable operator message templates that travel with a support package.</summary>
    [JsonPropertyName("messages")]
    public List<ShowMessageDefinition> Messages { get; set; } = new();

    /// <summary>Reusable props such as bugs, QR overlays, and labels.</summary>
    [JsonPropertyName("props")]
    public List<ShowPropDefinition> Props { get; set; } = new();

    /// <summary>Reusable macro definitions stored as portable production setup.</summary>
    [JsonPropertyName("macros")]
    public List<ShowMacroDefinition> Macros { get; set; } = new();

    /// <summary>Reusable audio playlists and folders for the Audio Bin.</summary>
    [JsonPropertyName("audioPlaylists")]
    public List<ShowAudioPlaylistDefinition> AudioPlaylists { get; set; } = new();
}

/// <summary>
/// Portable Audio Bin playlist definition.
/// </summary>
public sealed class ShowAudioPlaylistDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    [JsonPropertyName("itemIds")]
    public List<string> ItemIds { get; set; } = new();

    [JsonPropertyName("shuffle")]
    public bool Shuffle { get; set; }

    [JsonPropertyName("transitionSeconds")]
    public double TransitionSeconds { get; set; } = 0.5;
}

/// <summary>
/// Portable message template definition.
/// </summary>
public sealed class ShowMessageDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("tokens")]
    public List<ShowMessageTokenDefinition> Tokens { get; set; } = new();

    [JsonPropertyName("themeId")]
    public string? ThemeId { get; set; }

    [JsonPropertyName("transition")]
    public SlideTransition? Transition { get; set; }

    [JsonPropertyName("dismiss")]
    public ShowMessageDismissDefinition Dismiss { get; set; } = new();

    [JsonPropertyName("clearGroupId")]
    public string? ClearGroupId { get; set; }
}

/// <summary>
/// Token placeholder used by an operator message template.
/// </summary>
public sealed class ShowMessageTokenDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary><c>text</c>, <c>timer</c>, or <c>systemClock</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "text";

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Message auto-dismiss behavior.
/// </summary>
public sealed class ShowMessageDismissDefinition
{
    /// <summary><c>manual</c>, <c>timerEnd</c>, or <c>afterSeconds</c>.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "manual";

    [JsonPropertyName("seconds")]
    public int? Seconds { get; set; }
}

/// <summary>
/// Portable prop definition.
/// </summary>
public sealed class ShowPropDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("assetReference")]
    public string? AssetReference { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("transition")]
    public SlideTransition? Transition { get; set; }

    [JsonPropertyName("accentColor")]
    public string? AccentColor { get; set; }

    [JsonPropertyName("clearGroupId")]
    public string? ClearGroupId { get; set; }
}

/// <summary>
/// Portable macro definition; command payloads remain JSON-compatible and application-layer owned.
/// </summary>
public sealed class ShowMacroDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("collectionId")]
    public string? CollectionId { get; set; }

    [JsonPropertyName("iconKey")]
    public string? IconKey { get; set; }

    [JsonPropertyName("accentColor")]
    public string? AccentColor { get; set; }

    [JsonPropertyName("commandIds")]
    public List<string> CommandIds { get; set; } = new();

    [JsonPropertyName("commands")]
    public List<ShowMacroCommandDefinition> Commands { get; set; } = new();
}

/// <summary>
/// Portable macro command reference or inline live command payload.
/// </summary>
public sealed class ShowMacroCommandDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary><c>clearGroup</c>, <c>clearLayer</c>, <c>message</c>, <c>prop</c>, <c>stageLayout</c>, or <c>timer</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }

    [JsonPropertyName("screenId")]
    public string? ScreenId { get; set; }
}

/// <summary>
/// Portable stage-screen settings stored as <c>Configurations/Stage.json</c>.
/// Machine-local monitor bindings remain in <c>MachineState/OutputBinding.json</c>.
/// </summary>
public sealed class StageConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Reusable stage layouts available to logical stage screens.</summary>
    [JsonPropertyName("layouts")]
    public List<StageLayout> Layouts { get; set; } = new();

    /// <summary>Default layout id per logical stage screen id.</summary>
    [JsonPropertyName("defaultLayoutIdsByScreenId")]
    public Dictionary<string, string> DefaultLayoutIdsByScreenId { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Portable editor settings stored as <c>Configurations/Editor.json</c>.
/// </summary>
public sealed class EditorConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

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

/// <summary>
/// Portable reflow settings stored as <c>Configurations/Reflow.json</c>.
/// </summary>
public sealed class ReflowConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("textSize")]
    public int TextSize { get; set; } = 14;

    [JsonPropertyName("previewDensity")]
    public string PreviewDensity { get; set; } = "comfortable";

    [JsonPropertyName("showSlideLabels")]
    public bool ShowSlideLabels { get; set; } = true;
}

/// <summary>
/// Portable integrations settings stored as <c>Configurations/Integrations.json</c>.
/// Credentials (API keys) should still be treated as sensitive; omit from shared copies if needed.
/// </summary>
public sealed class IntegrationsConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("musicManager")]
    public MusicManagerIntegrationDto MusicManager { get; set; } = new();
}

/// <summary>
/// Portable appearance settings stored as <c>Configurations/Appearance.json</c>.
/// </summary>
public sealed class AppearanceConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("maxRecentFiles")]
    public int MaxRecentFiles { get; set; } = 10;
}

/// <summary>
/// Portable library management settings stored as <c>Configurations/LibraryManagement.json</c>.
/// </summary>
public sealed class LibraryManagementConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;
}

/// <summary>
/// Portable shared support-file manifest stored as <c>Configurations/Support.json</c>.
/// </summary>
public sealed class SupportConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("themeBindings")]
    public List<ThemeBindingDefinition> ThemeBindings { get; set; } = new();

    [JsonPropertyName("labels")]
    public List<SupportLabelDefinition> Labels { get; set; } = new();

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Portable mapping from a logical production surface to a theme or variant.
/// </summary>
public sealed class ThemeBindingDefinition
{
    [JsonPropertyName("surfaceId")]
    public string SurfaceId { get; set; } = string.Empty;

    [JsonPropertyName("themeId")]
    public string ThemeId { get; set; } = string.Empty;

    [JsonPropertyName("variantId")]
    public string? VariantId { get; set; }
}

/// <summary>
/// Portable label/group metadata shared by shows, clear groups, props, messages, and macros.
/// </summary>
public sealed class SupportLabelDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}