using System.ComponentModel;
using System.Runtime.CompilerServices;


using Windows.Media.Playback;

namespace ChurchPresenter.Adapters.Media;

/// <inheritdoc />
public sealed class MediaPlaybackCoordinator : IMediaPlaybackCoordinator, IMediaPlayerRegistration, IDisposable
{
    private const string EmptyTransportCueName = "No media playing";
    private const double EmptyTransportSliderMaximum = 1;
    private const string EmptyTransportTimeLabel = "--:--";

    // 50 ms gives smooth-enough timecode updates (20 Hz) without flooding the UI thread.
    // OnPositionChanged is intentionally not subscribed — it fires at frame rate (up to 60 Hz)
    // from a background thread, which would marshal up to 60 dispatcher enqueues per second
    // for a display that needs at most 20 Hz.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly StringComparer CueNameComparer = StringComparer.Ordinal;

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _pollTimer;
    private readonly object _sync = new();
    private readonly Dictionary<MediaPlaybackTarget, TargetState> _states = new()
    {
        [MediaPlaybackTarget.MediaFiles] = new TargetState(),
        [MediaPlaybackTarget.AudioFiles] = new TargetState(),
        [MediaPlaybackTarget.Announcements] = new TargetState(),
    };

    private MediaPlaybackTarget _selectedTransportTarget = MediaPlaybackTarget.MediaFiles;

    private string? _activeCueNameProp;
    private double _duration;
    private double _position;
    private bool _isPlaying;
    private bool _hasActiveCue;
    private bool _isScrubbing;
    private string _transportCueName = EmptyTransportCueName;
    private double _transportSliderMaximum = EmptyTransportSliderMaximum;
    private double _transportSliderValue;
    private string _transportPositionLabel = EmptyTransportTimeLabel;
    private string _transportRemainingLabel = EmptyTransportTimeLabel;

