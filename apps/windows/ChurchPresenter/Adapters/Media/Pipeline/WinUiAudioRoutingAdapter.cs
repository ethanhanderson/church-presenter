#nullable enable

using ChurchPresenter.Backend.Pipeline.HostAdapter;

using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace ChurchPresenter.Adapters.Media.Pipeline;

/// <summary>
/// Audio layer routing binding backed by an audio-only <see cref="MediaPlayer"/>. The Audio layer
/// is the bottom layer of the ProPresenter stack and has no visual surface, so this binding only
/// owns transport (play/pause/seek/restart) and reports playback state for diagnostics.
/// </summary>
public sealed class WinUiAudioRoutingAdapter : IAudioRoutingBinding
{
    private readonly MediaPlayer _player = new() { IsVideoFrameServerEnabled = false, AudioCategory = MediaPlayerAudioCategory.Media };
    private string? _boundSourcePath;

    /// <summary>Initializes the audio routing binding.</summary>
    public WinUiAudioRoutingAdapter()
    {
        PlayerId = "audio-route";
    }

    /// <inheritdoc />
    public string PlayerId { get; }

    /// <inheritdoc />
    public bool IsReady { get; private set; }

    /// <inheritdoc />
    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    /// <inheritdoc />
    public TimeSpan Position => _player.PlaybackSession.Position;

    /// <inheritdoc />
    public TimeSpan? Duration
    {
        get
        {
            TimeSpan naturalDuration = _player.PlaybackSession.NaturalDuration;
            return naturalDuration.TotalSeconds > 0 ? naturalDuration : null;
        }
    }

    /// <summary>Binds an audio source file to this route.</summary>
    public async Task BindAsync(string sourcePath, bool loop, double volume, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(_boundSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            _player.Volume = Math.Clamp(volume, 0, 1);
            _player.IsLoopingEnabled = loop;
            return;
        }

        StorageFile file = await StorageFile.GetFileFromPathAsync(sourcePath).AsTask(cancellationToken).ConfigureAwait(false);
        _player.Source = MediaSource.CreateFromStorageFile(file);
        _player.IsLoopingEnabled = loop;
        _player.Volume = Math.Clamp(volume, 0, 1);
        _boundSourcePath = sourcePath;
        IsReady = true;
    }

    /// <inheritdoc />
    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.Play();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.Pause();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.PlaybackSession.Position = position;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.PlaybackSession.Position = TimeSpan.Zero;
        _player.Play();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _player.Dispose();
        return ValueTask.CompletedTask;
    }
}
