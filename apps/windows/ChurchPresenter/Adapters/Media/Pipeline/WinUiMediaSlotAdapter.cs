#nullable enable

using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Services.Media;
using ChurchPresenter.Services.Pipeline;

using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace ChurchPresenter.Adapters.Media.Pipeline;

/// <summary>
/// WinUI media slot binding using gapless <see cref="MediaPlaybackList"/> looping.
/// </summary>
public sealed class WinUiMediaSlotAdapter : IMediaSlotBinding
{
    // IsVideoFrameServerEnabled must be true BEFORE any media source is opened so that
    // VideoSurfaceRenderer.CreateBrush() can receive VideoFrameAvailable callbacks at the
    // source's native framerate. Changing this property after the source is opened has no
    // effect until the source is closed and reopened (Windows Media documentation).
    private readonly MediaPlayer _player = new() { IsVideoFrameServerEnabled = true };
    private readonly MediaPlaybackList _playbackList = new()
    {
        AutoRepeatEnabled = true,
        MaxPlayedItemsToKeepOpen = 1,
    };
    private string? _boundSourcePath;
    private bool _boundLoop;
    private double _boundVolume;
    private bool _openCompleted;
    private DateTimeOffset? _loopBoundaryStartedAt;
    private TaskCompletionSource? _readyCompletion;

    /// <summary>Most recent media failure message for this slot, or null when healthy.</summary>
    public string? LastError { get; private set; }

    /// <summary>Initializes a media slot binding for one layer slot.</summary>
    public WinUiMediaSlotAdapter(LayerSlotId slotId, string playerId)
    {
        SlotId = slotId;
        PlayerId = playerId;
        _player.Source = _playbackList;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.PlaybackSession.PositionChanged += OnPlaybackPositionChanged;
        _player.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
    }

