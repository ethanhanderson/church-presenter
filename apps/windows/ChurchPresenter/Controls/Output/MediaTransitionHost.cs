
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.Media.Playback;

namespace ChurchPresenter.Controls;

/// <summary>
/// Animates changes to a live output media slot using the resolved global/custom media transition.
/// </summary>
public sealed class MediaTransitionHost : UserControl
{
    // #region agent log
    private static void DbgLog(string hyp, string loc, string msg, string dataJson)
        => OutputMediaSlotView.DbgLog(hyp, loc, msg, dataJson);
    // #endregion

    public static readonly DependencyProperty MediaProperty =
        DependencyProperty.Register(nameof(Media), typeof(OutputLayerMedia), typeof(MediaTransitionHost),
            new PropertyMetadata(null, OnMediaPropertyChanged));

    public static readonly DependencyProperty TransitionProperty =
        DependencyProperty.Register(nameof(Transition), typeof(SlideTransition), typeof(MediaTransitionHost),
            new PropertyMetadata(null, OnMediaPropertyChanged));

    public static readonly DependencyProperty PlaceholderBrushProperty =
        DependencyProperty.Register(nameof(PlaceholderBrush), typeof(Brush), typeof(MediaTransitionHost),
            new PropertyMetadata(null, OnPlaceholderBrushChanged));

    private readonly Grid _host;
    private readonly Border _layerBackdrop;
    private readonly OutputMediaSlotView _fromView;
    private readonly OutputMediaSlotView _toView;
    private readonly Queue<MediaTransitionState> _pendingStates = new();
    private Storyboard? _runningStoryboard;
    private bool _isSwapped;
    private bool _playPending;
    private bool _isPreparingTransition;
    private string? _visibleSignature;
    private string? _visibleMediaSignature;
    private string? _queuedSignature;
    private string? _transitioningSignature;

    /// <summary>
    /// Raised whenever the set or ordering of active players changes so outer hosts can keep shared
    /// transport controls pointed at the current media-layer player.
    /// </summary>
    public event EventHandler? ActivePlayersChanged;

    /// <summary>Initializes a new instance of the <see cref="MediaTransitionHost"/> control.</summary>
    public MediaTransitionHost()
    {
        _host = new Grid();
        _layerBackdrop = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _fromView = new OutputMediaSlotView();
        _toView = new OutputMediaSlotView { Opacity = 0 };

        _host.Children.Add(_layerBackdrop);
        _host.Children.Add(_fromView);
        _host.Children.Add(_toView);
        ApplyPlaceholderBrush();
        Content = _host;
    }

