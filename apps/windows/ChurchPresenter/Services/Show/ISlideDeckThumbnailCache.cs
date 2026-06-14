using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Services.Show;

/// <summary>
/// App-lifetime cache for slide deck thumbnails rendered via <c>ThumbnailEngine</c>.
/// Compositor surfaces are evicted during Show navigation; pixel buffers support safe revisit display.
/// </summary>
public interface ISlideDeckThumbnailCache
{
    /// <summary>Returns a cached compositor thumbnail when present.</summary>
    bool TryGet(string cacheKey, out ImageSource? image);

    /// <summary>True when a navigation pixel buffer exists for the cache key.</summary>
    bool HasNavigationPixels(string cacheKey);

    /// <summary>Returns a cached compositor thumbnail or renders via <paramref name="renderFactory"/>.</summary>
    Task<ImageSource?> GetOrRenderAsync(
        string cacheKey,
        Func<CancellationToken, Task<DeckThumbnailRasterBundle>> renderFactory,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a fresh display source from stored navigation pixels (never reuses compositor handles).</summary>
    Task<ImageSource?> CreateNavigationDisplaySourceAsync(
        string cacheKey,
        CancellationToken cancellationToken = default);

    /// <summary>Takes a pre-built navigation display spare captured at raster time (sync swap on leave).</summary>
    bool TryTakeNavigationDisplaySpare(string cacheKey, out ImageSource? spare);

    /// <summary>Drops the compositor surface while retaining navigation pixels for revisit display.</summary>
    void EvictCompositorSurface(string cacheKey);

    /// <summary>Removes entries whose keys start with <paramref name="keyPrefix"/>.</summary>
    void InvalidatePrefix(string keyPrefix);

    /// <summary>Clears all cached deck thumbnails.</summary>
    void Clear();

    /// <summary>Drops in-flight render tasks during Show navigation teardown.</summary>
    void CancelInflight();

    /// <summary>Drops an in-flight render for one cache key without disturbing other rows.</summary>
    void CancelInflight(string cacheKey);

    /// <summary>Replaces the compositor surface for a cache key after an in-place bitmap update.</summary>
    void UpdateCompositorSurface(string cacheKey, ImageSource source, DeckThumbnailRasterBundle bundle);
}
