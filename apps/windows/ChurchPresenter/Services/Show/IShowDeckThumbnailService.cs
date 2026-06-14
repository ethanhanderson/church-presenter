using ChurchPresenter.ViewModels;

namespace ChurchPresenter.Services.Show;

/// <summary>
/// Orchestrates slide-deck thumbnail cache apply/warm for the Show page lifecycle.
/// Raster runs only when <see cref="Hosting.IShowRenderSession"/> allows it (never during frame transition).
/// </summary>
public interface IShowDeckThumbnailService
{
    /// <summary>Stops queued warm work when Show begins leaving the frame.</summary>
    void CancelWarm();

    /// <summary>
    /// Evicts cached compositor surfaces before navigation while keeping row bindings intact for the outgoing slide.
    /// </summary>
    void PrepareForNavigationThumbnails(ShowViewModel show);

    /// <summary>Releases deck thumbnail bindings after Show is off the visual tree.</summary>
    void FinalizeHiddenThumbnails(ShowViewModel show);

    /// <summary>Fallback when <see cref="FinalizeHiddenThumbnails"/> does not run from Unloaded.</summary>
    void ScheduleFinalizeAfterLeave(ShowViewModel show);

    /// <summary>Starts restoring navigation pixel snapshots on a background task (startup only).</summary>
    void BeginRestoreNavigationDisplayOnLoaded(ShowViewModel show);

    /// <summary>
    /// Restores navigation pixel snapshots after the incoming slide transition has settled.
    /// Used on cached Show revisit — never bind deck images during <see cref="Hosting.DeckThumbnailRasterPhase.Returning"/>.
    /// </summary>
    Task RestoreNavigationDisplayOnLoadedAsync(ShowViewModel show);

    /// <summary>
    /// Rasterizes into the deck cache once Show is on-frame. Does not bind compositor surfaces mid-transition.
    /// </summary>
    void BeginCachePrefetch(ShowViewModel show);

    /// <summary>Synchronously binds cached compositor, spare, or pixel clones so deck rows stay visible.</summary>
    void BindVisibleCachedThumbnails(ShowViewModel show, string moment);

    /// <summary>Marks Show off-frame after unload.</summary>
    void OnShowUnloaded(ShowViewModel show);

    /// <summary>Queue on-frame compositor refresh after Show compositor settle.</summary>
    /// <param name="show">Show view model owning deck rows.</param>
    /// <param name="cachedRevisit">When true, only binds navigation-safe display copies; compositor warm runs after preview attach.</param>
    void OnShowCompositorReady(ShowViewModel show, bool cachedRevisit = false);

    /// <summary>
    /// Warms revisit deck thumbnails on-frame, then invokes <paramref name="onComplete"/> (e.g. preview attach).
    /// </summary>
    void BeginRevisitDeckWarm(ShowViewModel show, Action onComplete);

    /// <summary>Legacy hook; revisit warm now runs before preview attach via <see cref="BeginRevisitDeckWarm"/>.</summary>
    void OnRevisitPreviewAttached(ShowViewModel show);

    /// <summary>
    /// Re-binds cached deck thumbnails after in-app theme change without evicting raster cache.
    /// Warms only rows that remain unbound while Show is on-frame.
    /// </summary>
    void OnThemeChanged(ShowViewModel show);

    /// <summary>
    /// Drops stale cache variants and re-warms deck thumbnails after slide-background or preview changes.
    /// Never binds compositor surfaces during navigation block/return phases.
    /// </summary>
    void RefreshDeckThumbnails(ShowViewModel show, string moment);

    /// <summary>Single-row variant of <see cref="RefreshDeckThumbnails"/>.</summary>
    void RefreshDeckThumbnail(ShowViewModel show, ShowSlideDeckItem item, string moment);

    /// <summary>
    /// Re-rasters one deck row while keeping the current image visible until the new bitmap is ready.
    /// Used for Quick Edit live preview and in-place slide text updates.
    /// </summary>
    void RefreshDeckThumbnailContent(ShowViewModel show, ShowSlideDeckItem item, string moment);
}
