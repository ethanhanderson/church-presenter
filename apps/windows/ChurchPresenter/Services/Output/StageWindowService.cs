namespace ChurchPresenter.Services.Output;

/// <summary>Compatibility facade for stage window controls.</summary>
public sealed class StageWindowService(IOutputWindowService outputWindows) : IStageWindowService
{
    private readonly IOutputWindowService _outputWindows = outputWindows ?? throw new ArgumentNullException(nameof(outputWindows));

    /// <inheritdoc />
    public void Open() => _outputWindows.OpenStage();

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default) =>
        _outputWindows.OpenStageAsync(cancellationToken);

    /// <inheritdoc />
    public void CloseAll() => _outputWindows.CloseStage();
}
