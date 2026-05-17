
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Machine-local global slide/media transition fallbacks configured from the Show page toolbar.
/// Injected into <see cref="PlaybackEngine"/> snapshots and read by backend frame adaptation.
/// </summary>
public interface IShowTransitionDefaults
{
    /// <summary>Fallback when slide and arrangement do not define a transition.</summary>
    SlideTransition? GlobalSlideFallback { get; }

    /// <summary>Fallback when only program media layers change (same slide/presentation chrome).</summary>
    SlideTransition? GlobalMediaFallback { get; }

    void SetGlobalSlideFallback(SlideTransition? transition);

    void SetGlobalMediaFallback(SlideTransition? transition);
}