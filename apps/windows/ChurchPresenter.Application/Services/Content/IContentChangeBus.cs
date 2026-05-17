
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Publishes high-level content changes so caches, diagnostics, and query projections can stay coherent.
/// </summary>
public interface IContentChangeBus
{
    /// <summary>Raised whenever a content change is published.</summary>
    event EventHandler<ContentChangeEvent>? Changed;

    /// <summary>Publishes a change to current subscribers.</summary>
    void Publish(ContentChangeEvent change);
}

/// <summary>
/// Optional contract for services that clear or validate cached artifacts after content changes.
/// </summary>
public interface IContentCacheInvalidator
{
    /// <summary>Handles a content change that may invalidate cached artifacts.</summary>
    void HandleContentChanged(ContentChangeEvent change);
}