    /// <summary>The current media payload for the transitioned slot.</summary>
    public OutputLayerMedia? Media
    {
        get => (OutputLayerMedia?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    /// <summary>The resolved transition to apply when the media payload changes.</summary>
    public SlideTransition? Transition
    {
        get => (SlideTransition?)GetValue(TransitionProperty);
        set => SetValue(TransitionProperty, value);
    }

    public Brush? PlaceholderBrush
    {
        get => (Brush?)GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    /// <summary>
    /// Pre-warms the hidden back-buffer slot with <paramref name="media"/> so that when the operator
    /// triggers this media next the source is already open and <see cref="OutputMediaSlotView.WaitForReadyAsync"/>
    /// returns immediately.  Ignored during active transitions to avoid disrupting the pipeline.
    /// </summary>
    public void PreWarmMedia(OutputLayerMedia? media)
    {
        if (media == null || _isPreparingTransition || _runningStoryboard != null)
            return;

        HiddenView.Media = media;
    }

    /// <summary>All active players currently participating in the media transition.</summary>
    public IReadOnlyList<MediaPlayer> ActivePlayers =>
        GetOrderedViewsForPlaybackCoordinator()
            .Select(static view => view.ActivePlayer)
            .Where(static player => player != null)
            .Cast<MediaPlayer>()
            .ToArray();

    private OutputMediaSlotView VisibleView => _isSwapped ? _toView : _fromView;
    private OutputMediaSlotView HiddenView => _isSwapped ? _fromView : _toView;

    private IReadOnlyList<OutputMediaSlotView> GetOrderedViewsForPlaybackCoordinator()
    {
        // While preparing or running a transition, the hidden view contains the incoming media that should
        // own the operator transport controls. Once the transition settles, the visible view is current.
        return (_isPreparingTransition || _runningStoryboard != null || _transitioningSignature != null)
            ? new[] { HiddenView, VisibleView }
            : new[] { VisibleView, HiddenView };
    }

    private static void OnMediaPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is MediaTransitionHost host)
            host.ScheduleTransition();
    }

    private static void OnPlaceholderBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is MediaTransitionHost host)
            host.ApplyPlaceholderBrush();
    }

    private void ScheduleTransition()
    {
        if (_playPending)
            return;

        _playPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _playPending = false;
            EnqueueCurrentState();
        });
    }

    private void ApplyPlaceholderBrush()
    {
        _layerBackdrop.Background = PlaceholderBrush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _fromView.PlaceholderBrush = PlaceholderBrush;
        _toView.PlaceholderBrush = PlaceholderBrush;
    }

    private void EnqueueCurrentState()
    {
        var state = BuildState();
        if (ShouldIgnore(state))
            return;

        _pendingStates.Enqueue(state);
        _queuedSignature = state.Signature;

        if (_runningStoryboard == null && !_isPreparingTransition)
            PlayNextTransition();
    }

    private MediaTransitionState BuildState()
    {
        var mediaSignature = BuildMediaSignature(Media);
        var effectiveTransition = ResolveTransition(Media, Transition);
        var transitionFingerprint = BuildTransitionFingerprint(effectiveTransition);
        var signature = string.Join("\n", mediaSignature, transitionFingerprint);

        // Only animate when the media content itself changes. Transition-setting-only changes
        // (same media, different global transition config) are absorbed so they take effect on
        // the next actual media change instead of re-triggering a transition of the same content.
        SlideTransition? transition = null;
        if (_visibleMediaSignature != null
            && !string.Equals(_visibleMediaSignature, mediaSignature, StringComparison.Ordinal))
        {
            transition = CloneTransition(effectiveTransition);
        }

        return new MediaTransitionState(Media == null ? null : CloneMedia(Media), transition, signature, mediaSignature, transitionFingerprint);
    }

    private bool ShouldIgnore(MediaTransitionState state)
    {
        // Exact-match dedup: already queued or mid-transition with the same full state.
        if (string.Equals(_queuedSignature, state.Signature, StringComparison.Ordinal)
            || string.Equals(_transitioningSignature, state.Signature, StringComparison.Ordinal))
            return true;

        // Content-only dedup: if idle and the same media is already visible, skip even when
        // the transition fingerprint changed (new transition settings apply on the next content change).
        return _runningStoryboard == null
               && _pendingStates.Count == 0
               && !_isPreparingTransition
               && string.Equals(_visibleMediaSignature, state.MediaSignature, StringComparison.Ordinal);
    }

    private async void PlayNextTransition()
    {
        if (_isPreparingTransition)
            return;

        if (_pendingStates.Count == 0)
        {
            _queuedSignature = null;
            return;
        }

        _isPreparingTransition = true;
        var state = _pendingStates.Dequeue();
        if (_pendingStates.Count == 0)
            _queuedSignature = null;

        _transitioningSignature = state.Signature;

        var front = _isSwapped ? _fromView : _toView;
        var back = _isSwapped ? _toView : _fromView;

        // #region agent log
        var preWarmed = front.Media != null
            && string.Equals(front.Media.MediaId, state.Media?.MediaId, StringComparison.Ordinal);
        var transitionType = state.Transition?.Type ?? "cut";
        var transitionDur = state.Transition?.Duration ?? 0;
        DbgLog("H-2", "MediaTransitionHost.cs:PlayNextTransition",
            "transition-start",
            $"{{\"preWarmed\":{(preWarmed ? "true" : "false")},\"frontPlayerState\":\"{front.ActivePlayer?.PlaybackSession.PlaybackState}\",\"transitionType\":\"{transitionType}\",\"transitionDurMs\":{transitionDur},\"mediaId\":\"{state.Media?.MediaId?.Replace("\\", "\\\\")??"null"}\"}}");
        // #endregion

        ApplyState(front, state);
        RaiseActivePlayersChanged();
        await front.WaitForReadyAsync().ConfigureAwait(true);

        var transition = state.Transition;
        if (transition == null || string.Equals(transition.Type, "cut", StringComparison.OrdinalIgnoreCase))
        {
            front.BeginRevealPlayback();
            front.Opacity = 1;
            back.Opacity = 0;
            TransitionAnimationHelper.ResetTransforms(front);
            TransitionAnimationHelper.ResetTransforms(back);
            _isSwapped = !_isSwapped;
            _visibleSignature = state.Signature;
            _visibleMediaSignature = state.MediaSignature;
            _transitioningSignature = null;
            _isPreparingTransition = false;
            ClearHiddenView();
            RaiseActivePlayersChanged();
            PlayNextTransition();
            return;
        }

        if (state.Media == null)
        {
            front.Opacity = 1;
            back.Opacity = 1;
            TransitionAnimationHelper.ResetTransforms(front);
            TransitionAnimationHelper.ResetTransforms(back);

            var clearDuration = TimeSpan.FromMilliseconds(transition.Duration > 0 ? transition.Duration : 400);
            var clearEasing = TransitionAnimationHelper.BuildEasingFunction(transition.Easing);
            var clearStoryboard = new Storyboard();
            AddOpacityAnimation(clearStoryboard, back, 1, 0, clearDuration, clearEasing);
            clearStoryboard.Completed += (_, _) =>
            {
                front.Opacity = 1;
                back.Opacity = 0;
                TransitionAnimationHelper.ResetTransforms(front);
                TransitionAnimationHelper.ResetTransforms(back);
                _isSwapped = !_isSwapped;
                _visibleSignature = state.Signature;
                _visibleMediaSignature = state.MediaSignature;
                _transitioningSignature = null;
                _runningStoryboard = null;
                _isPreparingTransition = false;
                ClearHiddenView();
                RaiseActivePlayersChanged();
                PlayNextTransition();
            };

            _runningStoryboard = clearStoryboard;
            _isPreparingTransition = false;
            clearStoryboard.Begin();
            return;
        }

        front.Opacity = 0;
        back.Opacity = 1;
        TransitionAnimationHelper.ResetTransforms(front);
        TransitionAnimationHelper.ResetTransforms(back);
        front.BeginRevealPlayback();

        var duration = TimeSpan.FromMilliseconds(transition.Duration > 0 ? transition.Duration : 400);
        var easing = TransitionAnimationHelper.BuildEasingFunction(transition.Easing);
        var mediaInStoryboard = new Storyboard();
        AddOpacityAnimation(mediaInStoryboard, front, 0, 1, duration, easing);
        mediaInStoryboard.Completed += (_, _) =>
        {
            front.Opacity = 1;
            back.Opacity = 0;
            TransitionAnimationHelper.ResetTransforms(front);
            TransitionAnimationHelper.ResetTransforms(back);
            _isSwapped = !_isSwapped;
            _visibleSignature = state.Signature;
            _visibleMediaSignature = state.MediaSignature;
            _transitioningSignature = null;
            _runningStoryboard = null;
            _isPreparingTransition = false;
            ClearHiddenView();
            RaiseActivePlayersChanged();
            PlayNextTransition();
        };

        _runningStoryboard = mediaInStoryboard;
        _isPreparingTransition = false;
        mediaInStoryboard.Begin();
    }

    private static void ApplyState(OutputMediaSlotView view, MediaTransitionState state)
    {
        view.Media = state.Media;
    }

    private static void AddOpacityAnimation(
        Storyboard storyboard,
        UIElement target,
        double from,
        double to,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(duration),
            EasingFunction = easing,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
    }

    private void ClearHiddenView()
    {
        var hidden = HiddenView;

        hidden.Media = null;
        hidden.Opacity = 0;
        TransitionAnimationHelper.ResetTransforms(hidden);
    }

    private void RaiseActivePlayersChanged() => ActivePlayersChanged?.Invoke(this, EventArgs.Empty);

    private static string BuildMediaSignature(OutputLayerMedia? media)
    {
        if (media == null)
            return "none";

        static string Normalize(string? value) => value?.Trim() ?? string.Empty;

        return string.Join(":",
            Normalize(media.MediaId),
            Normalize(media.MediaType),
            Normalize(media.Fit),
            media.Loop,
            media.Muted,
            media.Autoplay,
            Normalize(media.ResolvedSourcePath));
    }

    private static string BuildTransitionFingerprint(SlideTransition? transition)
    {
        static string Normalize(string? value) => value?.Trim() ?? string.Empty;

        if (transition == null)
            return "none";

        return string.Join(":",
            Normalize(transition.Type).ToLowerInvariant(),
            transition.Duration,
            Normalize(transition.Easing).ToLowerInvariant(),
            string.Join(",",
                (transition.Parameters ?? new Dictionary<string, string>())
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{Normalize(pair.Key).ToLowerInvariant()}={Normalize(pair.Value)}")));
    }

    private static SlideTransition? CloneTransition(SlideTransition? source)
    {
        if (source == null)
            return null;

        return new SlideTransition
        {
            Type = source.Type,
            Duration = source.Duration,
            Easing = source.Easing,
            Parameters = source.Parameters == null
                ? null
                : new Dictionary<string, string>(source.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static OutputLayerMedia CloneMedia(OutputLayerMedia media)
    {
        return new OutputLayerMedia
        {
            MediaId = media.MediaId,
            MediaType = media.MediaType,
            DisplayName = media.DisplayName,
            Fit = media.Fit,
            Loop = media.Loop,
            Muted = media.Muted,
            Autoplay = media.Autoplay,
            Transition = CloneTransition(media.Transition),
            ResolvedSourcePath = media.ResolvedSourcePath,
        };
    }

    private static SlideTransition? ResolveTransition(OutputLayerMedia? media, SlideTransition? fallbackTransition)
    {
        var explicitTransition = TransitionResolver.Normalize(media?.Transition);
        if (explicitTransition != null)
            return explicitTransition;

        return TransitionResolver.Normalize(fallbackTransition);
    }

    private sealed class MediaTransitionState(
        OutputLayerMedia? media,
        SlideTransition? transition,
        string signature,
        string mediaSignature,
        string transitionFingerprint)
    {
        public OutputLayerMedia? Media { get; } = media;
        public SlideTransition? Transition { get; } = transition;
        /// <summary>Full state key: media content + transition fingerprint.</summary>
        public string Signature { get; } = signature;
        /// <summary>Content-only key: media fields without transition settings.</summary>
        public string MediaSignature { get; } = mediaSignature;
        public string TransitionFingerprint { get; } = transitionFingerprint;
    }
}
