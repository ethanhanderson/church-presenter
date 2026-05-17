using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Themes;

public sealed class ThemeTemplate : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("aspectRatio")]
    public string AspectRatio { get; set; } = "16:9";

    [JsonPropertyName("baseSize")]
    public SlideSizeDto BaseSize { get; set; } = new() { Width = 1920, Height = 1080 };

    [JsonPropertyName("supportedAspectRatios")]
    public List<string> SupportedAspectRatios { get; set; } = new();

    [JsonPropertyName("roleAliases")]
    public List<ThemeRoleAlias> RoleAliases { get; set; } = new();

    [JsonPropertyName("styleTokens")]
    public ThemeStyleTokens StyleTokens { get; set; } = new();

    [JsonPropertyName("slides")]
    public List<ThemeTemplateSlide> Slides { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeTemplateSlide : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("layoutType")]
    public string? LayoutType { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("roleAliases")]
    public List<string> RoleAliases { get; set; } = new();

    [JsonPropertyName("background")]
    public SlideBackground Background { get; set; } = new SolidSlideBackground();

    [JsonPropertyName("layers")]
    public List<SlideLayer> Layers { get; set; } = new();

    [JsonPropertyName("mediaCues")]
    public List<SlideMediaCue>? MediaCues { get; set; }

    [JsonPropertyName("textLayers")]
    public List<ThemeTemplateTextLayer>? LegacyTextLayers { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeTemplateTextLayer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("transform")]
    public LayerTransformModel Transform { get; set; } = new();

    [JsonPropertyName("style")]
    public TextStyleModel Style { get; set; } = new();

    [JsonPropertyName("fills")]
    public List<LayerFillModel>? Fills { get; set; }

    [JsonPropertyName("strokes")]
    public List<LayerStrokeModel>? Strokes { get; set; }

    [JsonPropertyName("textFit")]
    public string? TextFit { get; set; }

    [JsonPropertyName("padding")]
    public double? Padding { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeRoleAlias
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeStyleTokens
{
    [JsonPropertyName("fonts")]
    public List<ThemeFontToken> Fonts { get; set; } = new();

    [JsonPropertyName("colors")]
    public List<ThemeColorToken> Colors { get; set; } = new();

    [JsonPropertyName("shapes")]
    public List<ThemeShapeToken> Shapes { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeFontToken
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("font")]
    public TextFontModel Font { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeColorToken
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeShapeToken
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("shapeType")]
    public string ShapeType { get; set; } = "rectangle";

    [JsonPropertyName("style")]
    public ShapeStyleModel Style { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeLibraryFile
{
    [JsonPropertyName("themes")]
    public List<ThemeTemplate> Themes { get; set; } = new();
}