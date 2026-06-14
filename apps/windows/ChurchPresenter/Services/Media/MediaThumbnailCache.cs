using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Services.Media;

/// <inheritdoc />
public sealed class MediaThumbnailCache : IMediaThumbnailCache
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryGet(string? resolvedPath, string? mediaType, uint requestedSize, out ImageSource? image)
    {
        string key = BuildKey(resolvedPath, mediaType, requestedSize);
        if (_cache.TryGetValue(key, out ImageSource? cached))
        {
            image = cached;
            return true;
        }

        image = null;
        return false;
    }

    /// <inheritdoc />
    public async Task<ImageSource?> GetOrLoadAsync(
        string? resolvedPath,
        string? mediaType,
        uint requestedSize = 768,
        CancellationToken cancellationToken = default)
    {
        string key = BuildKey(resolvedPath, mediaType, requestedSize);
        if (_cache.TryGetValue(key, out ImageSource? cached))
            return cached;

        Task<ImageSource?> loadTask = _inflight.GetOrAdd(
            key,
            _ => LoadAndCacheAsync(key, resolvedPath, mediaType, requestedSize));

        try
        {
            return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (loadTask.IsCompleted)
                _inflight.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
        _inflight.Clear();
    }

    private async Task<ImageSource?> LoadAndCacheAsync(
        string key,
        string? resolvedPath,
        string? mediaType,
        uint requestedSize)
    {
        ImageSource? image = await LoadThumbnailOnDispatcherAsync(resolvedPath, mediaType, requestedSize)
            .ConfigureAwait(false);
        if (image != null)
            _cache[key] = image;

        return image;
    }

    private static Task<ImageSource?> LoadThumbnailOnDispatcherAsync(
        string? resolvedPath,
        string? mediaType,
        uint requestedSize)
    {
        DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcher == null || dispatcher.HasThreadAccess)
            return MediaThumbnailLoader.TryLoadAsync(resolvedPath, mediaType, requestedSize);

        TaskCompletionSource<ImageSource?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                completion.SetResult(await MediaThumbnailLoader.TryLoadAsync(resolvedPath, mediaType, requestedSize));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!queued)
            completion.SetResult(null);

        return completion.Task;
    }

    private static string BuildKey(string? resolvedPath, string? mediaType, uint requestedSize)
    {
        string normalizedPath;
        try
        {
            normalizedPath = string.IsNullOrWhiteSpace(resolvedPath)
                ? string.Empty
                : Path.GetFullPath(resolvedPath).Replace('\\', '/');
        }
        catch
        {
            normalizedPath = resolvedPath?.Trim().Replace('\\', '/') ?? string.Empty;
        }

        string normalizedType = string.IsNullOrWhiteSpace(mediaType)
            ? "media"
            : mediaType.Trim().ToLowerInvariant();

        return string.Concat(normalizedPath, "|", normalizedType, "|", requestedSize.ToString(CultureInfo.InvariantCulture));
    }
}
