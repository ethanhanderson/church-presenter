namespace ChurchPresenter.Backend.Pipeline.HostAdapter;

/// <summary>
/// Platform audio routing binding for one audio layer slot.
/// </summary>
public interface IAudioRoutingBinding : IAsyncDisposable
{
    /// <summary>Player id used for diagnostics.</summary>
    string PlayerId { get; }

    /// <summary>Whether the audio route is ready.</summary>
    bool IsReady { get; }

    /// <summary>Whether audio is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Current playback position.</summary>
    TimeSpan Position { get; }

    /// <summary>Total duration when known.</summary>
    TimeSpan? Duration { get; }

    /// <summary>Starts playback.</summary>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses playback.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Seeks to the requested position.</summary>
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>Restarts playback from the beginning.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);
}
