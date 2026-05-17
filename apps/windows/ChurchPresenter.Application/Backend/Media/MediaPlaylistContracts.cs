namespace ChurchPresenter.Backend.Media;

/// <summary>
/// Playlist navigation policy for media and audio bins.
/// </summary>
public enum PlaylistAdvanceMode
{
    StopAtEnd,
    Loop,
}

/// <summary>
/// Reference entry for a media cue inside a media playlist.
/// </summary>
public sealed record MediaPlaylistEntry
{
    public string EntryId { get; init; } = string.Empty;

    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Reference entry for an audio cue inside an audio playlist.
/// </summary>
public sealed record AudioPlaylistEntry
{
    public string EntryId { get; init; } = string.Empty;

    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Playback selection returned by playlist navigation.
/// </summary>
public sealed record PlaylistPlaybackSelection<TEntry>
    where TEntry : class
{
    public int? Index { get; init; }

    public TEntry? Entry { get; init; }

    public bool Wrapped { get; init; }

    public bool ReachedEnd { get; init; }

    public bool HasSelection => Index.HasValue && Entry is not null;
}

/// <summary>
/// Playlist contract for media-bin cues.
/// </summary>
public sealed record MediaPlaylist
{
    public string PlaylistId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public PlaylistAdvanceMode AdvanceMode { get; init; } = PlaylistAdvanceMode.StopAtEnd;

    public IReadOnlyList<MediaPlaylistEntry> Entries { get; init; } = Array.Empty<MediaPlaylistEntry>();

    public PlaylistPlaybackSelection<MediaPlaylistEntry> Start()
    {
        return CuePlaylistNavigator.Start(Entries);
    }

    public PlaylistPlaybackSelection<MediaPlaylistEntry> Advance(int currentIndex)
    {
        return CuePlaylistNavigator.Advance(Entries, currentIndex, AdvanceMode);
    }
}

/// <summary>
/// Playlist contract for audio-bin cues.
/// </summary>
public sealed record AudioPlaylist
{
    public string PlaylistId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public PlaylistAdvanceMode AdvanceMode { get; init; } = PlaylistAdvanceMode.StopAtEnd;

    public IReadOnlyList<AudioPlaylistEntry> Entries { get; init; } = Array.Empty<AudioPlaylistEntry>();

    public PlaylistPlaybackSelection<AudioPlaylistEntry> Start()
    {
        return CuePlaylistNavigator.Start(Entries);
    }

    public PlaylistPlaybackSelection<AudioPlaylistEntry> Advance(int currentIndex)
    {
        return CuePlaylistNavigator.Advance(Entries, currentIndex, AdvanceMode);
    }
}

/// <summary>
/// Reference surfaces that must participate in cleanup graph analysis.
/// </summary>
public enum MediaReferenceSurface
{
    Presentation,
    Slide,
    ServicePlaylist,
    MediaPlaylist,
    AudioPlaylist,
    Theme,
    Prop,
    Macro,
    Mask,
    Package,
    ExternalPlan,
}

/// <summary>
/// One node in the content/action graph that keeps assets alive for cleanup analysis.
/// </summary>
public sealed record MediaReferenceNode
{
    public string NodeId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MediaReferenceSurface Surface { get; init; }

    public IReadOnlySet<string> AssetIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Cleanup analysis result for one asset.
/// </summary>
public sealed record MediaCleanupCandidate
{
    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MediaStoragePolicy StoragePolicy { get; init; }

    public bool IsReferenced { get; init; }

    public bool IsPinned { get; init; }

    public bool EligibleForCleanup { get; init; }

    public IReadOnlyList<MediaReferenceNode> ReferencedBy { get; init; } = Array.Empty<MediaReferenceNode>();

    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Traversable reference graph used for safe media cleanup previews.
/// </summary>
public sealed record MediaCleanupReferenceGraph
{
    public IReadOnlyList<MediaReferenceNode> Nodes { get; init; } = Array.Empty<MediaReferenceNode>();

    public IReadOnlySet<string> GetReferencedAssetIds()
    {
        HashSet<string> assetIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (MediaReferenceNode node in Nodes)
        {
            assetIds.UnionWith(node.AssetIds);
        }

        return assetIds;
    }

    public IReadOnlyList<MediaCleanupCandidate> Analyze(
        IEnumerable<MediaAsset> assets,
        IEnumerable<string>? pinnedAssetIds = null)
    {
        ArgumentNullException.ThrowIfNull(assets);

        HashSet<string> pinned = pinnedAssetIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(pinnedAssetIds, StringComparer.OrdinalIgnoreCase);

        List<MediaCleanupCandidate> results = new();
        foreach (MediaAsset asset in assets)
        {
            List<MediaReferenceNode> referencedBy = Nodes
                .Where(node => node.AssetIds.Contains(asset.AssetId))
                .ToList();

            bool isReferenced = referencedBy.Count > 0;
            bool isPinned = pinned.Contains(asset.AssetId);
            bool ownsPayload = asset.OwnsManagedPayload;

            string reason;
            bool eligibleForCleanup;
            if (isReferenced)
            {
                eligibleForCleanup = false;
                reason = "Asset is still referenced by the content/action graph.";
            }
            else if (isPinned)
            {
                eligibleForCleanup = false;
                reason = "Asset is pinned for retention.";
            }
            else if (!ownsPayload)
            {
                eligibleForCleanup = false;
                reason = "Externally referenced assets are not owned by managed cleanup.";
            }
            else
            {
                eligibleForCleanup = true;
                reason = "Managed asset is unreferenced and eligible for cleanup preview.";
            }

            results.Add(new MediaCleanupCandidate
            {
                AssetId = asset.AssetId,
                DisplayName = asset.DisplayName,
                StoragePolicy = asset.StoragePolicy,
                IsReferenced = isReferenced,
                IsPinned = isPinned,
                EligibleForCleanup = eligibleForCleanup,
                ReferencedBy = referencedBy,
                Reason = reason,
            });
        }

        return results;
    }
}

internal static class CuePlaylistNavigator
{
    public static PlaylistPlaybackSelection<TEntry> Start<TEntry>(IReadOnlyList<TEntry> entries)
        where TEntry : class
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count == 0
            ? new PlaylistPlaybackSelection<TEntry> { ReachedEnd = true }
            : new PlaylistPlaybackSelection<TEntry>
            {
                Index = 0,
                Entry = entries[0],
            };
    }

    public static PlaylistPlaybackSelection<TEntry> Advance<TEntry>(
        IReadOnlyList<TEntry> entries,
        int currentIndex,
        PlaylistAdvanceMode advanceMode)
        where TEntry : class
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return new PlaylistPlaybackSelection<TEntry> { ReachedEnd = true };

        if (currentIndex < 0)
            return Start(entries);

        int nextIndex = currentIndex + 1;
        if (nextIndex < entries.Count)
        {
            return new PlaylistPlaybackSelection<TEntry>
            {
                Index = nextIndex,
                Entry = entries[nextIndex],
            };
        }

        if (advanceMode == PlaylistAdvanceMode.Loop)
        {
            return new PlaylistPlaybackSelection<TEntry>
            {
                Index = 0,
                Entry = entries[0],
                Wrapped = true,
            };
        }

        return new PlaylistPlaybackSelection<TEntry> { ReachedEnd = true };
    }
}