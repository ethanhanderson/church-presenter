namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Retained hot resource referenced by a prepared cue.
/// </summary>
public interface IResourceHandle
{
    /// <summary>Stable resource id.</summary>
    string Id { get; }

    /// <summary>Estimated memory cost in bytes for cache accounting.</summary>
    int SizeCostBytes { get; }
}

/// <summary>
/// Simple in-memory resource handle used by the prepared-cue cache.
/// </summary>
public sealed record ResourceHandle(string Id, int SizeCostBytes) : IResourceHandle;
