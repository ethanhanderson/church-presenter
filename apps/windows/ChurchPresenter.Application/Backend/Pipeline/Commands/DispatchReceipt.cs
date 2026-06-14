namespace ChurchPresenter.Backend.Pipeline.Commands;

/// <summary>
/// Result of dispatching one live command through the unified pipeline.
/// </summary>
public sealed record DispatchReceipt
{
    /// <summary>Whether the command completed successfully.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Command id that was dispatched.</summary>
    public Guid CommandId { get; init; }

    /// <summary>Correlation id supplied by the caller, when any.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Pipeline stage timings for diagnostics.</summary>
    public PipelineTiming Timing { get; init; } = new();

    /// <summary>Error message when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; init; }
}
