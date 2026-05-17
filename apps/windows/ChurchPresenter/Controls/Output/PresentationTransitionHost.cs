using System.Linq;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Controls;

/// <summary>
/// Animates between presentation-only output scenes while engine media remains hosted outside this control.
/// </summary>
public sealed class PresentationTransitionHost : UserControl
{
    public static readonly DependencyProperty ProjectProperty =
        DependencyProperty.Register(nameof(Project), typeof(PresentationProject), typeof(PresentationTransitionHost),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty SlideProperty =
        DependencyProperty.Register(nameof(Slide), typeof(PresentationSlide), typeof(PresentationTransitionHost),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty VisibleLayerIdsProperty =
        DependencyProperty.Register(nameof(VisibleLayerIds), typeof(IEnumerable<string>), typeof(PresentationTransitionHost),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty SuppressPresentationProperty =
        DependencyProperty.Register(nameof(SuppressPresentation), typeof(bool), typeof(PresentationTransitionHost),
            new PropertyMetadata(false, OnPresentationPropertyChanged));

    public static readonly DependencyProperty DefaultTransitionProperty =
        DependencyProperty.Register(nameof(DefaultTransition), typeof(SlideTransition), typeof(PresentationTransitionHost),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty OutputAspectRatioOverrideProperty =
        DependencyProperty.Register(nameof(OutputAspectRatioOverride), typeof(string), typeof(PresentationTransitionHost),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    public static readonly DependencyProperty OutputStageStretchProperty =
        DependencyProperty.Register(nameof(OutputStageStretch), typeof(Stretch), typeof(PresentationTransitionHost),
            new PropertyMetadata(Stretch.Uniform, OnLayoutPropertyChanged));

    private readonly Grid _host;
    private readonly SlideStageView _fromView;
    private readonly SlideStageView _toView;
    private readonly Queue<TransitionFrameState> _pendingStates = new();
    private Storyboard? _runningStoryboard;
    private bool _isSwapped;
    private bool _playPending;
    private string? _visibleSignature;
    private string? _visibleContentSignature;
    private string? _queuedSignature;
    private string? _transitioningSignature;

    /// <summary>Initializes a new instance of the <see cref="PresentationTransitionHost"/> control.</summary>
    public PresentationTransitionHost()
    {
        _host = new Grid();
        _fromView = CreateStageView();
        _toView = CreateStageView();
        _toView.Opacity = 0;

        _host.Children.Add(_fromView);
        _host.Children.Add(_toView);
        Content = _host;

        ApplyOutputLayout(_fromView);
        ApplyOutputLayout(_toView);
    }

    /// <summary>The active presentation project.</summary>
    public PresentationProject? Project
    {
        get => (PresentationProject?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    /// <summary>The active presentation slide.</summary>
    public PresentationSlide? Slide
    {
        get => (PresentationSlide?)GetValue(SlideProperty);
        set => SetValue(SlideProperty, value);
    }

    /// <summary>Visible layer ids for the current build step.</summary>
    public IEnumerable<string>? VisibleLayerIds
    {
        get => (IEnumerable<string>?)GetValue(VisibleLayerIdsProperty);
        set => SetValue(VisibleLayerIdsProperty, value);
    }

    /// <summary>Whether presentation content is currently suppressed.</summary>
    public bool SuppressPresentation
    {
        get => (bool)GetValue(SuppressPresentationProperty);
        set => SetValue(SuppressPresentationProperty, value);
    }

    /// <summary>Transition to apply for presentation changes.</summary>
    public SlideTransition? DefaultTransition
    {
        get => (SlideTransition?)GetValue(DefaultTransitionProperty);
        set => SetValue(DefaultTransitionProperty, value);
    }

    /// <summary>Optional output aspect ratio override.</summary>
    public string? OutputAspectRatioOverride
    {
        get => (string?)GetValue(OutputAspectRatioOverrideProperty);
        set => SetValue(OutputAspectRatioOverrideProperty, value);
    }

    /// <summary>Output stretch mode.</summary>
    public Stretch OutputStageStretch
    {
        get => (Stretch)GetValue(OutputStageStretchProperty);
        set => SetValue(OutputStageStretchProperty, value);
    }

    private static void OnPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is PresentationTransitionHost host)
            host.ScheduleTransition();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is PresentationTransitionHost host)
        {
            host.ApplyOutputLayout(host._fromView);
            host.ApplyOutputLayout(host._toView);
        }
    }

    private SlideStageView CreateStageView()
    {
        return new SlideStageView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderMode = SlideStageRenderMode.Output,
            TrackPlaybackCoordinator = false,
            SuppressMedia = true,
        };
    }

    private void ApplyOutputLayout(SlideStageView view)
    {
        view.OutputAspectRatioOverride = OutputAspectRatioOverride;
        view.OutputStageStretch = OutputStageStretch;
        view.RenderMode = SlideStageRenderMode.Output;
        view.TrackPlaybackCoordinator = false;
        view.SuppressMedia = true;
        view.IsBlackout = false;
        view.IsClear = false;
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

    private void EnqueueCurrentState()
    {
        var state = BuildState();
        if (ShouldIgnore(state))
            return;

        _pendingStates.Enqueue(state);
        _queuedSignature = state.Signature;

        if (_runningStoryboard == null)
            PlayNextTransition();
    }

    private TransitionFrameState BuildState()
    {
        var visible = VisibleLayerIds?.ToArray() ?? Array.Empty<string>();

        // Sort for signature: SlideStageView renders layers in slide-defined order regardless of
        // the VisibleLayerIds list order, so {A,B} and {B,A} produce identical output.
        var sortedVisible = visible.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var contentSignature = string.Join("|",
            PresentationModelUtilities.StablePresentationKey(Project),
            Slide?.Id ?? string.Empty,
            string.Join(",", sortedVisible),
            SuppressPresentation,
            OutputAspectRatioOverride ?? string.Empty,
            OutputStageStretch);
        var transitionFingerprint = BuildTransitionFingerprint(DefaultTransition);
        var signature = string.Join("\n", contentSignature, transitionFingerprint);

        // Only animate when slide content itself changes. Transition-setting-only changes
        // (same slide, new global transition config) are absorbed so they take effect on
        // the next actual slide change instead of re-triggering an animation of the same content.
        SlideTransition? transition;
        if (_visibleContentSignature == null
            || string.Equals(_visibleContentSignature, contentSignature, StringComparison.Ordinal))
        {
            transition = null;
        }
        else
        {
            transition = CloneTransition(DefaultTransition);
        }

        return new TransitionFrameState(
            Project,
            Slide,
            visible,
            SuppressPresentation,
            transition,
            signature,
            contentSignature,
            transitionFingerprint);
    }

    private bool ShouldIgnore(TransitionFrameState state)
    {
        // Exact-match dedup: already queued or mid-transition with the same full state.
        if (string.Equals(_queuedSignature, state.Signature, StringComparison.Ordinal)
            || string.Equals(_transitioningSignature, state.Signature, StringComparison.Ordinal))
            return true;

        // Content-only dedup: if idle and the same slide content is already visible, skip even
        // when the transition fingerprint changed (new settings apply on the next content change).
        return _runningStoryboard == null
               && _pendingStates.Count == 0
               && string.Equals(_visibleContentSignature, state.ContentSignature, StringComparison.Ordinal);
    }

    private void PlayNextTransition()
    {
        if (_pendingStates.Count == 0)
        {
            _queuedSignature = null;
            return;
        }

        var state = _pendingStates.Dequeue();
        if (_pendingStates.Count == 0)
            _queuedSignature = null;

        _transitioningSignature = state.Signature;

        var front = _isSwapped ? _fromView : _toView;
        var back = _isSwapped ? _toView : _fromView;
        ApplyState(front, state);

        var transition = state.Transition;
        if (transition == null || string.Equals(transition.Type, "cut", StringComparison.OrdinalIgnoreCase))
        {
            front.Opacity = 1;
            back.Opacity = 0;
            ResetTransforms(front);
            ResetTransforms(back);
            _isSwapped = !_isSwapped;
            _visibleSignature = state.Signature;
            _visibleContentSignature = state.ContentSignature;
            _transitioningSignature = null;
            PlayNextTransition();
            return;
        }

        front.Opacity = 0;
        ResetTransforms(front);

        var duration = TimeSpan.FromMilliseconds(transition.Duration > 0 ? transition.Duration : 400);
        var easing = BuildEasingFunction(transition.Easing);
        var storyboard = new Storyboard();
        BuildTransitionStoryboard(storyboard, front, back, transition, duration, easing);
        storyboard.Completed += (_, _) =>
        {
            front.Opacity = 1;
            back.Opacity = 0;
            ResetTransforms(front);
            ResetTransforms(back);
            _isSwapped = !_isSwapped;
            _visibleSignature = state.Signature;
            _visibleContentSignature = state.ContentSignature;
            _transitioningSignature = null;
            _runningStoryboard = null;
            PlayNextTransition();
        };

        _runningStoryboard = storyboard;
        storyboard.Begin();
    }

    private void ApplyState(SlideStageView view, TransitionFrameState state)
    {
        view.Project = state.Project;
        view.Slide = state.Slide;
        view.VisibleLayerIds = state.VisibleLayerIds;
        view.MediaLayers = null;
        view.SuppressPresentation = state.SuppressPresentation;
        view.SuppressMedia = true;
        view.IsBlackout = false;
        view.IsClear = false;
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

    private delegate void TransitionEffectBuilder(
        Storyboard storyboard,
        SlideStageView front,
        SlideStageView back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing);

    private static readonly Dictionary<string, TransitionEffectBuilder> TransitionRegistry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fade"] = BuildFadeEffect,
            ["wipe"] = BuildWipeEffect,
            ["slide"] = BuildSlideEffect,
            ["zoom-in"] = BuildZoomInEffect,
            ["zoom-out"] = BuildZoomOutEffect,
        };

    private static void BuildTransitionStoryboard(
        Storyboard storyboard,
        SlideStageView front,
        SlideStageView back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        if (TransitionRegistry.TryGetValue(transition.Type.ToLowerInvariant(), out var builder))
            builder(storyboard, front, back, transition, duration, easing);
        else
            BuildFadeEffect(storyboard, front, back, transition, duration, easing);
    }

    private static void BuildFadeEffect(Storyboard storyboard, SlideStageView front, SlideStageView back,
        SlideTransition transition, TimeSpan duration, EasingFunctionBase easing)
    {
        _ = transition;
        AddDoubleAnimation(storyboard, front, "Opacity", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildWipeEffect(Storyboard storyboard, SlideStageView front, SlideStageView back,
        SlideTransition transition, TimeSpan duration, EasingFunctionBase easing)
    {
        _ = transition;
        EnsureScaleTransform(front);
        front.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, front, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 1, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildSlideEffect(Storyboard storyboard, SlideStageView front, SlideStageView back,
        SlideTransition transition, TimeSpan duration, EasingFunctionBase easing)
    {
        EnsureTranslateTransform(front);
        EnsureTranslateTransform(back);
        var direction = transition.GetParameter("direction", "fromLeft");
        double startTranslate = direction switch
        {
            "fromRight" => 600,
            "fromTop" => -400,
            "fromBottom" => 400,
            _ => -600,
        };
        var horizontal = direction is "fromLeft" or "fromRight";
        var target = horizontal ? "TranslateX" : "TranslateY";
        front.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, front,
            $"(UIElement.RenderTransform).(TranslateTransform.{target})",
            startTranslate,
            0,
            duration,
            easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 1, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildZoomInEffect(Storyboard storyboard, SlideStageView front, SlideStageView back,
        SlideTransition transition, TimeSpan duration, EasingFunctionBase easing)
    {
        _ = transition;
        EnsureScaleTransform(front);
        front.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, front, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.7, 1, duration, easing);
        AddDoubleAnimation(storyboard, front, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.7, 1, duration, easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildZoomOutEffect(Storyboard storyboard, SlideStageView front, SlideStageView back,
        SlideTransition transition, TimeSpan duration, EasingFunctionBase easing)
    {
        _ = transition;
        EnsureScaleTransform(back);
        back.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, back, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1, 1.3, duration, easing);
        AddDoubleAnimation(storyboard, back, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1, 1.3, duration, easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void AddDoubleAnimation(Storyboard storyboard, DependencyObject target, string property,
        double from, double to, TimeSpan duration, EasingFunctionBase easing)
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
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private static void EnsureScaleTransform(UIElement element)
    {
        if (element.RenderTransform is not ScaleTransform)
            element.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
    }

    private static void EnsureTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is not TranslateTransform)
            element.RenderTransform = new TranslateTransform();
    }

    private static void ResetTransforms(UIElement element)
    {
        switch (element.RenderTransform)
        {
            case ScaleTransform scale:
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                break;
            case TranslateTransform translate:
                translate.X = 0;
                translate.Y = 0;
                break;
            case CompositeTransform composite:
                composite.ScaleX = 1;
                composite.ScaleY = 1;
                composite.TranslateX = 0;
                composite.TranslateY = 0;
                break;
        }
    }

    private static EasingFunctionBase BuildEasingFunction(string? easing)
    {
        return (easing?.Trim().ToLowerInvariant()) switch
        {
            "ease-in" => new CubicEase { EasingMode = EasingMode.EaseIn },
            "ease-out" => new CubicEase { EasingMode = EasingMode.EaseOut },
            "linear" => new SineEase { EasingMode = EasingMode.EaseInOut },
            "ease" => new QuarticEase { EasingMode = EasingMode.EaseInOut },
            "bounce" => new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 3 },
            "elastic" => new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 },
            _ => new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
    }

    private sealed class TransitionFrameState(
        PresentationProject? project,
        PresentationSlide? slide,
        IReadOnlyList<string> visibleLayerIds,
        bool suppressPresentation,
        SlideTransition? transition,
        string signature,
        string contentSignature,
        string transitionFingerprint)
    {
        public PresentationProject? Project { get; } = project;
        public PresentationSlide? Slide { get; } = slide;
        public IReadOnlyList<string> VisibleLayerIds { get; } = visibleLayerIds;
        public bool SuppressPresentation { get; } = suppressPresentation;
        public SlideTransition? Transition { get; } = transition;
        /// <summary>Full state key: slide content + transition fingerprint.</summary>
        public string Signature { get; } = signature;
        /// <summary>Content-only key: slide fields without transition settings.</summary>
        public string ContentSignature { get; } = contentSignature;
        public string TransitionFingerprint { get; } = transitionFingerprint;
    }
}
