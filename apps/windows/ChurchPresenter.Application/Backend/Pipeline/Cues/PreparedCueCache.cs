using System.Collections.Concurrent;

namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Dictionary-backed LRU prepared-cue cache with configurable hot-tier byte budget.
/// </summary>
public sealed class PreparedCueCache : IPreparedCueCache
{
    private const int DefaultMaxHotBytes = 256 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly ConcurrentDictionary<string, byte> _warming = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxHotBytes;

    /// <summary>Creates a cache with the default 256 MB hot budget.</summary>
    public PreparedCueCache()
        : this(DefaultMaxHotBytes)
    {
    }

    /// <summary>Creates a cache with the supplied hot-tier byte budget.</summary>
    public PreparedCueCache(int maxHotBytes)
    {
        if (maxHotBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHotBytes));

        _maxHotBytes = maxHotBytes;
    }

    /// <inheritdoc />
    public bool TryGet(CueRequest request, out PreparedCueRecord record, out CacheHitTier tier)
    {
        ArgumentNullException.ThrowIfNull(request);

        string key = BuildKey(request);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out CacheEntry? entry))
            {
                Touch(key);
                record = entry.Record;
                tier = CacheHitTier.Hot;
                return true;
            }
        }

        record = null!;
        tier = CacheHitTier.Miss;
        return false;
    }

    /// <inheritdoc />
    public void Set(CueRequest request, PreparedCueRecord record)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(record);

        string key = BuildKey(request);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out CacheEntry? existing))
            {
                _lru.Remove(existing.Node);
                _entries.Remove(key);
            }

            LinkedListNode<string> node = _lru.AddFirst(key);
            _entries[key] = new CacheEntry(record, node);
            EvictIfNeeded();
        }
    }

    /// <inheritdoc />
    public void Invalidate(ContentRef contentRef)
    {
        ArgumentNullException.ThrowIfNull(contentRef);

        lock (_gate)
        {
            string[] keysToRemove = _entries
                .Where(pair => MatchesContentRef(pair.Value.Record.ContentRef, contentRef))
                .Select(static pair => pair.Key)
                .ToArray();

            foreach (string key in keysToRemove)
                RemoveEntry(key);
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lru.Clear();
        }
    }

    /// <inheritdoc />
    public void Warm(CueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _warming.TryAdd(BuildKey(request), 0);
    }

    /// <summary>Returns pending warm keys and clears the warm hint set.</summary>
    internal IReadOnlyList<string> DrainWarmHints()
    {
        string[] keys = _warming.Keys.ToArray();
        foreach (string key in keys)
            _warming.TryRemove(key, out _);

        return keys;
    }

    private void EvictIfNeeded()
    {
        while (_entries.Values.Sum(static entry => Math.Max(0, entry.Record.SizeCostBytes)) > _maxHotBytes && _lru.Last != null)
        {
            RemoveEntry(_lru.Last.Value);
        }
    }

    private void Touch(string key)
    {
        if (!_entries.TryGetValue(key, out CacheEntry? entry))
            return;

        _lru.Remove(entry.Node);
        entry.Node = _lru.AddFirst(key);
    }

    private void RemoveEntry(string key)
    {
        if (!_entries.TryGetValue(key, out CacheEntry? entry))
            return;

        _lru.Remove(entry.Node);
        _entries.Remove(key);
    }

    private static bool MatchesContentRef(ContentRef cached, ContentRef target)
    {
        if (cached.Kind != target.Kind)
            return false;

        if (string.Equals(cached.Id, target.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(target.Version)
            && string.Equals(cached.Version, target.Version, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildKey(CueRequest request) =>
        $"{request.Layer}:{request.CueKind}:{request.ContentRef.Kind}:{request.ContentRef.Id}:{request.ContentRef.Version}:{request.RoutingHash}";

    private sealed class CacheEntry(PreparedCueRecord record, LinkedListNode<string> node)
    {
        public PreparedCueRecord Record { get; } = record;

        public LinkedListNode<string> Node { get; set; } = node;
    }
}
