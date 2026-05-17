namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
/// <remarks>
/// Delegates all initialization and migration logic to <see cref="IContentBootstrapService"/>.
/// Kept for backward compatibility; new code should inject <see cref="IContentBootstrapService"/> directly.
/// </remarks>
public sealed class AppDataInitializer(IContentBootstrapService bootstrap) : IAppDataInitializer
{
    private readonly IContentBootstrapService _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _bootstrap.InitializeAsync(cancellationToken);
}