    /// <summary>Layer slot owned by this binding.</summary>
    public LayerSlotId SlotId { get; }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

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
            var naturalDuration = _player.PlaybackSession.NaturalDuration;
            return naturalDuration.TotalSeconds > 0 ? naturalDuration : null;
        }
    }

    /// <inheritdoc />
    public double LoopGapMs { get; private set; }

    /// <summary>Underlying media player for composition binding.</summary>
    public MediaPlayer Player => _player;

    /// <summary>Returns whether this retained player is already bound to the requested playback identity.</summary>
    public bool IsBoundTo(string sourcePath, bool loop) =>
        _playbackList.Items.Count > 0
        && string.Equals(_boundSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
        && _boundLoop == loop;

    /// <summary>Prepares playback for the requested source.</summary>
    public Task BindAsync(string sourcePath, bool loop, double volume, CancellationToken cancellationToken = default) =>
        BindAsync(sourcePath, loop, volume, startPlayback: true, cancellationToken);

    /// <summary>Prepares playback with optional deferred start for XAML external-video attach.</summary>
    public async Task BindAsync(
        string sourcePath,
        bool loop,
        double volume,
        bool startPlayback,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        double normalizedVolume = Math.Clamp(volume, 0, 1);
        if (_playbackList.Items.Count > 0
            && string.Equals(_boundSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
            && _boundLoop == loop)
        {
            _boundVolume = normalizedVolume;
            _player.Volume = normalizedVolume;
            if (startPlayback)
            {
                _player.Play();
            }
            else
            {
                _player.Pause();
                _player.PlaybackSession.Position = TimeSpan.Zero;
            }
            TryMarkReady();
            RaiseStateChanged();
            return;
        }

        IsReady = false;
        _openCompleted = false;
        _boundSourcePath = null;
        _boundLoop = loop;
        _boundVolume = normalizedVolume;

        StorageFile file = await StorageFile.GetFileFromPathAsync(sourcePath).AsTask(cancellationToken)
            .ConfigureAwait(false);
        MediaSource source = MediaSource.CreateFromStorageFile(file);
        MediaPlaybackItem item = new(source);

        _playbackList.AutoRepeatEnabled = loop;
        _playbackList.ItemOpened -= OnPlaybackListItemOpened;
        _playbackList.ItemOpened += OnPlaybackListItemOpened;
        _playbackList.Items.Clear();
        _playbackList.Items.Add(item);
        _player.Volume = normalizedVolume;
        _boundSourcePath = sourcePath;
        if (startPlayback)
        {
            _player.Play();
        }
        else
        {
            _player.Pause();
            _player.PlaybackSession.Position = TimeSpan.Zero;
        }
        RaiseStateChanged();
    }

    /// <summary>Preloads the requested source into this retained slot without leaving playback running.</summary>
    public async Task PreloadAsync(
        string sourcePath,
        bool loop,
        double volume,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsBoundTo(sourcePath, loop) && IsReady)
        {
            return;
        }

        LastError = null;
        await BindAsync(sourcePath, loop, volume, cancellationToken).ConfigureAwait(false);

        try
        {
            if (!IsReady)
                await WaitUntilReadyAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A decoder that never reaches its first frame within the budget must not abort the cue
            // or block the operator. Leave the slot un-ready (the video layer stays blank) and let
            // the rest of the frame apply; transport/diagnostics observe IsReady/LastError.
            LastError ??= "Media failed to reach first frame within the preload timeout.";
        }
        finally
        {
            _player.Pause();
            _player.PlaybackSession.Position = TimeSpan.Zero;
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.Play();
        RaiseStateChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.Pause();
        RaiseStateChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.PlaybackSession.Position = position;
        RaiseStateChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _player.PlaybackSession.Position = TimeSpan.Zero;
        _player.Play();
        RaiseStateChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        _player.PlaybackSession.PositionChanged -= OnPlaybackPositionChanged;
        _player.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
        _playbackList.ItemOpened -= OnPlaybackListItemOpened;
        _player.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void OnPlaybackListItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
    {
        _openCompleted = true;
        TryMarkReady();
        RaiseStateChanged();
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        if (sender.PlaybackState == MediaPlaybackState.Playing)
            TryMarkReady();
        RaiseStateChanged();
    }

    private void OnPlaybackPositionChanged(MediaPlaybackSession sender, object args) =>
        RaiseStateChanged();

    private void OnNaturalDurationChanged(MediaPlaybackSession sender, object args) =>
        RaiseStateChanged();

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _ = sender;
        // Capture the failure and release any pending readiness wait immediately so a corrupt file,
        // missing codec, or decode error degrades the slot gracefully instead of stalling the
        // take-live for the full ready timeout. The video layer simply will not paint.
        LastError = $"{args.Error}: {args.ErrorMessage}";
        _openCompleted = false;
        IsReady = false;
        _readyCompletion?.TrySetResult();
        RaiseStateChanged();
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (_loopBoundaryStartedAt == null)
            return;

        LoopGapMs = (DateTimeOffset.UtcNow - _loopBoundaryStartedAt.Value).TotalMilliseconds;
        _loopBoundaryStartedAt = DateTimeOffset.UtcNow;
        RaiseStateChanged();
    }

    private void TryMarkReady()
    {
        if (!IsPlaying)
            return;

        MediaPlaybackSession session = _player.PlaybackSession;
        bool hasOpenedMedia = _openCompleted
            || session.NaturalDuration > TimeSpan.Zero
            || session.Position > TimeSpan.Zero;
        if (!hasOpenedMedia)
            return;

        IsReady = true;
        _loopBoundaryStartedAt ??= DateTimeOffset.UtcNow;
        _readyCompletion?.TrySetResult();
    }

    private Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (IsReady)
            return Task.CompletedTask;

        _readyCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsReady)
            _readyCompletion.TrySetResult();
        return _readyCompletion.Task.WaitAsync(timeout, cancellationToken);
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, EventArgs.Empty);
}
