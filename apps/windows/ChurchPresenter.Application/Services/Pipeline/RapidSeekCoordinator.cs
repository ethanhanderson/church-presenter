using ChurchPresenter.Models.Media;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Implements hold-to-seek navigation using the pipeline selection loop semantics.
/// </summary>
public sealed class RapidSeekCoordinator : IRapidSeekCoordinator
{
    private const int MinimumSeekRepeatMs = 50;
    private CancellationTokenSource? _seekCts;
    private int _seekDirection;
    private Task? _seekTask;

    /// <inheritdoc />
    public bool IsSeeking => _seekCts != null && !_seekCts.IsCancellationRequested;

    /// <inheritdoc />
    public async Task BeginRapidSeekAsync(
        int direction,
        Func<int, Task<SlideSeekStepResult>> stepProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stepProvider);

        if (_seekCts != null && !_seekCts.IsCancellationRequested && _seekDirection == direction)
            return;

        EndRapidSeek();

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _seekCts = cts;
        _seekDirection = direction;

        SlideSeekStepResult initialStep = await stepProvider(direction).ConfigureAwait(true);
        if (!initialStep.Moved)
        {
            EndRapidSeek();
            return;
        }

        _seekTask = RunSeekLoopAsync(direction, initialStep.Delay, cts.Token, stepProvider);
    }

    /// <inheritdoc />
    public void EndRapidSeek()
    {
        _seekCts?.Cancel();
        _seekCts = null;
        _seekDirection = 0;
        _seekTask = null;
    }

    private async Task RunSeekLoopAsync(
        int direction,
        TimeSpan initialDelay,
        CancellationToken ct,
        Func<int, Task<SlideSeekStepResult>> stepProvider)
    {
        TimeSpan delay = NormalizeDelay(initialDelay);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            SlideSeekStepResult step = await stepProvider(direction).ConfigureAwait(true);
            if (!step.Moved)
            {
                EndRapidSeek();
                break;
            }

            delay = NormalizeDelay(step.Delay);
        }
    }

    private static TimeSpan NormalizeDelay(TimeSpan delay) =>
        delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(MinimumSeekRepeatMs);
}
