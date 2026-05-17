using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Stores and resolves the active Looks preset used to route output layers to specific feeds.
/// </summary>
public interface IOutputRoutingService
{
    /// <summary>Raised whenever the active look or any routed layer mapping changes.</summary>
    event EventHandler? Changed;

    /// <summary>Known output feeds that Looks can target.</summary>
    IReadOnlyList<OutputFeedDefinition> Feeds { get; }

    /// <summary>Available look presets.</summary>
    IReadOnlyList<OutputLookDefinition> Looks { get; }

    /// <summary>Identifier of the currently active look.</summary>
    string ActiveLookId { get; }

    /// <summary>The currently active look definition.</summary>
    OutputLookDefinition ActiveLook { get; }

    /// <summary>Returns whether the current look routes <paramref name="layerKind"/> to <paramref name="feedId"/>.</summary>
    bool RoutesLayer(string feedId, OutputLayerKind layerKind);

    /// <summary>Activates the requested look id, falling back to the default look when missing.</summary>
    Task SetActiveLookAsync(string lookId, CancellationToken cancellationToken = default);

    /// <summary>Resets routing back to the built-in default look.</summary>
    Task ResetToDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates routing for one feed/layer pair. Editing the built-in default look automatically
    /// promotes the current state into the writable custom look first.
    /// </summary>
    Task SetLayerRoutingAsync(string feedId, OutputLayerKind layerKind, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the active look's audience screen routes. Editing the built-in default look automatically
    /// promotes the current state into the writable custom look first.
    /// </summary>
    Task SetRoutesAsync(IEnumerable<OutputLookFeedRouting> routes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the active look's configured clear groups. Editing the built-in default look automatically
    /// promotes the current state into the writable custom look first.
    /// </summary>
    Task SetClearGroupsAsync(IEnumerable<OutputLookClearGroupDefinition> clearGroups, CancellationToken cancellationToken = default);
}