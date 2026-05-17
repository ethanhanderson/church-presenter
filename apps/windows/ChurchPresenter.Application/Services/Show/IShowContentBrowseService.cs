
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Loads Show catalog/workspace browse data and activates selected slides through the live path.
/// </summary>
public interface IShowContentBrowseService
{
    /// <summary>Loads catalog and workspace state, then returns the initial browse projection.</summary>
    Task<ShowContentBrowseSnapshot> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Searches all catalog presentations and slides using the application browse service.</summary>
    Task<ShowContentBrowseSnapshot> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Opens a specific presentation path even when it is not part of the selected catalog source.</summary>
    Task<ShowContentBrowseSnapshot> OpenPresentationAsync(string presentationPath, CancellationToken cancellationToken = default);

    /// <summary>Selects a library or playlist source and returns the refreshed browse projection.</summary>
    Task<ShowContentBrowseSnapshot> SelectSourceAsync(string sourceKey, CancellationToken cancellationToken = default);

    /// <summary>Selects a presentation from the current source and returns the refreshed browse projection.</summary>
    Task<ShowContentBrowseSnapshot> SelectPresentationAsync(string presentationPath, CancellationToken cancellationToken = default);

    /// <summary>Sends a slide to live output through cue preparation, playback, and slide actions.</summary>
    Task<bool> TakeSlideLiveAsync(
        string presentationPath,
        string slideId,
        string? instanceKey = null,
        CancellationToken cancellationToken = default);
}