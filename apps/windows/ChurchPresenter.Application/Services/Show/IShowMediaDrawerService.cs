
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Application workflow for browsing media assets and sending one to the live media layer.
/// </summary>
public interface IShowMediaDrawerService
{
    /// <summary>Loads root media assets available to the Show media drawer.</summary>
    Task<ShowMediaDrawerSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Prepares and sends the selected media asset to the live media layer.</summary>
    Task<bool> TakeMediaLiveAsync(string itemId, CancellationToken cancellationToken = default);
}