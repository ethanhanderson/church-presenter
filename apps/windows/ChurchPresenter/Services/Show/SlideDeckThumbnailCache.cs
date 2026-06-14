using System.Collections.Concurrent;

using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
public sealed class SlideDeckThumbnailCache : ISlideDeckThumbnailCache
{
    private readonly ConcurrentDictionary<string, DeckThumbnailCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool TryGet(string cacheKey, out ImageSource? image)
    {
        if (_entries.TryGetValue(cacheKey, out DeckThumbnailCacheEntry? entry)
            && entry.CompositorSource is not null)
        {
            image = entry.CompositorSource;
            return true;
        }

        image = null;
        return false;
    }

    /// <inheritdoc />
    public bool HasNavigationPixels(string cacheKey) =>
        _entries.TryGetValue(cacheKey, out DeckThumbnailCacheEntry? entry) && entry.HasNavigationPixels;

    /// <inheritdoc />
    public async Task<ImageSource?> GetOrRenderAsync(
        string cacheKey,
        Func<CancellationToken, Task<DeckThumbnailRasterBundle>> renderFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(renderFactory);

        if (_entries.TryGetValue(cacheKey, out DeckThumbnailCacheEntry? cached)
            && cached.CompositorSource is not null)
            return cached.CompositorSource;

        Task<ImageSource?> loadTask = _inflight.GetOrAdd(
            cacheKey,
            _ => RenderAndCacheAsync(this, cacheKey, renderFactory, cancellationToken));

        try
        {
            return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (loadTask.IsCompleted)
                _inflight.TryRemove(cacheKey, out _);
        }
    }

    /// <inheritdoc />
    public async Task<ImageSource?> CreateNavigationDisplaySourceAsync(
        string cacheKey,
        CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(cacheKey, out DeckThumbnailCacheEntry? entry)
            || !entry.HasNavigationPixels)
            return null;

        return await DeckThumbnailPixelSnapshot
            .CreateDisplaySourceAsync(entry.Pixels!, entry.Width, entry.Height, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <inheritdoc />
    public bool TryTakeNavigationDisplaySpare(string cacheKey, out ImageSource? spare)
    {
        if (_entries.TryGetValue(cacheKey, out DeckThumbnailCacheEntry? entry)
            && entry.NavigationDisplaySpare is not null)
        {
            spare = entry.NavigationDisplaySpare;
            entry.NavigationDisplaySpare = null;
            return true;
        }

        spare = null;
        return false;
    }

    /// <inheritdoc />
    public void EvictCompositorSurface(string cacheKey)
    {
        if (_entries.TryGetValue(cacheKey, out DeckThumbnailCacheEntry? entry))
            entry.CompositorSource = null;
    }

    /// <inheritdoc />
    public void InvalidatePrefix(string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        foreach (string key in _entries.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal)))
            _entries.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _entries.Clear();
        _inflight.Clear();
    }

    /// <inheritdoc />
    public void CancelInflight() => _inflight.Clear();

    /// <inheritdoc />
    public void CancelInflight(string cacheKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        _inflight.TryRemove(cacheKey, out _);
    }

    /// <inheritdoc />
    public void UpdateCompositorSurface(string cacheKey, ImageSource source, DeckThumbnailRasterBundle bundle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bundle);

        _entries.AddOrUpdate(
            cacheKey,
            _ => DeckThumbnailCacheEntry.FromBundle(bundle, navigationSpare: null),
            (_, existing) =>
            {
                existing.CompositorSource = source;
                existing.SetNavigationPixels(bundle.Pixels, bundle.Width, bundle.Height);
                return existing;
            });
    }

    private static async Task<ImageSource?> RenderAndCacheAsync(
        SlideDeckThumbnailCache owner,
        string cacheKey,
        Func<CancellationToken, Task<DeckThumbnailRasterBundle>> renderFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            DeckThumbnailRasterBundle bundle = await renderFactory(cancellationToken).ConfigureAwait(true);
            ImageSource? navigationSpare = await DeckThumbnailPixelSnapshot
                .CreateDisplaySourceAsync(bundle.Pixels, bundle.Width, bundle.Height, cancellationToken)
                .ConfigureAwait(true);

            owner._entries.AddOrUpdate(
                cacheKey,
                _ => DeckThumbnailCacheEntry.FromBundle(bundle, navigationSpare),
                (_, existing) =>
                {
                    existing.CompositorSource = bundle.Source;
                    existing.SetNavigationPixels(bundle.Pixels, bundle.Width, bundle.Height);
                    existing.NavigationDisplaySpare = navigationSpare;
                    return existing;
                });
            return bundle.Source;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class DeckThumbnailCacheEntry
    {
        public ImageSource? CompositorSource { get; set; }

        public ImageSource? NavigationDisplaySpare { get; set; }

        public byte[]? Pixels { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool HasNavigationPixels =>
            Pixels is { Length: > 0 } && Width > 0 && Height > 0;

        public static DeckThumbnailCacheEntry FromBundle(DeckThumbnailRasterBundle bundle, ImageSource? navigationSpare)
        {
            var entry = new DeckThumbnailCacheEntry
            {
                CompositorSource = bundle.Source,
                NavigationDisplaySpare = navigationSpare,
            };
            entry.SetNavigationPixels(bundle.Pixels, bundle.Width, bundle.Height);
            return entry;
        }

        public void SetNavigationPixels(byte[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
        }
    }
}
