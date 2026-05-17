using System.ComponentModel;

namespace ChurchPresenter.Services.Media;

/// <summary>
/// Operator transport target shown by the media controls card.
/// </summary>
public enum MediaPlaybackTarget
{
    MediaFiles,
    AudioFiles,
    Announcements,
}

/// <summary>
/// Provides a shared view of the currently active media cue playback state for transport controls
/// in the operator output panel. This coordinator is the single source of truth for position,
/// duration, and playback status across operator-preview and live output surfaces.
/// </summary>
public interface IMediaPlaybackCoordinator : INotifyPropertyChanged
{
    /// <summary>Playback lane currently shown and controlled by the operator transport card.</summary>
    MediaPlaybackTarget SelectedTransportTarget { get; set; }

    // ── Cue info ─────────────────────────────────────────────────────────────

    /// <summary>Display name of the currently active media cue, or <c>null</c> when nothing is playing.</summary>
    string? ActiveCueName { get; }

    /// <summary>Total duration in seconds, or <c>0</c> when unknown or an image cue is active.</summary>
    double Duration { get; }

    /// <summary>Current playback position in seconds.</summary>
    double Position { get; }

    /// <summary>Fraction 0–1 representing playback progress.</summary>
    double Progress { get; }

    /// <summary>Formatted display string for the current position (e.g. <c>0:32</c>).</summary>
    string PositionLabel { get; }

    /// <summary>Formatted display string for the remaining time (e.g. <c>-1:14</c>).</summary>
    string RemainingLabel { get; }

    /// <summary>Display name shown in the transport card, including the empty state label.</summary>
    string TransportCueName { get; }

    /// <summary>Slider maximum shown in the transport card. Uses a non-zero placeholder for the empty state.</summary>
    double TransportSliderMaximum { get; }

    /// <summary>Slider value shown in the transport card.</summary>
    double TransportSliderValue { get; }

    /// <summary>Elapsed time shown in the transport card.</summary>
    string TransportPositionLabel { get; }

    /// <summary>Remaining time shown in the transport card.</summary>
    string TransportRemainingLabel { get; }

    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>True while the active cue is playing.</summary>
    bool IsPlaying { get; }

    /// <summary>True when a cue is active (playing or paused).</summary>
    bool HasActiveCue { get; }

    // ── Transport ────────────────────────────────────────────────────────────

    /// <summary>Plays or resumes the active cue.</summary>
    void Play();

    /// <summary>Pauses the active cue.</summary>
    void Pause();

    /// <summary>Toggles play/pause on the active cue.</summary>
    void TogglePlayPause();

    /// <summary>Restarts the active cue from position 0.</summary>
    void Restart();

    /// <summary>Seeks forward by <paramref name="seconds"/>.</summary>
    void SeekForward(double seconds = 5);

    /// <summary>Seeks backward by <paramref name="seconds"/>.</summary>
    void SeekBackward(double seconds = 5);

    /// <summary>Seeks to the specified normalized position (0–1).</summary>
    void SeekToFraction(double fraction);

    /// <summary>Seeks directly to the specified playback position in seconds.</summary>
    void SeekToPosition(double positionSeconds);

    /// <summary>Marks the start of a user-driven scrub gesture.</summary>
    void BeginScrub();

    /// <summary>Updates the previewed scrub position in seconds while the user is dragging the transport thumb.</summary>
    void UpdateScrubPosition(double positionSeconds);

    /// <summary>Commits the current scrub position to the active playback session.</summary>
    void CommitScrubPosition(double positionSeconds);

    /// <summary>Cancels an in-progress scrub gesture and returns the UI to the live playback position.</summary>
    void CancelScrub();

    /// <summary>True while the transport UI is holding a user-driven scrub preview.</summary>
    bool IsScrubbing { get; }

}