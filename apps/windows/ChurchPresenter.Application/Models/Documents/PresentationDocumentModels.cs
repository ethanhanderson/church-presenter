using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Documents;

/// <summary>
/// Parsed <c>.cpres</c> content for Show/Output (manifest + slides). Layers remain as <see cref="JsonElement"/> for flexible rendering.
/// </summary>
public sealed class PresentationDocument
{
    public PresentationManifestDto Manifest { get; init; } = new();

    public IReadOnlyList<SlideDto> Slides { get; init; } = Array.Empty<SlideDto>();

    /// <summary>
    /// Fully typed project model used by the new renderer, editor, and theme workflows.
    /// </summary>
    public PresentationProject? Project { get; init; }

    /// <summary>Absolute path to the opened <c>.cpres</c> file.</summary>
    public string SourcePath { get; init; } = "";
}

public sealed class PresentationManifestDto
{
    [JsonPropertyName("formatVersion")]
    public string FormatVersion { get; set; } = "";

    [JsonPropertyName("presentationId")]
    public string PresentationId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("aspectRatio")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("outputScaleMode")]
    public string? OutputScaleMode { get; set; }

    [JsonPropertyName("slideSize")]
    public SlideSizeDto? SlideSize { get; set; }

    [JsonPropertyName("media")]
    public List<MediaEntryDto> Media { get; set; } = new();

    [JsonPropertyName("fonts")]
    public JsonElement Fonts { get; set; }
}

public sealed class SlideSizeDto
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public sealed class MediaEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }
}

public sealed class SlideDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "blank";

    [JsonPropertyName("layers")]
    public JsonElement Layers { get; set; }

    [JsonPropertyName("animations")]
    public SlideAnimationsDto? Animations { get; set; }

    [JsonPropertyName("background")]
    public JsonElement? Background { get; set; }

    [JsonPropertyName("mediaCues")]
    public List<SlideMediaCueDto>? MediaCues { get; set; }

    [JsonPropertyName("sectionLabel")]
    public string? SectionLabel { get; set; }

    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class SlideAnimationsDto
{
    [JsonPropertyName("buildIn")]
    public List<BuildStepDto>? BuildIn { get; set; }

    [JsonPropertyName("buildOut")]
    public List<BuildStepDto>? BuildOut { get; set; }
}

public sealed class BuildStepDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("layerId")]
    public string LayerId { get; set; } = "";

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "";
}

public sealed class SlideMediaCueDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = "";

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
}

/// <summary>Audience output media layer payload.</summary>
public sealed class OutputLayerMedia
{
    public string MediaId { get; set; } = "";
    public string MediaType { get; set; } = "image";
    public string? DisplayName { get; set; }
    public string? Fit { get; set; }
    public bool Loop { get; set; }
    public bool Muted { get; set; }
    public bool Autoplay { get; set; }

    /// <summary>Optional transition override resolved from the source cue or media asset defaults.</summary>
    public SlideTransition? Transition { get; set; }

    /// <summary>Resolved file path or ms-appdata URI for rendering.</summary>
    public string? ResolvedSourcePath { get; set; }
}

/// <summary>
/// Program media layers: media underlay, media overlay, and audio.
/// The media clear group removes operator / file-driven layers and restores slide media cues only; the slide clear group suppresses slide background and layer content (not these layers).
/// </summary>
public sealed class MediaLayersState
{
    public OutputLayerMedia? MediaUnderlay { get; set; }
    public OutputLayerMedia? MediaOverlay { get; set; }
    public OutputLayerMedia? Audio { get; set; }
}

/// <summary>Operator clear groups: Presentation = slide background + layer content; Media = <see cref="MediaLayersState"/>.</summary>
public sealed class SuppressState
{
    public bool Presentation { get; set; }
    public bool Media { get; set; }
}

public sealed class ClearingState
{
    public bool Presentation { get; set; }
    public bool Media { get; set; }
}

public sealed class ClearedPresentationState
{
    public string SlideId { get; set; } = "";
    public int SlideIndex { get; set; }
    public int BuildIndex { get; set; }
}

public sealed class ClearedMediaState
{
    public MediaLayersState MediaLayers { get; set; } = new();
}