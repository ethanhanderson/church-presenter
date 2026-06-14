#nullable enable

using ChurchPresenter.Backend.Rendering;

using Microsoft.UI.Composition;

using System.Numerics;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Runs composition keyframe animations for layer transitions entirely off the UI thread.
/// Supports cut, fade, wipe, slide/push, and zoom with direction, easing, and duration. The
/// incoming visual animates in while the outgoing visual animates out so a slot crossfades
/// atomically. Replaces the fade-only animator in the legacy pipeline.
/// </summary>
public static class LayerTransitionAnimator
{
    /// <summary>Animates a front/back slot swap for one transition and completes when finished.</summary>
    public static Task AnimateAsync(
        Compositor compositor,
        SpriteVisual incoming,
        SpriteVisual outgoing,
        Vector2 size,
        LayerTransitionState transition)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(outgoing);

        TimeSpan duration = transition.Duration;
        bool isCut = transition.Kind is TransitionKind.Cut or TransitionKind.None;
        if (duration <= TimeSpan.Zero || isCut)
        {
            ResetVisual(incoming);
            outgoing.Opacity = 0f;
            incoming.Opacity = 1f;
            return Task.CompletedTask;
        }

        CompositionEasingFunction easing = ResolveEasing(compositor, transition.Easing);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => completion.TrySetResult(null);

        switch (transition.Kind)
        {
            case TransitionKind.Wipe:
                AnimateWipe(compositor, incoming, outgoing, size, duration, easing, transition.Direction);
                break;
            case TransitionKind.Slide:
                AnimateSlide(compositor, incoming, outgoing, size, duration, easing, transition.Direction);
                break;
            case TransitionKind.ZoomIn:
            case TransitionKind.ZoomOut:
                AnimateZoom(compositor, incoming, outgoing, size, duration, easing, transition.Kind == TransitionKind.ZoomIn);
                break;
            default:
                AnimateFade(compositor, incoming, outgoing, duration, easing);
                break;
        }

        batch.End();

        // Safety net: a CompositionScopedBatch.Completed event is NOT guaranteed to fire if the
        // animated visual is detached, the output window is hidden, or the compositor is paused
        // (app suspended/minimized). Without a fallback the returned task would never complete,
        // which stalls the apply pipeline and freezes the operator UI. Cap the await at the
        // animation duration plus a grace window; the keyframes still settle to their final values.
        return CompleteWithTimeoutAsync(completion.Task, duration + BatchCompletionGrace);
    }

    private static readonly TimeSpan BatchCompletionGrace = TimeSpan.FromSeconds(2);

    private static async Task CompleteWithTimeoutAsync(Task completion, TimeSpan timeout)
    {
        if (completion.IsCompleted)
        {
            await completion.ConfigureAwait(true);
            return;
        }

        Task winner = await Task.WhenAny(completion, Task.Delay(timeout)).ConfigureAwait(true);
        if (ReferenceEquals(winner, completion))
            await completion.ConfigureAwait(true);
    }

    private static void AnimateFade(
        Compositor compositor,
        SpriteVisual incoming,
        SpriteVisual outgoing,
        TimeSpan duration,
        CompositionEasingFunction easing)
    {
        incoming.Opacity = 0f;
        incoming.StartAnimation("Opacity", Scalar(compositor, 1f, duration, easing));
        outgoing.StartAnimation("Opacity", Scalar(compositor, 0f, duration, easing));
    }

    private static void AnimateWipe(
        Compositor compositor,
        SpriteVisual incoming,
        SpriteVisual outgoing,
        Vector2 size,
        TimeSpan duration,
        CompositionEasingFunction easing,
        TransitionDirection direction)
    {
        incoming.Opacity = 1f;
        outgoing.Opacity = 1f;
        InsetClip clip = compositor.CreateInsetClip();
        incoming.Clip = clip;

        (string property, float from) = direction switch
        {
            TransitionDirection.FromLeft => ("RightInset", size.X),
            TransitionDirection.FromRight => ("LeftInset", size.X),
            TransitionDirection.FromTop => ("BottomInset", size.Y),
            _ => ("TopInset", size.Y),
        };

        SetInset(clip, property, from);
        clip.StartAnimation(property, Scalar(compositor, 0f, duration, easing));
    }

    private static void AnimateSlide(
        Compositor compositor,
        SpriteVisual incoming,
        SpriteVisual outgoing,
        Vector2 size,
        TimeSpan duration,
        CompositionEasingFunction easing,
        TransitionDirection direction)
    {
        incoming.Opacity = 1f;
        outgoing.Opacity = 1f;

        Vector3 incomingFrom = direction switch
        {
            TransitionDirection.FromLeft => new Vector3(size.X, 0f, 0f),
            TransitionDirection.FromRight => new Vector3(-size.X, 0f, 0f),
            TransitionDirection.FromTop => new Vector3(0f, size.Y, 0f),
            _ => new Vector3(0f, -size.Y, 0f),
        };
        Vector3 outgoingTo = -incomingFrom;

        incoming.Offset = incomingFrom;
        incoming.StartAnimation("Offset", Vector(compositor, Vector3.Zero, duration, easing));
        outgoing.StartAnimation("Offset", Vector(compositor, outgoingTo, duration, easing));
    }

    private static void AnimateZoom(
        Compositor compositor,
        SpriteVisual incoming,
        SpriteVisual outgoing,
        Vector2 size,
        TimeSpan duration,
        CompositionEasingFunction easing,
        bool zoomIn)
    {
        incoming.CenterPoint = new Vector3(size.X / 2f, size.Y / 2f, 0f);
        outgoing.CenterPoint = incoming.CenterPoint;
        incoming.Opacity = 0f;

        Vector3 incomingFrom = zoomIn ? new Vector3(0.7f, 0.7f, 1f) : new Vector3(1.3f, 1.3f, 1f);
        incoming.Scale = incomingFrom;
        incoming.StartAnimation("Scale", Vector(compositor, Vector3.One, duration, easing));
        incoming.StartAnimation("Opacity", Scalar(compositor, 1f, duration, easing));
        outgoing.StartAnimation("Opacity", Scalar(compositor, 0f, duration, easing));
    }

    private static void ResetVisual(SpriteVisual visual)
    {
        visual.Clip = null;
        visual.Offset = Vector3.Zero;
        visual.Scale = Vector3.One;
    }

    private static void SetInset(InsetClip clip, string property, float value)
    {
        switch (property)
        {
            case "LeftInset": clip.LeftInset = value; break;
            case "RightInset": clip.RightInset = value; break;
            case "TopInset": clip.TopInset = value; break;
            case "BottomInset": clip.BottomInset = value; break;
        }
    }

    private static ScalarKeyFrameAnimation Scalar(
        Compositor compositor, float to, TimeSpan duration, CompositionEasingFunction easing)
    {
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, easing);
        animation.Duration = duration;
        return animation;
    }

    private static Vector3KeyFrameAnimation Vector(
        Compositor compositor, Vector3 to, TimeSpan duration, CompositionEasingFunction easing)
    {
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, easing);
        animation.Duration = duration;
        return animation;
    }

    private static CompositionEasingFunction ResolveEasing(Compositor compositor, string? easing) =>
        easing?.ToLowerInvariant() switch
        {
            "linear" => compositor.CreateLinearEasingFunction(),
            "easein" or "ease-in" => compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0f), new Vector2(1f, 1f)),
            "easeout" or "ease-out" => compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0.58f, 1f)),
            _ => compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0f), new Vector2(0.58f, 1f)),
        };

}
