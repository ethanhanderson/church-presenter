namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Read-model query surface for pipeline-backed Show/output consumers.
/// </summary>
public interface ILiveSessionQueryService
{
    /// <summary>Raised whenever the projected query snapshot changes.</summary>
    event EventHandler<LiveSessionQueryChangedEventArgs>? Changed;

    /// <summary>Current projected query snapshot.</summary>
    LiveSessionQuerySnapshot Current { get; }
}

/// <summary>
/// Event data for <see cref="ILiveSessionQueryService.Changed"/>.
/// </summary>
public sealed class LiveSessionQueryChangedEventArgs : EventArgs
{
    /// <summary>Updated query snapshot.</summary>
    public LiveSessionQuerySnapshot Snapshot { get; init; } = LiveSessionQuerySnapshot.Empty;
}
