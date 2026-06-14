namespace ChurchPresenter.Services.Pipeline;

using ChurchPresenter.Services.Media;

/// <summary>
/// Transport display snapshot for the selected playback target.
/// </summary>
public sealed record TransportDisplaySnapshot
{
    /// <summary>Whether the selected playback target has a live cue in the show.</summary>
    public bool HasActiveCue { get; init; }

    /// <summary>Whether transport controls should be enabled.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Media display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Current playback position label.</summary>
    public string PositionLabel { get; init; } = "0:00";

    /// <summary>Duration label.</summary>
    public string DurationLabel { get; init; } = "0:00";

    /// <summary>Current position in seconds.</summary>
    public double PositionSeconds { get; init; }

    /// <summary>Duration in seconds when known.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Whether media is currently playing.</summary>
    public bool IsPlaying { get; init; }
}

/// <summary>
/// Operator transport controls for playback targets in the live show.
/// </summary>
public interface IMediaTransportService
{
    /// <summary>Currently selected transport target id.</summary>
    string? SelectedTransportTarget { get; }

    /// <summary>Current transport display snapshot for the requested target.</summary>
    TransportDisplaySnapshot GetDisplay(MediaPlaybackTarget target);

    /// <summary>Starts playback on the selected target binding.</summary>
    Task PlayAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default);

    /// <summary>Pauses playback on the selected target binding.</summary>
    Task PauseAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default);

    /// <summary>Seeks forward by the configured interval.</summary>
    Task SeekForwardAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default);

    /// <summary>Seeks backward by the configured interval.</summary>
    Task SeekBackwardAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default);

    /// <summary>Restarts playback from the beginning.</summary>
    Task RestartAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default);

    /// <summary>Begins a scrub gesture.</summary>
    void BeginScrub(MediaPlaybackTarget target);

    /// <summary>Updates scrub position in seconds.</summary>
    void UpdateScrub(double positionSeconds);

    /// <summary>Commits the scrub position to the selected media binding.</summary>
    Task CommitScrubAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default);
}
