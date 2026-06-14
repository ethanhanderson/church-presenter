namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Audience and stage output target toggles for the pipeline runtime.
/// </summary>
public interface IOutputTargetService
{
    /// <summary>Whether audience output is enabled.</summary>
    bool AudienceEnabled { get; }

    /// <summary>Whether stage output is enabled.</summary>
    bool StageEnabled { get; }

    /// <summary>Raised when output target flags change.</summary>
    event EventHandler? Changed;

    /// <summary>Enables or disables audience output.</summary>
    Task SetAudienceEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables stage output.</summary>
    Task SetStageEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Delegates output target toggles to the pipeline runtime.
/// </summary>
public sealed class OutputTargetService : IOutputTargetService
{
    private readonly CuePipelineRuntime _runtime;

    /// <summary>Creates the service and tracks runtime changes.</summary>
    public OutputTargetService(CuePipelineRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.Changed += HandleRuntimeChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool AudienceEnabled => _runtime.AudienceEnabled;

    /// <inheritdoc />
    public bool StageEnabled => _runtime.StageEnabled;

    /// <inheritdoc />
    public Task SetAudienceEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _runtime.SetAudienceEnabledAsync(enabled, cancellationToken);

    /// <inheritdoc />
    public Task SetStageEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _runtime.SetStageEnabledAsync(enabled, cancellationToken);

    private void HandleRuntimeChanged(object? sender, EventArgs args)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
