namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Indicates which cache tier satisfied a cue preparation request.
/// </summary>
public enum CacheHitTier
{
    Hot,
    Warm,
    Cold,
    Miss,
}
