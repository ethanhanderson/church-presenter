namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Version tuple stamped onto prepared cues for cache and host diffing.
/// </summary>
public sealed record ResourceStamp
{
    /// <summary>Document or asset content version.</summary>
    public string? ContentVersion { get; init; }

    /// <summary>Applied theme version.</summary>
    public string? ThemeVersion { get; init; }

    /// <summary>Resolved resource dependency hash.</summary>
    public string? ResourceHash { get; init; }

    /// <summary>Operator override hash applied during preparation.</summary>
    public string? OverridesHash { get; init; }

    /// <summary>Builds a stable cache key for this stamp.</summary>
    public string ToCacheKey()
    {
        return string.Join(
            '|',
            ContentVersion ?? string.Empty,
            ThemeVersion ?? string.Empty,
            ResourceHash ?? string.Empty,
            OverridesHash ?? string.Empty);
    }
}