    public MediaPlaybackCoordinator(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _pollTimer = _dispatcher.CreateTimer();
        _pollTimer.Interval = PollInterval;
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += (_, _) => PollPosition();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MediaPlaybackTarget SelectedTransportTarget
    {
        get => _selectedTransportTarget;
        set
        {
            if (_selectedTransportTarget == value)
                return;

            lock (_sync)
            {
                ResetScrubUnsafe(SelectedStateUnsafe);
                _selectedTransportTarget = value;
                UpdatePollTimerUnsafe();
            }

            RaiseProperty();
            PublishTransportSnapshot();
        }
    }

    public string? ActiveCueName => _activeCueNameProp;

    public double Duration => _duration;

    public double Position => _position;

    public double Progress => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;

    public string PositionLabel => FormatTime(Position);

    public string RemainingLabel
    {
        get
        {
            if (Duration <= 0)
                return string.Empty;

            return $"-{FormatTime(Math.Max(0, Duration - Position))}";
        }
    }

    public string TransportCueName => _transportCueName;

    public double TransportSliderMaximum => _transportSliderMaximum;

    public double TransportSliderValue => _transportSliderValue;

    public string TransportPositionLabel => _transportPositionLabel;

    public string TransportRemainingLabel => _transportRemainingLabel;

    public bool IsPlaying => _isPlaying;

    public bool HasActiveCue => _hasActiveCue;

    public bool IsScrubbing => _isScrubbing;

    public void Play()
    {
        SafePrimaryOp(static player => player.Play());
    }

    public void Pause()
    {
        SafePrimaryOp(static player => player.Pause());
    }

    public void TogglePlayPause()
    {
        SafePrimaryOp(player =>
        {
            if (player.CurrentState == MediaPlayerState.Playing)
                player.Pause();
            else
                player.Play();
        });
    }

    public void Restart()
    {
        SafePrimarySessionOp((player, session) =>
        {
            session.Position = TimeSpan.Zero;
            player.Play();
        });
    }

    public void SeekForward(double seconds = 5)
    {
        double startPosition;
        double duration;
        lock (_sync)
        {
            var state = SelectedStateUnsafe;
            startPosition = state.IsUserScrubbing ? state.ScrubPosition : state.LivePosition;
            duration = state.LiveDuration;
        }

        SeekToPosition(ClampPosition(startPosition + seconds, duration));
    }

    public void SeekBackward(double seconds = 5)
    {
        double startPosition;
        double duration;
        lock (_sync)
        {
            var state = SelectedStateUnsafe;
            startPosition = state.IsUserScrubbing ? state.ScrubPosition : state.LivePosition;
            duration = state.LiveDuration;
        }

        SeekToPosition(ClampPosition(startPosition - seconds, duration));
    }

    public void SeekToFraction(double fraction)
    {
        double duration;
        lock (_sync)
            duration = SelectedStateUnsafe.LiveDuration;

        if (duration <= 0)
            return;

        SeekToPosition(Math.Clamp(fraction, 0, 1) * duration);
    }

    public void SeekToPosition(double positionSeconds)
    {
        double duration;
        lock (_sync)
            duration = SelectedStateUnsafe.LiveDuration;

        var target = ClampPosition(positionSeconds, duration);
        SafePrimarySessionOp((_, session) => session.Position = TimeSpan.FromSeconds(target));
    }

    public void BeginScrub()
    {
        lock (_sync)
        {
            var state = SelectedStateUnsafe;
            if (state.Primary == null)
                return;

            state.IsUserScrubbing = true;
            state.PendingSeekPosition = null;
            state.ScrubPosition = ClampPosition(state.LivePosition, state.LiveDuration);
        }

        PublishTransportSnapshot();
    }

    public void UpdateScrubPosition(double positionSeconds)
    {
        lock (_sync)
        {
            var state = SelectedStateUnsafe;
            if (!state.IsUserScrubbing)
                return;

            state.ScrubPosition = ClampPosition(positionSeconds, state.LiveDuration);
        }

        PublishTransportSnapshot();
    }

    public void CommitScrubPosition(double positionSeconds)
    {
        var shouldCommit = false;
        double target = 0;
        double duration;
        lock (_sync)
        {
            var state = SelectedStateUnsafe;
            duration = state.LiveDuration;
            if (state.IsUserScrubbing)
            {
                state.ScrubPosition = ClampPosition(positionSeconds, duration);
                target = state.ScrubPosition;
                state.IsUserScrubbing = false;
                // Hold the committed position so the UI never snaps back to the stale
                // live position while the async seek propagates to session.Position.
                state.PendingSeekPosition = target;
                shouldCommit = true;
            }
        }

        if (!shouldCommit)
            return;

        SeekToPosition(target);
    }

    public void CancelScrub()
    {
        lock (_sync)
        {
            var state = SelectedStateUnsafe;
            if (!state.IsUserScrubbing)
                return;

            ResetScrubUnsafe(state);
        }

        PublishTransportSnapshot();
    }

    public void RegisterActivePlayers(
        IReadOnlyList<MediaPlayer> players,
        string? cueName,
        MediaPlaybackRegistrationMode registrationMode,
        MediaPlaybackTarget target = MediaPlaybackTarget.MediaFiles)
    {
        if (registrationMode != MediaPlaybackRegistrationMode.Authority)
            return;

        lock (_sync)
        {
            var state = _states[target];
            UnsubscribePrimaryUnsafe(state);
            state.ActiveCueName = NormalizeCueName(cueName);
            state.Primary = players.Count > 0 ? players[0] : null;
            state.LiveDuration = 0;
            state.LivePosition = 0;
            ResetScrubUnsafe(state);
            SubscribePrimaryUnsafe(state);
            UpdatePollTimerUnsafe();
        }

        PublishTransportSnapshot();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        lock (_sync)
        {
            foreach (var state in _states.Values)
            {
                UnsubscribePrimaryUnsafe(state);
                state.Primary = null;
                state.ActiveCueName = null;
                state.LiveDuration = 0;
                state.LivePosition = 0;
                ResetScrubUnsafe(state);
            }
        }
    }

    private void SubscribePrimaryUnsafe(TargetState state)
    {
        if (state.Primary == null)
            return;

        try
        {
            state.Primary.CurrentStateChanged += OnPlayerStateChanged;
            state.Primary.SourceChanged += OnPlayerSourceChanged;
            // PositionChanged is intentionally NOT subscribed. It fires at playback frame rate
            // (~30–60 Hz) from a background thread and would create excessive dispatcher marshaling.
            // The 50 ms poll timer handles position updates at a UI-appropriate rate (20 Hz).
        }
        catch (ObjectDisposedException)
        {
            state.Primary = null;
        }
        catch (Exception)
        {
            state.Primary = null;
        }
    }

    private void UnsubscribePrimaryUnsafe(TargetState state)
    {
        if (state.Primary == null)
            return;

        try
        {
            state.Primary.CurrentStateChanged -= OnPlayerStateChanged;
            state.Primary.SourceChanged -= OnPlayerSourceChanged;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void OnPlayerStateChanged(MediaPlayer sender, object args) => PublishTransportSnapshot();

    private void OnPlayerSourceChanged(MediaPlayer sender, object args) => PublishTransportSnapshot();

    private void PollPosition() => PublishTransportSnapshot();

    private void PublishTransportSnapshot()
    {
        TransportSnapshot snapshot;
        lock (_sync)
            snapshot = CaptureSnapshotUnsafe(SelectedStateUnsafe);

        if (_dispatcher.HasThreadAccess)
            ApplySnapshot(snapshot);
        else
            _dispatcher.TryEnqueue(() => ApplySnapshot(snapshot));
    }

    private TransportSnapshot CaptureSnapshotUnsafe(TargetState state)
    {
        if (state.Primary?.PlaybackSession is not { } session)
        {
            state.LiveDuration = 0;
            state.LivePosition = 0;
            ResetScrubUnsafe(state);
            return new TransportSnapshot(state.ActiveCueName, false, false, 0, 0, false);
        }

        state.LiveDuration = Math.Max(0, session.NaturalDuration.TotalSeconds);
        state.LivePosition = ClampPosition(session.Position.TotalSeconds, state.LiveDuration);
        state.ScrubPosition = ClampPosition(state.ScrubPosition, state.LiveDuration);

        // Clear the pending seek once the player's reported position has converged to the
        // committed target (within 200 ms tolerance) or when at the end of the file.
        if (state.PendingSeekPosition.HasValue)
        {
            if (Math.Abs(state.LivePosition - state.PendingSeekPosition.Value) <= 0.2 ||
                state.LiveDuration > 0 && state.PendingSeekPosition.Value >= state.LiveDuration)
            {
                state.PendingSeekPosition = null;
            }
        }

        double displayPosition;
        if (state.IsUserScrubbing)
            displayPosition = state.ScrubPosition;
        else if (state.PendingSeekPosition.HasValue)
            displayPosition = state.PendingSeekPosition.Value;
        else
            displayPosition = state.LivePosition;

        return new TransportSnapshot(
            state.ActiveCueName,
            true,
            state.Primary.CurrentState == MediaPlayerState.Playing,
            state.LiveDuration,
            displayPosition,
            state.IsUserScrubbing);
    }

    private void ApplySnapshot(TransportSnapshot snapshot)
    {
        var cueNameChanged = !CueNameComparer.Equals(_activeCueNameProp, snapshot.CueName);
        var hasActiveCueChanged = _hasActiveCue != snapshot.HasActiveCue;
        var isPlayingChanged = _isPlaying != snapshot.IsPlaying;
        var durationChanged = Math.Abs(_duration - snapshot.Duration) > 0.0005;
        var positionChanged = Math.Abs(_position - snapshot.DisplayPosition) > 0.0005;
        var isScrubbingChanged = _isScrubbing != snapshot.IsUserScrubbing;
        var nextTransportCueName = snapshot.HasActiveCue
            ? snapshot.CueName ?? EmptyTransportCueName
            : EmptyTransportCueName;
        var nextTransportSliderMaximum = snapshot.HasActiveCue && snapshot.Duration > 0
            ? snapshot.Duration
            : EmptyTransportSliderMaximum;
        var nextTransportSliderValue = snapshot.HasActiveCue
            ? ClampPosition(snapshot.DisplayPosition, nextTransportSliderMaximum)
            : 0;
        var nextTransportPositionLabel = snapshot.HasActiveCue
            ? FormatTime(snapshot.DisplayPosition)
            : EmptyTransportTimeLabel;
        var nextTransportRemainingLabel = snapshot.HasActiveCue && snapshot.Duration > 0
            ? $"-{FormatTime(Math.Max(0, snapshot.Duration - snapshot.DisplayPosition))}"
            : EmptyTransportTimeLabel;
        var transportCueNameChanged = !CueNameComparer.Equals(_transportCueName, nextTransportCueName);
        var transportSliderMaximumChanged = Math.Abs(_transportSliderMaximum - nextTransportSliderMaximum) > 0.0005;
        var transportSliderValueChanged = Math.Abs(_transportSliderValue - nextTransportSliderValue) > 0.0005;
        var transportPositionLabelChanged = !CueNameComparer.Equals(_transportPositionLabel, nextTransportPositionLabel);
        var transportRemainingLabelChanged = !CueNameComparer.Equals(_transportRemainingLabel, nextTransportRemainingLabel);

        _activeCueNameProp = snapshot.CueName;
        _hasActiveCue = snapshot.HasActiveCue;
        _isPlaying = snapshot.IsPlaying;
        _duration = snapshot.Duration;
        _position = snapshot.DisplayPosition;
        _isScrubbing = snapshot.IsUserScrubbing;
        _transportCueName = nextTransportCueName;
        _transportSliderMaximum = nextTransportSliderMaximum;
        _transportSliderValue = nextTransportSliderValue;
        _transportPositionLabel = nextTransportPositionLabel;
        _transportRemainingLabel = nextTransportRemainingLabel;

        if (cueNameChanged)
            RaiseProperty(nameof(ActiveCueName));
        if (hasActiveCueChanged)
            RaiseProperty(nameof(HasActiveCue));
        if (isPlayingChanged)
            RaiseProperty(nameof(IsPlaying));
        if (durationChanged)
            RaiseProperty(nameof(Duration));
        if (positionChanged)
            RaiseProperty(nameof(Position));
        if (isScrubbingChanged)
            RaiseProperty(nameof(IsScrubbing));
        if (durationChanged || positionChanged)
        {
            RaiseProperty(nameof(Progress));
            RaiseProperty(nameof(PositionLabel));
            RaiseProperty(nameof(RemainingLabel));
        }
        if (transportCueNameChanged)
            RaiseProperty(nameof(TransportCueName));
        if (transportSliderMaximumChanged)
            RaiseProperty(nameof(TransportSliderMaximum));
        if (transportSliderValueChanged)
            RaiseProperty(nameof(TransportSliderValue));
        if (transportPositionLabelChanged)
            RaiseProperty(nameof(TransportPositionLabel));
        if (transportRemainingLabelChanged)
            RaiseProperty(nameof(TransportRemainingLabel));
    }

    private void SafePrimaryOp(Action<MediaPlayer> action)
    {
        try
        {
            MediaPlayer? primary;
            lock (_sync)
                primary = SelectedStateUnsafe.Primary;

            if (primary == null)
                return;

            action(primary);
            PublishTransportSnapshot();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void SafePrimarySessionOp(Action<MediaPlayer, MediaPlaybackSession> action)
    {
        try
        {
            MediaPlayer? primary;
            MediaPlaybackSession? session;
            lock (_sync)
            {
                primary = SelectedStateUnsafe.Primary;
                session = primary?.PlaybackSession;
            }

            if (primary == null || session == null)
                return;

            action(primary, session);
            PublishTransportSnapshot();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void RaiseProperty([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string? NormalizeCueName(string? cueName) =>
        string.IsNullOrWhiteSpace(cueName) ? null : cueName.Trim();

    private TargetState SelectedStateUnsafe => _states[_selectedTransportTarget];

    private void UpdatePollTimerUnsafe()
    {
        if (_states.Values.Any(static state => state.Primary != null))
            _pollTimer.Start();
        else
            _pollTimer.Stop();
    }

    private static void ResetScrubUnsafe(TargetState state)
    {
        state.IsUserScrubbing = false;
        state.ScrubPosition = 0;
        state.PendingSeekPosition = null;
    }

    private static double ClampPosition(double positionSeconds, double durationSeconds)
    {
        if (durationSeconds > 0)
            return Math.Clamp(positionSeconds, 0, durationSeconds);

        return Math.Max(0, positionSeconds);
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private sealed class TargetState
    {
        public MediaPlayer? Primary { get; set; }

        public string? ActiveCueName { get; set; }

        public double LiveDuration { get; set; }

        public double LivePosition { get; set; }

        public bool IsUserScrubbing { get; set; }

        public double ScrubPosition { get; set; }

        // After CommitScrubPosition, the async seek may take one or two poll cycles to reflect in
        // session.Position. PendingSeekPosition holds the committed target so the UI shows that
        // value immediately, preventing a brief snap-back to the pre-seek position.
        public double? PendingSeekPosition { get; set; }
    }

    private readonly record struct TransportSnapshot(
        string? CueName,
        bool HasActiveCue,
        bool IsPlaying,
        double Duration,
        double DisplayPosition,
        bool IsUserScrubbing);
}