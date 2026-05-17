using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Content;

/// <summary>
/// Root manifest written to <c>ChurchPresenter.Content.json</c> at the managed content root.
/// Identifies the layout version, root identity, and top-level folder map.
/// </summary>
public sealed class ContentRootManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonPropertyName("contentRootId")]
    public string ContentRootId { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("portableRootKind")]
    public string PortableRootKind { get; set; } = "onedrive-documents";

    [JsonPropertyName("portableRootName")]
    public string PortableRootName { get; set; } = "Church Presenter";

    [JsonPropertyName("lastAuditAt")]
    public string? LastAuditAt { get; set; }

    [JsonPropertyName("lastMigratedAt")]
    public string? LastMigratedAt { get; set; }

    [JsonPropertyName("machineStateDirectory")]
    public string MachineStateDirectory { get; set; } = "MachineState/";

    [JsonPropertyName("migrationHistoryPath")]
    public string MigrationHistoryPath { get; set; } = "Audits/MigrationHistory.json";

    [JsonPropertyName("resetWorkflow")]
    public List<string> ResetWorkflow { get; set; } = new();

    [JsonPropertyName("folderMap")]
    public Dictionary<string, string> FolderMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A lightweight index entry stored inside a domain <c>Index.json</c> file.
/// Provides a quick-scan list without loading every per-item manifest.
/// </summary>
public sealed class DomainIndexEntry
{
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
}

/// <summary>
/// The index file stored as <c>Index.json</c> inside each domain folder (Libraries, Playlists, etc.).
/// </summary>
public sealed class DomainIndex
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<DomainIndexEntry> Entries { get; set; } = new();
}

/// <summary>
/// Per-library manifest stored as <c>Libraries/&lt;LibraryId&gt;/Library.json</c>.
/// </summary>
public sealed class LibraryManifest
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

    [JsonPropertyName("defaultFolder")]
    public string? DefaultFolder { get; set; }

    [JsonPropertyName("presentations")]
    public List<PresentationRefDto> Presentations { get; set; } = new();
}

/// <summary>
/// Per-playlist manifest stored as <c>Playlists/&lt;PlaylistId&gt;/Playlist.json</c>.
/// </summary>
public sealed class PlaylistManifest
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
    public List<PresentationRefDto> Items { get; set; } = new();

    [JsonPropertyName("externalSet")]
    public ExternalSetLinkDto? ExternalSet { get; set; }

    [JsonPropertyName("sync")]
    public SyncMetadata? Sync { get; set; }
}

/// <summary>
/// Per-presentation manifest stored as <c>Presentations/&lt;PresentationId&gt;/Presentation.json</c>.
/// </summary>
public sealed class PresentationManifestEntry
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("bundlePath")]
    public string? BundlePath { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Configurations folder manifest stored as <c>Configurations/Manifest.json</c>.
/// </summary>
public sealed class ConfigurationsManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("lastModifiedAt")]
    public string? LastModifiedAt { get; set; }
}

/// <summary>
/// Themes index stored as <c>Themes/Index.json</c>.
/// </summary>
public sealed class ThemeIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Themes index stored as <c>Themes/Index.json</c>.
/// </summary>
public sealed class ThemeIndex
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<ThemeIndexEntry> Entries { get; set; } = new();
}