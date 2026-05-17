using System.Collections.Concurrent;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Show;

/// <summary>
/// Persistent in-memory cache of <see cref="PresentationDocument"/> instances.
/// Wraps <see cref="IPresentationDocumentService"/> so that browse-stack rendering, slide
/// navigation, and output routing all share the same loaded instances instead of each
/// reopening bundles from disk.
/// <para>
/// Once a presentation is loaded it remains cached while its bundle file still exists — switching
/// libraries or playlists only updates the ordered <see cref="SessionPaths"/> list.  Deleted
/// bundles are pruned on session refresh and cache access so the app does not keep stale slide
/// state alive after content is removed.
/// </para>
/// </summary>
public sealed class ShowSessionCache(
    IPresentationDocumentService presentationDocs,
    IContentDirectoryService contentDirectories,
    ILogger<ShowSessionCache> logger,
    Func<string, bool>? fileExists = null,
    IContentStore? contentStore = null) : IShowSessionCache, IContentCacheInvalidator, IDisposable
{
    private readonly IPresentationDocumentService _presentationDocs = presentationDocs
        ?? throw new ArgumentNullException(nameof(presentationDocs));
    private readonly IContentDirectoryService _contentDirectories = contentDirectories
        ?? throw new ArgumentNullException(nameof(contentDirectories));
    private readonly ILogger<ShowSessionCache> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;
    private readonly IContentStore? _contentStore = contentStore;

    // Keyed by normalized (resolved) absolute path, case-insensitive.
    // ConcurrentDictionary is required because background prefetch tasks write to the cache
    // on thread-pool threads while the UI thread reads from it via TryGet / GetOrLoad.
    private readonly ConcurrentDictionary<string, CachedPresentation> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sessionPaths = new();
    private CancellationTokenSource? _prefetchCts;

    /// <inheritdoc />
    public IReadOnlyList<string> SessionPaths => _sessionPaths;

    /// <inheritdoc />
    public void SetSessionOrder(IReadOnlyList<PresentationRefDto> items)
    {
        CancelPrefetch();
        PruneMissingFiles();
        _sessionPaths.Clear();
        if (items.Count == 0)
            return;
        _sessionPaths.AddRange(ResolveSessionPaths(items));
    }

    /// <inheritdoc />
    public async Task LoadSessionAsync(IReadOnlyList<PresentationRefDto> items, CancellationToken ct = default)
    {
        CancelPrefetch();
        PruneMissingFiles();

        // Update the ordered session list without clearing the cache.
        // Previously loaded documents remain in memory so navigating back to them
        // is instant while their backing bundle files still exist.
        _sessionPaths.Clear();

        if (items.Count == 0)
            return;

        var resolvedPaths = ResolveSessionPaths(items);
        _sessionPaths.AddRange(resolvedPaths);

        // Load items not already in the cache.
        // The first uncached item is loaded synchronously so the browse stack has data immediately;
        // the remainder are loaded on a background thread so the UI stays responsive.
        var uncached = resolvedPaths.Where(p => !TryGetCachedFresh(p, out _)).ToList();

        if (uncached.Count == 0)
            return;

        // Load the first uncached item synchronously.
        LoadSingle(uncached[0]);

        if (uncached.Count <= 1)
            return;

        var cts = new CancellationTokenSource();
        _prefetchCts = cts;
        var pathsToLoad = uncached.Skip(1).ToList();

        await Task.Run(() =>
        {
            foreach (var path in pathsToLoad)
            {
                if (cts.IsCancellationRequested)
                    break;
                LoadSingle(path);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ClearSession()
    {
        CancelPrefetch();
        _cache.Clear();
        _sessionPaths.Clear();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PruneMissingFiles()
    {
        List<string> pruned = new();

        foreach (var path in _cache.Keys)
        {
            if (IsPresentationFileAvailable(path))
                continue;

            RemoveResolvedPath(path);
            pruned.Add(path);
        }

        for (var i = _sessionPaths.Count - 1; i >= 0; i--)
        {
            var path = _sessionPaths[i];
            if (IsPresentationFileAvailable(path))
                continue;

            RemoveResolvedPath(path);
            if (!pruned.Contains(path, StringComparer.OrdinalIgnoreCase))
                pruned.Add(path);
        }

        return pruned;
    }

    /// <inheritdoc />
    public PresentationDocument? TryGet(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var resolved = TryResolvePath(path);
        if (resolved == null)
            return null;

        return TryGetCachedFresh(resolved, out var doc) ? doc : null;
    }

    /// <inheritdoc />
    public PresentationDocument? GetOrLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var resolved = TryResolvePath(path);
        if (resolved == null)
            return null;

        if (!IsPresentationFileAvailable(resolved))
            return null;

        if (TryGetCachedFresh(resolved, out var cached))
            return cached;

        return LoadSingle(resolved);
    }

    /// <inheritdoc />
    public void UpdateEntry(string path, PresentationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolved = TryResolvePath(path) ?? path;
        if (!IsPresentationFileAvailable(resolved))
            return;

        var stamp = TryGetAvailableStamp(resolved);
        if (stamp is null)
            return;

        _cache[resolved] = new CachedPresentation(document, stamp);
    }

    /// <inheritdoc />
    public void SchedulePrefetch(string currentPath, int lookAhead = 2)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || _sessionPaths.Count == 0 || lookAhead <= 0)
            return;

        var resolved = TryResolvePath(currentPath);
        if (resolved == null)
            return;

        var currentIdx = _sessionPaths.FindIndex(p =>
            string.Equals(p, resolved, StringComparison.OrdinalIgnoreCase));
        if (currentIdx < 0)
            return;

        // Collect uncached paths within the look-ahead window.
        var toLoad = new List<string>();
        for (var i = 1; i <= lookAhead; i++)
        {
            var idx = currentIdx + i;
            if (idx >= _sessionPaths.Count)
                break;
            var candidate = _sessionPaths[idx];
            if (!IsPresentationFileAvailable(candidate))
                continue;
            if (!TryGetCachedFresh(candidate, out _))
                toLoad.Add(candidate);
        }

        if (toLoad.Count == 0)
            return;

        CancelPrefetch();
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;

        _ = Task.Run(() =>
        {
            foreach (var path in toLoad)
            {
                if (cts.IsCancellationRequested)
                    break;
                LoadSingle(path);
            }
        }, cts.Token);
    }

    /// <inheritdoc />
    public void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolved = TryResolvePath(path) ?? path;
        RemoveResolvedPath(resolved);
    }

    /// <inheritdoc />
    public void HandleContentChanged(ContentChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);

        switch (change.Kind)
        {
            case ContentChangeKind.PresentationDeleted:
            case ContentChangeKind.PresentationReplaced:
            case ContentChangeKind.PresentationRenamed:
            case ContentChangeKind.PresentationUpdated:
                if (!string.IsNullOrWhiteSpace(change.PreviousSubjectId))
                    Invalidate(change.PreviousSubjectId);
                if (!string.IsNullOrWhiteSpace(change.SubjectId))
                    Invalidate(change.SubjectId);
                break;
            case ContentChangeKind.CatalogRefreshed:
            case ContentChangeKind.PackageImportCompleted:
            case ContentChangeKind.RepairCompleted:
                PruneMissingFiles();
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelPrefetch();
        _prefetchCts?.Dispose();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private List<string> ResolveSessionPaths(IReadOnlyList<PresentationRefDto> items)
    {
        var resolvedPaths = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
                continue;
            try
            {
                var resolvedPath = _contentDirectories.ResolvePresentationPath(item.Path);
                if (!IsPresentationFileAvailable(resolvedPath))
                {
                    RemoveResolvedPath(resolvedPath);
                    _logger.LogInformation("Skipping deleted presentation bundle '{Path}' while updating the session cache.", resolvedPath);
                    continue;
                }

                resolvedPaths.Add(resolvedPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot resolve path for session item '{Path}'.", item.Path);
            }
        }

        return resolvedPaths;
    }

    private PresentationDocument? LoadSingle(string resolvedPath)
    {
        if (!IsPresentationFileAvailable(resolvedPath))
            return null;

        if (TryGetCachedFresh(resolvedPath, out var existing))
            return existing;

        try
        {
            var stamp = TryGetAvailableStamp(resolvedPath);
            if (stamp is null)
                return null;

            var doc = _presentationDocs.Open(resolvedPath);
            // GetOrAdd ensures that if two threads race to load the same path, only
            // one instance ends up in the cache and both callers receive the same object.
            var stored = _cache.GetOrAdd(resolvedPath, new CachedPresentation(doc, stamp));
            _logger.LogDebug("Session cache loaded '{Path}'.", resolvedPath);
            return stored.Document;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session cache failed to load '{Path}'.", resolvedPath);
            if (!IsPresentationFileAvailable(resolvedPath))
                RemoveResolvedPath(resolvedPath);
            return null;
        }
    }

    private bool TryGetCachedFresh(string resolvedPath, out PresentationDocument? document)
    {
        document = null;
        if (!_cache.TryGetValue(resolvedPath, out var cached))
            return false;

        var currentStamp = TryGetAvailableStamp(resolvedPath);
        if (currentStamp is not null && cached.Stamp.Matches(currentStamp))
        {
            document = cached.Document;
            return true;
        }

        RemoveResolvedPath(resolvedPath);
        return false;
    }

    private bool IsPresentationFileAvailable(string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return false;

        try
        {
            if (_contentStore != null)
                return _contentStore.GetStamp(resolvedPath).Succeeded;

            return _fileExists(resolvedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not check whether presentation bundle '{Path}' still exists.", resolvedPath);
            return false;
        }
    }

    private ContentResourceStamp? TryGetAvailableStamp(string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return null;

        if (_contentStore != null)
        {
            var result = _contentStore.GetStamp(resolvedPath);
            return result.Succeeded ? result.Value : null;
        }

        try
        {
            if (!_fileExists(resolvedPath))
                return null;

            var info = new FileInfo(resolvedPath);
            if (info.Exists)
            {
                return new ContentResourceStamp
                {
                    Path = Path.GetFullPath(resolvedPath),
                    Exists = true,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Length = info.Length,
                };
            }

            return new ContentResourceStamp { Path = resolvedPath, Exists = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not stamp presentation bundle '{Path}'.", resolvedPath);
            return null;
        }
    }

    private void RemoveResolvedPath(string resolvedPath)
    {
        _cache.TryRemove(resolvedPath, out _);
        _sessionPaths.RemoveAll(path => string.Equals(path, resolvedPath, StringComparison.OrdinalIgnoreCase));
    }

    private string? TryResolvePath(string path)
    {
        try
        {
            return _contentDirectories.ResolvePresentationPath(path);
        }
        catch
        {
            return null;
        }
    }

    private void CancelPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts = null;
    }

    private sealed record CachedPresentation(PresentationDocument Document, ContentResourceStamp Stamp);
}