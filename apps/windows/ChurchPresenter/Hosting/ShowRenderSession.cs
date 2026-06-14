namespace ChurchPresenter.Hosting;

/// <inheritdoc />
public sealed class ShowRenderSession : IShowRenderSession
{
    private int _generation;
    private DeckThumbnailRasterPhase _thumbnailRasterPhase = DeckThumbnailRasterPhase.OnFrameReady;
    private CancellationTokenSource _navigationWorkCts = new();

    /// <inheritdoc />
    public int Generation => Volatile.Read(ref _generation);

    /// <inheritdoc />
    public DeckThumbnailRasterPhase ThumbnailRasterPhase => _thumbnailRasterPhase;

    /// <inheritdoc />
    public bool OperatorPreviewAttachAllowed => _thumbnailRasterPhase == DeckThumbnailRasterPhase.OnFrameReady;

    /// <inheritdoc />
    public CancellationToken NavigationWorkToken => _navigationWorkCts.Token;

    /// <inheritdoc />
    public int BeginShowNavigationTeardown()
    {
        _thumbnailRasterPhase = DeckThumbnailRasterPhase.Blocked;
        CancelNavigationWork();
        int next = Interlocked.Increment(ref _generation);
        ShowNavigationDiagnostics.Log("RenderSession.Teardown", $"generation={next} phase={_thumbnailRasterPhase}");
        return next;
    }

    /// <inheritdoc />
    public void MarkShowOffFrame()
    {
        _thumbnailRasterPhase = DeckThumbnailRasterPhase.OffFrame;
        ShowNavigationDiagnostics.Log("RenderSession.OffFrame", $"generation={Generation} phase={_thumbnailRasterPhase}");
    }

    /// <inheritdoc />
    public void MarkShowReturning()
    {
        _thumbnailRasterPhase = DeckThumbnailRasterPhase.Returning;
        ShowNavigationDiagnostics.Log("RenderSession.Returning", $"generation={Generation} phase={_thumbnailRasterPhase}");
    }

    /// <inheritdoc />
    public void MarkShowOnFrameCompositorReady()
    {
        _thumbnailRasterPhase = DeckThumbnailRasterPhase.OnFrameReady;
        ShowNavigationDiagnostics.Log("RenderSession.OnFrameReady", $"generation={Generation} phase={_thumbnailRasterPhase}");
    }

    private void CancelNavigationWork()
    {
        try
        {
            _navigationWorkCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _navigationWorkCts.Dispose();
        _navigationWorkCts = new CancellationTokenSource();
    }
}
