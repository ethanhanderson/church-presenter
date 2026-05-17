using Windows.Media.Playback;

namespace ChurchPresenter.Adapters.Media;

/// <summary>
/// Declares whether a media surface drives operator transport state or only mirrors playback visually.
/// </summary>
public enum MediaPlaybackRegistrationMode
{
    Mirror,
    Authority,
}

/// <summary>
/// Extends <see cref="IMediaPlaybackCoordinator"/> with the ability to receive
/// <see cref="MediaPlayer"/> instances from the active live output hosts.
/// Kept in the WinUI project because <c>Windows.Media.Playback</c> is not
/// available in the platform-agnostic Application layer.
/// </summary>
public interface IMediaPlayerRegistration
{
    /// <summary>
    /// Registers a new set of active media players from the live output compositor.
    /// Pass an empty collection to signal no active cue.
    /// </summary>
    void RegisterActivePlayers(
        IReadOnlyList<MediaPlayer> players,
        string? cueName,
        MediaPlaybackRegistrationMode registrationMode,
        MediaPlaybackTarget target = MediaPlaybackTarget.MediaFiles);
}