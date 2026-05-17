namespace ChurchPresenter.Models.Themes;

/// <summary>
/// Resolves the effective <see cref="SlideTransition"/> for a given slide in a presentation,
/// applying the precedence: slide override → arrangement default → global Show default → null (no transition).
/// </summary>
public static class TransitionResolver
{
    /// <summary>
    /// Returns the effective transition for the slide to be shown, using slide-level overrides
    /// first and falling back to the presentation's arrangement default.
    /// </summary>
    /// <param name="slide">The incoming slide. May be null when clearing or loading.</param>
    /// <param name="arrangement">The active arrangement for the presentation. May be null.</param>
    /// <param name="globalFallback">Optional Show-toolbar global slide transition when slide and arrangement omit one.</param>
    /// <returns>
    /// The resolved <see cref="SlideTransition"/>, or <c>null</c> when no transition is configured
    /// at any level.
    /// </returns>
    public static SlideTransition? Resolve(
        PresentationSlide? slide,
        PresentationArrangement? arrangement,
        SlideTransition? globalFallback = null)
    {
        var slideOverride = slide?.Animations?.Transition;
        if (slideOverride != null && !string.IsNullOrWhiteSpace(slideOverride.Type))
            return Normalize(slideOverride);

        var presentationDefault = arrangement?.DefaultTransition;
        if (presentationDefault != null && !string.IsNullOrWhiteSpace(presentationDefault.Type))
            return Normalize(presentationDefault);

        if (globalFallback != null && !string.IsNullOrWhiteSpace(globalFallback.Type))
            return Normalize(globalFallback);

        return null;
    }

    /// <summary>
    /// Normalises a raw <see cref="SlideTransition"/> for consumption by the output engine.
    /// Specifically, a <c>cut</c> transition with no meaningful animation duration is preserved as-is
    /// so the engine can execute an instant switch; all other transitions must have a positive duration.
    /// </summary>
    /// <param name="transition">The raw transition to normalise.</param>
    /// <returns>The normalised transition, or <c>null</c> if the transition is effectively no-op.</returns>
    public static SlideTransition? Normalize(SlideTransition? transition)
    {
        if (transition == null)
            return null;

        var type = transition.Type.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return null;

        // "cut" is a first-class transition — pass it through so the engine can do an instant switch.
        if (string.Equals(type, "cut", StringComparison.Ordinal))
            return transition;

        // Any other transition type needs a positive duration to be meaningful.
        if (transition.Duration <= 0)
            return null;

        return transition;
    }
}