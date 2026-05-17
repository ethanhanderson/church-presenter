
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Persistent in-memory cache of <see cref="PresentationDocument"/> instances shared across all
/// library and playlist sessions.  Loaded presentations are retained across source switches while
/// their bundle files still exist.  Background prefetch ensures upcoming presentations are warm
/// before the user reaches them.
/// </summary>
public interface IShowSessionCache
{
    /// <summary>
    /// Ordered resolved paths for the current session (library selection = one item, playlist = all items).
    /// </summary>
    IReadOnlyList<string> SessionPaths { get; }

    /// <summary>
    /// Updates the active session to <paramref name="items"/> without evicting existing cached
    /// documents whose bundle files still exist.  Items already in the cache are skipped; uncached
    /// items are loaded — the first synchronously, the rest on a background thread.
    /// </summary>
    /// <param name="items">Ordered presentation refs from the active library or playlist.</param>
    /// <param name="ct">Cancellation token for the background loading phase.</param>
    Task LoadSessionAsync(IReadOnlyList<PresentationRefDto> items, CancellationToken ct = default);

    /// <summary>
    /// Replaces <see cref="SessionPaths"/> with the resolved order of <paramref name="items"/> without
    /// loading documents.  Use after the caller has already warmed the cache (e.g. playlist browse stack)
    /// so <see cref="SchedulePrefetch"/> has a complete session ordering immediately.
    /// </summary>
    void SetSessionOrder(IReadOnlyList<PresentationRefDto> items);

    /// <summary>Removes all cached documents and clears the session order.</summary>
    void ClearSession();

    /// <summary>
    /// Removes cached/session paths whose bundle files no longer exist and returns the resolved
    /// paths that were pruned.
    /// </summary>
    IReadOnlyList<string> PruneMissingFiles();

    /// <summary>
    /// Returns the cached document for <paramref name="path"/> without loading from disk,
    /// or <c>null</c> if not yet loaded.
    /// </summary>
    PresentationDocument? TryGet(string path);

    /// <summary>
    /// Returns the cached document for <paramref name="path"/>, loading and caching it
    /// synchronously if it is not already present.
    /// Returns <c>null</c> when the path cannot be resolved or the bundle is unreadable.
    /// </summary>
    PresentationDocument? GetOrLoad(string path);

    /// <summary>
    /// Stores or replaces the cache entry for <paramref name="path"/> with <paramref name="document"/>.
    /// Call this after a presentation is saved so the cache stays consistent without a round-trip to disk.
    /// </summary>
    void UpdateEntry(string path, PresentationDocument document);

    /// <summary>
    /// Schedules background loading of the next <paramref name="lookAhead"/> presentations that
    /// follow <paramref name="currentPath"/> in the session order. No-ops when the cache is warm.
    /// </summary>
    void SchedulePrefetch(string currentPath, int lookAhead = 2);

    /// <summary>
    /// Removes the cache entry for <paramref name="path"/>.
    /// The next <see cref="GetOrLoad"/> call will reload from disk.
    /// </summary>
    void Invalidate(string path);
}