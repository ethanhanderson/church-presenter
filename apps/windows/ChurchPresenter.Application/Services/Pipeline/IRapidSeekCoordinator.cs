
namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Hold-to-seek coordination extracted from the legacy playback engine.
/// </summary>
public interface IRapidSeekCoordinator
{
    /// <summary>Whether a rapid seek loop is currently active.</summary>
    bool IsSeeking { get; }

    /// <summary>Begins a hold-to-seek loop in the given direction.</summary>
    Task BeginRapidSeekAsync(int direction, Func<int, Task<SlideSeekStepResult>> stepProvider, CancellationToken cancellationToken = default);

    /// <summary>Ends any active rapid seek loop.</summary>
    void EndRapidSeek();
}
