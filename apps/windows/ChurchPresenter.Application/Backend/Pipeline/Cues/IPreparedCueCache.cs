namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Size-aware LRU cache for prepared cue records.
/// </summary>
public interface IPreparedCueCache
{
    /// <summary>Attempts to retrieve a prepared cue for the request.</summary>
    bool TryGet(CueRequest request, out PreparedCueRecord record, out CacheHitTier tier);

    /// <summary>Stores a prepared cue record.</summary>
    void Set(CueRequest request, PreparedCueRecord record);

    /// <summary>Invalidates cached entries for the specified content reference.</summary>
    void Invalidate(ContentRef contentRef);

    /// <summary>Clears all cached prepared cues.</summary>
    void InvalidateAll();

    /// <summary>Hints background preparation for the request.</summary>
    void Warm(CueRequest request);
}
