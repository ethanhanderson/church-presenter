using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Content;

public sealed class CatalogDto
{
    [JsonPropertyName("libraries")]
    public List<LibraryDto> Libraries { get; set; } = new();

    [JsonPropertyName("playlists")]
    public List<PlaylistDto> Playlists { get; set; } = new();
}

public sealed class LibraryDto : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("presentations")]
    public List<PresentationRefDto> Presentations { get; set; } = new();

    [JsonPropertyName("defaultFolder")]
    public string? DefaultFolder { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class PlaylistDto : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public List<PresentationRefDto> Items { get; set; } = new();

    [JsonPropertyName("externalSet")]
    public ExternalSetLinkDto? ExternalSet { get; set; }

    [JsonPropertyName("sync")]
    public SyncMetadata? Sync { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Optional Sunday Manager set link for a playlist.</summary>
public sealed class ExternalSetLinkDto
{
    [JsonPropertyName("setId")]
    public string SetId { get; set; } = "";

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }

    [JsonPropertyName("syncedAt")]
    public string? SyncedAt { get; set; }

    [JsonPropertyName("serviceDate")]
    public string? ServiceDate { get; set; }

    [JsonPropertyName("remoteVersion")]
    public int? RemoteVersion { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}