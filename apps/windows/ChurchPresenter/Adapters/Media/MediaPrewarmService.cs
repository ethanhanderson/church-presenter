
namespace ChurchPresenter.Adapters.Media;

/// <summary>
/// Simple event-bus implementation of <see cref="IMediaPrewarmService"/>.
/// Subscribing output surfaces receive the request and pre-load the media source
/// into their hidden back-buffer slot before the operator triggers playback.
/// </summary>
public sealed class MediaPrewarmService : IMediaPrewarmService
{
    /// <inheritdoc/>
    public event EventHandler<MediaPrewarmRequestedEventArgs>? PreWarmRequested;

    /// <inheritdoc/>
    public void RequestPreWarm(OutputLayerMedia media, string layerTarget)
    {
        ArgumentNullException.ThrowIfNull(media);
        PreWarmRequested?.Invoke(this, new MediaPrewarmRequestedEventArgs(media, layerTarget));
    }
}
