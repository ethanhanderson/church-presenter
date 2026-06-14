using System.Threading.Channels;

using ChurchPresenter.Backend.Commands;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Backend.Pipeline.Commands;

/// <summary>
/// Unbounded channel-backed live command bus with a single consumer loop.
/// </summary>
public sealed class LiveCommandBus(ILogger<LiveCommandBus> logger) : ILiveCommandBus
{
    private readonly ILogger<LiveCommandBus> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Channel<LiveCommand> _channel = Channel.CreateUnbounded<LiveCommand>();
    private readonly object _startGate = new();
    private Task? _consumerTask;

    /// <inheritdoc />
    public event Action<LiveCommand>? CommandProcessed;

    /// <inheritdoc />
    public ValueTask EnqueueAsync(LiveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _channel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_startGate)
        {
            if (_consumerTask != null)
                return _consumerTask;

            _consumerTask = Task.Run(() => ConsumeAsync(cancellationToken), CancellationToken.None);
            return _consumerTask;
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (LiveCommand command in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                CommandProcessed?.Invoke(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live command bus handler failed for command {CommandId}.", command.Id);
            }
        }
    }
}
