using ChurchPresenter.Backend.Commands;

namespace ChurchPresenter.Backend.Pipeline.Commands;

/// <summary>
/// Serializes live commands through a single-consumer channel.
/// </summary>
public interface ILiveCommandBus
{
    /// <summary>Enqueues a live command for serialized execution.</summary>
    ValueTask EnqueueAsync(LiveCommand command, CancellationToken cancellationToken = default);

    /// <summary>Starts the background consumer loop.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised after each command has been processed by the consumer.</summary>
    event Action<LiveCommand>? CommandProcessed;
}
