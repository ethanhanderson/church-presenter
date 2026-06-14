namespace ChurchPresenter.Services.Output;

/// <summary>Compatibility facade for audience window controls.</summary>
public sealed class AudienceWindowService(IOutputWindowService outputWindows) : IAudienceWindowService
{
    private readonly IOutputWindowService _outputWindows = outputWindows ?? throw new ArgumentNullException(nameof(outputWindows));

    /// <inheritdoc />
    public void Open() => _outputWindows.OpenAudience();

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default) =>
        _outputWindows.OpenAudienceAsync(cancellationToken);

    /// <inheritdoc />
    public void CloseAll() => _outputWindows.CloseAll();
}