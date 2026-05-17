using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Documents;

/// <summary>
/// Fully typed view of a presentation bundle used by rendering, editing, and theme application surfaces.
/// </summary>
public sealed class PresentationProject
{
    public PresentationManifest Manifest { get; set; } = new();

    public List<PresentationSlide> Slides { get; set; } = new();

    public PresentationArrangement Arrangement { get; set; } = new();

    /// <summary>
    /// Theme JSON files stored inside the bundle's <c>themes/</c> folder.
    /// </summary>
    public List<BundleThemeEntry> EmbeddedThemes { get; set; } = new();

    /// <summary>
    /// Absolute file path to the opened <c>.cpres</c> bundle.
    /// </summary>
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;
}

public sealed class BundleThemeEntry
{
    public string FileName { get; set; } = string.Empty;

    public string RawJson { get; set; } = "{}";

    public ThemeTemplate? Template { get; set; }
}

public sealed class PresentationManifest
{
    [JsonPropertyName("formatVersion")]
    public string FormatVersion { get; set; } = "1.0.0";

    [JsonPropertyName("presentationId")]
    public string PresentationId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("aspectRatio")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("outputScaleMode")]
    public string OutputScaleMode { get; set; } = "fit";

    [JsonPropertyName("slideSize")]
    public SlideSizeDto? SlideSize { get; set; }

    [JsonPropertyName("themeId")]
    public string? ThemeId { get; set; }

    [JsonPropertyName("themeBinding")]
    public PresentationThemeBinding? ThemeBinding { get; set; }

    [JsonPropertyName("media")]
    public List<MediaEntry> Media { get; set; } = new();

    [JsonPropertyName("fonts")]
    public List<FontEntry> Fonts { get; set; } = new();

    [JsonPropertyName("externalSong")]
    public ExternalSongLink? ExternalSong { get; set; }

    [JsonPropertyName("sync")]
    public SyncMetadata? Sync { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class MediaEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string? FileName { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("byteSize")]
    public long? ByteSize { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "image";

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonIgnore]
    public string? SourcePath { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class FontEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("postscriptName")]
    public string? PostscriptName { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("byteSize")]
    public long? ByteSize { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 400;

    [JsonPropertyName("style")]
    public string Style { get; set; } = "normal";

    [JsonIgnore]
    public string? SourcePath { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalSongLink
{
    [JsonPropertyName("songId")]
    public string SongId { get; set; } = string.Empty;

    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = string.Empty;

    [JsonPropertyName("syncedAt")]
    public string? SyncedAt { get; set; }

    [JsonPropertyName("remoteVersion")]
    public int? RemoteVersion { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SyncMetadata
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("lastSyncAttempt")]
    public string? LastSyncAttempt { get; set; }

    [JsonPropertyName("conflictUrl")]
    public string? ConflictUrl { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class PresentationArrangement
{
    [JsonPropertyName("order")]
    public List<string> Order { get; set; } = new();

    [JsonPropertyName("sections")]
    public List<SectionGroup> Sections { get; set; } = new();

    /// <summary>Named arrangements for the presentation, including the auto-generated natural/master arrangement.</summary>
    [JsonPropertyName("arrangements")]
    public List<NamedArrangement> Arrangements { get; set; } = new();

    /// <summary>ID of the currently active named arrangement, or null for the natural order.</summary>
    [JsonPropertyName("activeArrangementId")]
    public string? ActiveArrangementId { get; set; }

    /// <summary>Seconds between automatic slide advances; 0 disables auto-advance.</summary>
    [JsonPropertyName("autoAdvanceSeconds")]
    public int AutoAdvanceSeconds { get; set; }

    /// <summary>Presentation-wide default transition applied to all slides unless overridden per slide.</summary>
    [JsonPropertyName("defaultTransition")]
    public SlideTransition? DefaultTransition { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A named, reorderable arrangement of section groups, with optional repeated group instances.</summary>
public sealed class NamedArrangement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>True for the auto-generated master arrangement that mirrors the natural slide creation order.</summary>
    [JsonPropertyName("isNatural")]
    public bool IsNatural { get; set; }

    /// <summary>Ordered list of group occurrences that form this arrangement.</summary>
    [JsonPropertyName("groups")]
    public List<ArrangementGroupRef> Groups { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A single occurrence of a section group within a named arrangement (may repeat the same group multiple times).</summary>
public sealed class ArrangementGroupRef
{
    /// <summary>References a <see cref="SectionGroup.Id"/> in the parent arrangement's <see cref="PresentationArrangement.Sections"/>.</summary>
    [JsonPropertyName("sectionGroupId")]
    public string SectionGroupId { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SectionGroup
{
    /// <summary>Stable identity for this group, used by <see cref="ArrangementGroupRef"/>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("slideIds")]
    public List<string> SlideIds { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class PresentationSlide : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "blank";

    [JsonPropertyName("layoutType")]
    public string? LayoutType { get; set; }

    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("sectionLabel")]
    public string? SectionLabel { get; set; }

    [JsonPropertyName("sectionIndex")]
    public int? SectionIndex { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("hotKey")]
    public string? HotKey { get; set; }

    [JsonPropertyName("goToNextTimerId")]
    public string? GoToNextTimerId { get; set; }

    [JsonPropertyName("layers")]
    public List<SlideLayer> Layers { get; set; } = new();

    [JsonPropertyName("textBlocks")]
    public List<SlideTextBlock> TextBlocks { get; set; } = new();

    [JsonPropertyName("themeBinding")]
    public PresentationThemeBinding? ThemeBinding { get; set; }

    [JsonPropertyName("mediaCues")]
    public List<SlideMediaCue> MediaCues { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<SlideActionDefinition> Actions { get; set; } = new();

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("background")]
    public SlideBackground? Background { get; set; }

    [JsonPropertyName("overrides")]
    public JsonElement? Overrides { get; set; }

    [JsonPropertyName("animations")]
    public SlideAnimations? Animations { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SlideTextBlock
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("sourceLayerId")]
    public string? SourceLayerId { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class PresentationThemeBinding
{
    [JsonPropertyName("themeId")]
    public string? ThemeId { get; set; }

    [JsonPropertyName("themeVersion")]
    public string? ThemeVersion { get; set; }

    [JsonPropertyName("themeSlideId")]
    public string? ThemeSlideId { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ThemeBindingModes.Linked;

    [JsonPropertyName("embeddedSnapshotId")]
    public string? EmbeddedSnapshotId { get; set; }

    [JsonPropertyName("roleMappings")]
    public List<ThemeRoleMapping> RoleMappings { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public static class ThemeBindingModes
{
    public const string Linked = "linked";
    public const string Detached = "detached";
    public const string Forked = "forked";
    public const string Materialized = "materialized";
}

public sealed class ThemeRoleMapping
{
    [JsonPropertyName("slideRole")]
    public string SlideRole { get; set; } = string.Empty;

    [JsonPropertyName("themeSlideId")]
    public string ThemeSlideId { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SlideMediaCue
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "image";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("target")]
    public string Target { get; set; } = "mediaUnderlay";

    [JsonPropertyName("fit")]
    public string? Fit { get; set; }

    [JsonPropertyName("loop")]
    public bool? Loop { get; set; }

    [JsonPropertyName("muted")]
    public bool? Muted { get; set; }

    [JsonPropertyName("autoplay")]
    public bool? Autoplay { get; set; }

    [JsonPropertyName("transition")]
    public SlideTransition? Transition { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SlideActionDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "clearPresentation";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SlideAnimations
{
    [JsonPropertyName("transition")]
    public SlideTransition? Transition { get; set; }

    [JsonPropertyName("buildIn")]
    public List<BuildStep> BuildIn { get; set; } = new();

    [JsonPropertyName("buildOut")]
    public List<BuildStep> BuildOut { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SlideTransition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "fade";

    [JsonPropertyName("duration")]
    public int Duration { get; set; } = 300;

    [JsonPropertyName("easing")]
    public string? Easing { get; set; }

    /// <summary>Type-specific parameters (e.g. "direction": "fromLeft" for wipe/slide transitions).</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, string>? Parameters { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string GetParameter(string key, string defaultValue = "") =>
        Parameters != null && Parameters.TryGetValue(key, out var v) ? v : defaultValue;
}

public sealed class BuildStep
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("layerId")]
    public string LayerId { get; set; } = string.Empty;

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "onAdvance";

    [JsonPropertyName("delay")]
    public int? Delay { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public abstract class SlideBackground
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SolidSlideBackground : SlideBackground
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";
}

public sealed class GradientSlideBackground : SlideBackground
{
    [JsonPropertyName("angle")]
    public double Angle { get; set; }

    [JsonPropertyName("stops")]
    public List<GradientStop> Stops { get; set; } = new();
}

public sealed class ImageSlideBackground : SlideBackground
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = "cover";

    [JsonPropertyName("position")]
    public BackgroundPosition Position { get; set; } = new();

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;
}

public sealed class VideoSlideBackground : SlideBackground
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = "cover";

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    [JsonPropertyName("muted")]
    public bool Muted { get; set; } = true;

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;
}

public sealed class TransparentSlideBackground : SlideBackground
{
}

public sealed class GradientStop
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("position")]
    public double Position { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class BackgroundPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; } = 50;

    [JsonPropertyName("y")]
    public double Y { get; set; } = 50;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public abstract class SlideLayer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("blendMode")]
    public string? BlendMode { get; set; }

    [JsonPropertyName("transform")]
    public LayerTransformModel Transform { get; set; } = new();

    [JsonPropertyName("fills")]
    public List<LayerFillModel>? Fills { get; set; }

    [JsonPropertyName("strokes")]
    public List<LayerStrokeModel>? Strokes { get; set; }

    [JsonPropertyName("effects")]
    public List<LayerEffectModel>? Effects { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TextLayer : SlideLayer
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("textBinding")]
    public ThemeTextBinding? TextBinding { get; set; }

    [JsonPropertyName("style")]
    public TextStyleModel? Style { get; set; }

    [JsonPropertyName("textFit")]
    public string? TextFit { get; set; }

    [JsonPropertyName("textMode")]
    public string? TextMode { get; set; }

    [JsonPropertyName("padding")]
    public double? Padding { get; set; }
}

public sealed class ThemeTextBinding
{
    [JsonPropertyName("textBlockId")]
    public string? TextBlockId { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("fallbackIndex")]
    public int? FallbackIndex { get; set; }

    [JsonPropertyName("placeholderText")]
    public string? PlaceholderText { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ShapeLayer : SlideLayer
{
    [JsonPropertyName("shapeType")]
    public string ShapeType { get; set; } = "rectangle";

    [JsonPropertyName("style")]
    public ShapeStyleModel Style { get; set; } = new();
}

public sealed class MediaLayer : SlideLayer
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "image";

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = "contain";

    [JsonPropertyName("loop")]
    public bool? Loop { get; set; }

    [JsonPropertyName("muted")]
    public bool? Muted { get; set; }

    [JsonPropertyName("autoplay")]
    public bool? Autoplay { get; set; }
}

public sealed class WebLayer : SlideLayer
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1;

    [JsonPropertyName("interactive")]
    public bool Interactive { get; set; }

    [JsonPropertyName("refreshInterval")]
    public int? RefreshInterval { get; set; }
}

public sealed class VectorLayer : SlideLayer
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("viewBox")]
    public string? ViewBox { get; set; }

    [JsonPropertyName("fillRule")]
    public string? FillRule { get; set; }
}

public sealed class LayerTransformModel
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("cornerRadius")]
    public double? CornerRadius { get; set; }

    [JsonPropertyName("cornerRadiusTopLeft")]
    public double? CornerRadiusTopLeft { get; set; }

    [JsonPropertyName("cornerRadiusTopRight")]
    public double? CornerRadiusTopRight { get; set; }

    [JsonPropertyName("cornerRadiusBottomRight")]
    public double? CornerRadiusBottomRight { get; set; }

    [JsonPropertyName("cornerRadiusBottomLeft")]
    public double? CornerRadiusBottomLeft { get; set; }

    [JsonPropertyName("flipX")]
    public bool? FlipX { get; set; }

    [JsonPropertyName("flipY")]
    public bool? FlipY { get; set; }

    [JsonPropertyName("lockAspectRatio")]
    public bool? LockAspectRatio { get; set; }

    [JsonPropertyName("clipContent")]
    public bool? ClipContent { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class LayerFillModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class LayerStrokeModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 1;

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("sides")]
    public string? Sides { get; set; }

    [JsonPropertyName("customSides")]
    public LayerStrokeSides? CustomSides { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class LayerStrokeSides
{
    [JsonPropertyName("top")]
    public bool Top { get; set; } = true;

    [JsonPropertyName("right")]
    public bool Right { get; set; } = true;

    [JsonPropertyName("bottom")]
    public bool Bottom { get; set; } = true;

    [JsonPropertyName("left")]
    public bool Left { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public abstract class LayerEffectModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class DropShadowEffectModel : LayerEffectModel
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.5;

    [JsonPropertyName("offsetX")]
    public double OffsetX { get; set; }

    [JsonPropertyName("offsetY")]
    public double OffsetY { get; set; }

    [JsonPropertyName("blur")]
    public double Blur { get; set; }

    [JsonPropertyName("spread")]
    public double? Spread { get; set; }
}

public sealed class LayerBlurEffectModel : LayerEffectModel
{
    [JsonPropertyName("radius")]
    public double Radius { get; set; }
}

public sealed class TextStyleModel
{
    [JsonPropertyName("font")]
    public TextFontModel Font { get; set; } = new();

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    [JsonPropertyName("alignment")]
    public string Alignment { get; set; } = "center";

    [JsonPropertyName("verticalAlignment")]
    public string VerticalAlignment { get; set; } = "middle";

    [JsonPropertyName("shadow")]
    public TextShadowModel Shadow { get; set; } = new();

    [JsonPropertyName("outline")]
    public TextOutlineModel Outline { get; set; } = new();

    [JsonPropertyName("effectsOrder")]
    public List<string>? EffectsOrder { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TextFontModel
{
    [JsonPropertyName("family")]
    public string Family { get; set; } = "Segoe UI";

    [JsonPropertyName("size")]
    public double Size { get; set; } = 72;

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 700;

    [JsonPropertyName("italic")]
    public bool Italic { get; set; }

    [JsonPropertyName("lineHeight")]
    public double? LineHeight { get; set; }

    [JsonPropertyName("letterSpacing")]
    public double LetterSpacing { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TextShadowModel
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("offsetX")]
    public double OffsetX { get; set; }

    [JsonPropertyName("offsetY")]
    public double OffsetY { get; set; }

    [JsonPropertyName("blur")]
    public double Blur { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TextOutlineModel
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ShapeStyleModel
{
    [JsonPropertyName("fill")]
    public string Fill { get; set; } = "#3B82F6";

    [JsonPropertyName("fillOpacity")]
    public double FillOpacity { get; set; } = 1;

    [JsonPropertyName("stroke")]
    public string Stroke { get; set; } = "#1D4ED8";

    [JsonPropertyName("strokeWidth")]
    public double StrokeWidth { get; set; } = 1;

    [JsonPropertyName("strokeOpacity")]
    public double StrokeOpacity { get; set; } = 1;

    [JsonPropertyName("cornerRadius")]
    public double CornerRadius { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}