namespace ChurchPresenter.Models.Media;

/// <summary>
/// Media assets shown by the active Show media drawer.
/// </summary>
public sealed class ShowMediaDrawerSnapshot
{
    /// <summary>Root media-library items available for direct media-layer activation.</summary>
    public IReadOnlyList<ShowMediaDrawerItem> Items { get; init; } = Array.Empty<ShowMediaDrawerItem>();

    /// <summary>Operator-facing status for the media drawer.</summary>
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Built-in media drawer source/filter used by the Show operator surface.
/// </summary>
public sealed class ShowMediaDrawerFilterOption
{
    /// <summary>Stable option id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing option name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Short explanation displayed by richer drawer UIs.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Number of items currently matching the option.</summary>
    public int ItemCount { get; init; }
}

/// <summary>
/// One media-library item projected for the Show media drawer.
/// </summary>
public sealed class ShowMediaDrawerItem
{
    /// <summary>Media library item id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Effective media type.</summary>
    public string Type { get; init; } = "image";

    /// <summary>Stored source path.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>True when the item can be resolved to a playable file.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Operator-facing availability text.</summary>
    public string AvailabilitySummary { get; init; } = string.Empty;

    /// <summary>Human-readable media type label.</summary>
    public string TypeDisplayName => string.IsNullOrWhiteSpace(Type) ? "Media" : char.ToUpperInvariant(Type[0]) + Type[1..];
}