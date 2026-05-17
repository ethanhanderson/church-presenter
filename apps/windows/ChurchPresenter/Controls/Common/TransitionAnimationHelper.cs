
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Controls;

/// <summary>
/// Shared storyboard builders for live output transitions so presentation and media hosts
/// interpret cut, dissolve, and custom transition keys consistently.
/// </summary>
internal static class TransitionAnimationHelper
{
    private delegate void TransitionEffectBuilder(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing);

    private static readonly Dictionary<string, TransitionEffectBuilder> TransitionRegistry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fade"] = BuildFadeEffect,
            ["dissolve"] = BuildFadeEffect,
            ["wipe"] = BuildWipeEffect,
            ["slide"] = BuildSlideEffect,
            ["zoom-in"] = BuildZoomInEffect,
            ["zoom-out"] = BuildZoomOutEffect,
        };

    public static void BuildTransitionStoryboard(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(easing);

        if (TransitionRegistry.TryGetValue(transition.Type.ToLowerInvariant(), out var builder))
            builder(storyboard, front, back, transition, duration, easing);
        else
            BuildFadeEffect(storyboard, front, back, transition, duration, easing);
    }

    public static void ResetTransforms(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

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

    public static EasingFunctionBase BuildEasingFunction(string? easing)
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

    private static void BuildFadeEffect(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        _ = transition;
        AddDoubleAnimation(storyboard, front, "Opacity", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildWipeEffect(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        EnsureScaleTransform(front);
        front.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, front, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 1, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildSlideEffect(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
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
        AddDoubleAnimation(
            storyboard,
            front,
            $"(UIElement.RenderTransform).(TranslateTransform.{target})",
            startTranslate,
            0,
            duration,
            easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 1, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildZoomInEffect(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        _ = transition;
        EnsureScaleTransform(front);
        front.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, front, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.7, 1, duration, easing);
        AddDoubleAnimation(storyboard, front, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.7, 1, duration, easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void BuildZoomOutEffect(
        Storyboard storyboard,
        UIElement front,
        UIElement back,
        SlideTransition transition,
        TimeSpan duration,
        EasingFunctionBase easing)
    {
        _ = transition;
        EnsureScaleTransform(back);
        back.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        AddDoubleAnimation(storyboard, back, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1, 1.3, duration, easing);
        AddDoubleAnimation(storyboard, back, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1, 1.3, duration, easing);
        AddDoubleAnimation(storyboard, front, "Opacity", 0, 1, duration, easing);
        AddDoubleAnimation(storyboard, back, "Opacity", 1, 0, duration, easing);
    }

    private static void AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
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
}
