namespace ChurchPresenter.Hosting;

/// <summary>
/// When deck slide thumbnails and operator preview may touch the compositor during cached Show navigation.
/// </summary>
public enum DeckThumbnailRasterPhase
{
    /// <summary>Show leave/reload in progress — no deck raster or preview attach.</summary>
    Blocked,

    /// <summary>Show unloaded from the tree — off-frame deck cache warm only.</summary>
    OffFrame,

    /// <summary>Show is loading/reloading on-frame — wait for compositor settle.</summary>
    Returning,

    /// <summary>Show on-frame and compositor settled — on-frame warm and preview attach.</summary>
    OnFrameReady,
}

/// <summary>
/// App-lifetime coordination for Show render surfaces across cached frame navigation.
/// </summary>
public interface IShowRenderSession
{
    /// <summary>Increments when Show leaves the frame; stale thumbnail work should abort.</summary>
    int Generation { get; }

    /// <summary>Current raster/preview gate.</summary>
    DeckThumbnailRasterPhase ThumbnailRasterPhase { get; }

    /// <summary>True when operator preview may attach to the Show preview slot.</summary>
    bool OperatorPreviewAttachAllowed { get; }

    /// <summary>Cancelled when Show begins navigation teardown; in-flight deck raster should stop.</summary>
    CancellationToken NavigationWorkToken { get; }

    /// <summary>Bumps generation, blocks raster, and cancels in-flight work before navigating away from Show.</summary>
    int BeginShowNavigationTeardown();

    /// <summary>Show <see cref="Microsoft.UI.Xaml.FrameworkElement.Unloaded"/> fired — off-frame warm may be scheduled.</summary>
    void MarkShowOffFrame();

    /// <summary>Show <see cref="Microsoft.UI.Xaml.FrameworkElement.Loaded"/> fired — block preview/warm until settle.</summary>
    void MarkShowReturning();

    /// <summary>Show is visible and compositor has settled; on-frame cache warm and preview attach may run.</summary>
    void MarkShowOnFrameCompositorReady();
}
