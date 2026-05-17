
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
public sealed class ContentChangeBus(
    ILogger<ContentChangeBus> logger,
    IServiceProvider? services = null) : IContentChangeBus
{
    private readonly ILogger<ContentChangeBus> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IServiceProvider? _services = services;

    /// <inheritdoc />
    public event EventHandler<ContentChangeEvent>? Changed;

    /// <inheritdoc />
    public void Publish(ContentChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _logger.LogDebug(
            "Content change published: {Kind} {SubjectId}.",
            change.Kind,
            change.SubjectId);
        Changed?.Invoke(this, change);

        if (_services == null)
            return;

        foreach (var invalidator in _services.GetServices<IContentCacheInvalidator>())
            invalidator.HandleContentChanged(change);
    }
}
