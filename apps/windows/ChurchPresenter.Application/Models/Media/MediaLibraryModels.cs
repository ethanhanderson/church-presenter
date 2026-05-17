using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Media;

/// <summary>
/// Default cue settings stored per media asset in the global media library.
/// These values seed each new <see cref="SlideMediaCue"/> when the asset is added to a slide but
/// remain independent of the created cue instance.
/// </summary>
public sealed class MediaCueDefaults
{
    [JsonPropertyName("target")]
    public string Target { get; set; } = "mediaUnderlay";

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = "cover";

    [JsonPropertyName("autoplay")]
    public bool Autoplay { get; set; } = true;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    [JsonPropertyName("muted")]
    public bool Muted { get; set; } = true;

    /// <summary>Optional transition when this asset is cued (catalog display name, not toolbar segment labels).</summary>
    [JsonPropertyName("transition")]
    public SlideTransition? Transition { get; set; }
}

/// <summary>
/// A single item in the global media library with per-asset cue defaults and file metadata.
/// </summary>
public sealed class MediaLibraryItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Absolute or content-root-relative path to the source media file.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>Media type: <c>image</c>, <c>video</c>, or <c>audio</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "image";

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("addedAt")]
    public string AddedAt { get; set; } = "";

    [JsonPropertyName("cueDefaults")]
    public MediaCueDefaults CueDefaults { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Per-playlist manifest stored as <c>Media/Playlists/&lt;playlistId&gt;/Playlist.json</c>.
/// </summary>
public sealed class MediaPlaylistManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public List<MediaLibraryItem> Items { get; set; } = new();
}

/// <summary>
/// Lightweight index entry for a media playlist, stored in <c>Media/Index.json</c>.
/// </summary>
public sealed class MediaPlaylistIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Root index for the global media library stored as <c>Media/Index.json</c>.
/// </summary>
public sealed class MediaLibraryIndex
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("items")]
    public List<MediaLibraryItem> Items { get; set; } = new();

    [JsonPropertyName("playlists")]
    public List<MediaPlaylistIndexEntry> Playlists { get; set; } = new();
}

/// <summary>
/// Result of copying externally referenced media into <c>Media/Files/</c>.
/// </summary>
public sealed class MediaMigrationResult
{
    public int TotalItemsScanned { get; set; }

    public int CopiedIntoManagedStorage { get; set; }

    public int SkippedAlreadyManaged { get; set; }

    public int MissingSourceFiles { get; set; }
}

/// <summary>
/// Health snapshot for media path references (audit and diagnostics).
/// </summary>
public sealed class MediaLinkStatistics
{
    public int TotalItems { get; set; }

    public int ManagedItems { get; set; }

    public int MissingFiles { get; set; }

    public int ExternalPathReferences { get; set; }
}