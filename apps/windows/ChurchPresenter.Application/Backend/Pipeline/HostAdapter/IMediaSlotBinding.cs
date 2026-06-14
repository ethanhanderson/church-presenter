namespace ChurchPresenter.Backend.Pipeline.HostAdapter;

/// <summary>
/// Platform media player binding for one media layer slot.
/// </summary>
public interface IMediaSlotBinding : IAsyncDisposable
{
    /// <summary>Layer slot owned by this binding.</summary>
    LayerSlotId SlotId { get; }

    /// <summary>Raised when playback position, duration, readiness, or play state changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Player id used for diagnostics.</summary>
    string PlayerId { get; }

    /// <summary>Whether the player has decoded the first frame.</summary>
    bool IsReady { get; }

    /// <summary>Whether the player is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Current playback position.</summary>
    TimeSpan Position { get; }

    /// <summary>Total media duration when known.</summary>
    TimeSpan? Duration { get; }

    /// <summary>Observed loop gap in milliseconds.</summary>
    double LoopGapMs { get; }

    /// <summary>Starts playback.</summary>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses playback.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Seeks to the requested position.</summary>
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>Restarts playback from the beginning.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);
}
