using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Pipeline.Commands;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

public sealed class LiveCommandBusTests
{
    [Fact]
    public async Task EnqueueAsync_processes_commands_in_order()
    {
        LiveCommandBus bus = new(NullLogger<LiveCommandBus>.Instance);
        List<Guid> processed = [];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        TaskCompletionSource allProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        bus.CommandProcessed += command =>
        {
            processed.Add(command.Id);
            if (processed.Count == 2)
                allProcessed.TrySetResult();
        };

        _ = bus.StartAsync(timeout.Token);
        LiveCommand first = new() { Kind = LiveCommandKind.Clear };
        LiveCommand second = new() { Kind = LiveCommandKind.SetLook };

        await bus.EnqueueAsync(first, timeout.Token);
        await bus.EnqueueAsync(second, timeout.Token);
        await allProcessed.Task.WaitAsync(timeout.Token);

        processed.Should().Equal(first.Id, second.Id);
    }
}
