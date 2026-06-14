namespace ChurchPresenter.Backend.Pipeline.HostAdapter;

/// <summary>
/// Placeholder binding contract for live video input slots.
/// </summary>
public interface ILiveVideoBinding : IAsyncDisposable
{
    /// <summary>Input id used for diagnostics.</summary>
    string InputId { get; }

    /// <summary>Whether the input is currently connected.</summary>
    bool IsConnected { get; }
}
