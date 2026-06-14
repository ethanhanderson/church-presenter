using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Services.Media;

/// <summary>
/// Host-side thumbnail cache for media browser surfaces.
/// </summary>
public interface IMediaThumbnailCache
{
    /// <summary>Returns a cached thumbnail when one has already been loaded.</summary>
    bool TryGet(string? resolvedPath, string? mediaType, uint requestedSize, out ImageSource? image);

    /// <summary>Loads and caches a thumbnail, coalescing concurrent requests for the same media file.</summary>
    Task<ImageSource?> GetOrLoadAsync(
        string? resolvedPath,
        string? mediaType,
        uint requestedSize = 768,
        CancellationToken cancellationToken = default);

    /// <summary>Clears every cached thumbnail source.</summary>
    void Clear();
}
