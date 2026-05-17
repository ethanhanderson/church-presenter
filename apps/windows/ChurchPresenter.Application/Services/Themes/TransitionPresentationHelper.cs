


namespace ChurchPresenter.Services.Themes;



/// <summary>
/// Provides helper methods for arrangement-level default transitions used by authoring surfaces.
/// When no arrangement default is configured, the presentation default is considered unset and
/// runtime output falls back to the Show toolbar global slide transition.
/// </summary>
public static class TransitionPresentationHelper

{

    /// <summary>
    /// Returns the arrangement-level default transition for pickers, or <c>null</c> when unset.
    /// </summary>
    public static SlideTransition? GetDefaultTransitionForDisplay(PresentationProject? project)

    {

        if (project?.Arrangement == null)

            return null;



        var arrangementDefault = project.Arrangement.DefaultTransition;

        if (arrangementDefault == null || string.IsNullOrWhiteSpace(arrangementDefault.Type))

            return null;



        var normalized = TransitionResolver.Normalize(arrangementDefault);

        return normalized == null ? null : PresentationModelUtilities.DeepClone(normalized);

    }



    /// <summary>
    /// Returns <c>true</c> when the arrangement stores an explicit default transition.
    /// </summary>
    public static bool HasPresentationTransitionConfigured(PresentationProject? project)

    {

        var t = project?.Arrangement?.DefaultTransition;

        return t != null && !string.IsNullOrWhiteSpace(t.Type);

    }

}