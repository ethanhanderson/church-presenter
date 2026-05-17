
namespace ChurchPresenter.Adapters.Media;

/// <summary>
/// Event args carrying a single media pre-warm request.
/// </summary>
public sealed class MediaPrewarmRequestedEventArgs(OutputLayerMedia media, string layerTarget) : EventArgs
{
    /// <summary>The fully resolved media payload to pre-warm.</summary>
    public OutputLayerMedia Media { get; } = media;

    /// <summary>
    /// The target slot key: <c>mediaUnderlay</c>, <c>mediaOverlay</c>, or <c>audio</c>.
    /// </summary>
    public string LayerTarget { get; } = layerTarget;
}

/// <summary>
/// Allows view-model and service layers to request that output surfaces pre-warm a media
/// source into their hidden back-buffer slot so playback can begin immediately when triggered.
/// </summary>
public interface IMediaPrewarmService
{
    /// <summary>
    /// Fired on the UI thread when a caller wants a media source pre-warmed.
    /// </summary>
    event EventHandler<MediaPrewarmRequestedEventArgs>? PreWarmRequested;

    /// <summary>
    /// Requests that output surfaces pre-warm <paramref name="media"/> into the slot identified
    /// by <paramref name="layerTarget"/> (<c>mediaUnderlay</c>, <c>mediaOverlay</c>, or <c>audio</c>).
    /// Must be called on the UI thread.
    /// </summary>
    void RequestPreWarm(OutputLayerMedia media, string layerTarget);
}
