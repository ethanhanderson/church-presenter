namespace ChurchPresenter.Services.Media;

/// <summary>
/// Background warmup workflow for media-library thumbnails, cue records, and playback resources.
/// </summary>
public interface IMediaStartupWarmupService
{
    /// <summary>Raised when the warmup workflow changes state.</summary>
    event EventHandler<MediaStartupWarmupChangedEventArgs>? Changed;

    /// <summary>Most recent warmup state.</summary>
    MediaStartupWarmupSnapshot Current { get; }

    /// <summary>
    /// Queues the tiered startup warmup if one is not already running: slide-action media in the
    /// selected presentation/playlist first (priority), then the rest of the media library.
    /// </summary>
    void QueueStartupWarmup();

    /// <summary>
    /// Runs the tiered startup warmup. Tier 1 warms the retained playback slots for every slide-action
    /// media cue in the selected presentation/playlist so they start instantly when taken live; tier 2
    /// warms the remaining media-drawer library in the background.
    /// </summary>
    Task WarmStartupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tier 1 only: warms retained playback slots for slide-action media cues in the currently
    /// selected presentation or playlist.
    /// </summary>
    Task WarmSelectedContextSlideActionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a best-effort all-media warmup if one is not already running.</summary>
    void QueueWarmAllMedia();

    /// <summary>Warms every media item in root All Media and media playlists.</summary>
    Task WarmAllMediaAsync(CancellationToken cancellationToken = default);

    /// <summary>Warms the supplied media-library items without reloading the whole library.</summary>
    Task WarmMediaItemsAsync(IEnumerable<MediaLibraryItem> items, CancellationToken cancellationToken = default);

    /// <summary>Warms the slide cue record and media payloads embedded in a prepared slide cue.</summary>
    Task WarmPreparedSlideMediaAsync(PreparedSlideCue cue, CancellationToken cancellationToken = default);
}

/// <summary>
/// Warmup progress phase.
/// </summary>
public enum MediaStartupWarmupPhase
{
    Idle,
    LoadingLibrary,
    WarmingMedia,
    Completed,
    Failed,
}

/// <summary>
/// Immutable warmup status snapshot.
/// </summary>
public sealed record MediaStartupWarmupSnapshot
{
    public bool IsRunning { get; init; }

    public MediaStartupWarmupPhase Phase { get; init; } = MediaStartupWarmupPhase.Idle;

    public int TotalItems { get; init; }

    public int WarmedItems { get; init; }

    public int MissingItems { get; init; }

    public int FailedItems { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }
}

/// <summary>
/// Event payload for warmup status changes.
/// </summary>
public sealed class MediaStartupWarmupChangedEventArgs(MediaStartupWarmupSnapshot snapshot) : EventArgs
{
    public MediaStartupWarmupSnapshot Snapshot { get; } = snapshot;
}
