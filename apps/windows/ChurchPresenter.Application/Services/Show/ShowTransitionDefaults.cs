
namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
public sealed class ShowTransitionDefaults : IShowTransitionDefaults
{
    private SlideTransition? _globalSlide;
    private SlideTransition? _globalMedia;

    /// <inheritdoc />
    public SlideTransition? GlobalSlideFallback => _globalSlide;

    /// <inheritdoc />
    public SlideTransition? GlobalMediaFallback => _globalMedia;

    /// <inheritdoc />
    public void SetGlobalSlideFallback(SlideTransition? transition) => _globalSlide = transition;

    /// <inheritdoc />
    public void SetGlobalMediaFallback(SlideTransition? transition) => _globalMedia = transition;
}