using ChurchPresenter.Hosting;
using ChurchPresenter.ViewModels;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
internal sealed class ShowDeckThumbnailService(
    IShowRenderSession renderSession,
    ISlideDeckThumbnailCache thumbnailCache) : IShowDeckThumbnailService
{
    private const int RevisitCompositorWarmPasses = 8;
    private const int MaxRasterPhaseWaitIterations = 600;

    private readonly IShowRenderSession _renderSession =
        renderSession ?? throw new ArgumentNullException(nameof(renderSession));

    private readonly ISlideDeckThumbnailCache _thumbnailCache =
        thumbnailCache ?? throw new ArgumentNullException(nameof(thumbnailCache));

    private int _warmGeneration;

    private Action? _warmQueueComplete;

    /// <inheritdoc />
    public void CancelWarm()
    {
        int gen = Interlocked.Increment(ref _warmGeneration);
        ShowNavigationDiagnostics.Log("DeckThumb.CancelWarm", $"warmGeneration={gen}");
    }

    /// <inheritdoc />
    public void PrepareForNavigationThumbnails(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        DeckThumbnailVisibilityDiagnostics.LogMoment("leave.before", show);

        int keptCompositor = 0;
        int keptNav = 0;
        int empty = 0;
        foreach (ShowSlideDeckItem item in show.EnumerateDeckThumbnailItems())
        {
            string cacheKey = item.GetThumbnailCacheKey();
            if (item.Thumbnail is not null && !item.IsNavigationSnapshot)
                item.ReleasePublishedThumbnail("leave.releaseCompositor");

            _thumbnailCache.EvictCompositorSurface(cacheKey);

            if (item.Thumbnail is null)
                empty++;
            else if (item.IsNavigationSnapshot)
                keptNav++;
            else
                keptCompositor++;
        }

        DeckThumbnailVisibilityDiagnostics.LogMoment(
            "leave.after",
            show,
            $"keptCompositor={keptCompositor} keptNav={keptNav} empty={empty}");
    }

    /// <inheritdoc />
    public void ScheduleFinalizeAfterLeave(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        // Binding release runs only from Show Unloaded — deferred release while another
        // page is active was causing compositor fail-fast during Editor navigation.
    }

    /// <inheritdoc />
    public void FinalizeHiddenThumbnails(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        DeckThumbnailVisibilityDiagnostics.LogMoment("unload.before", show);
        int released = 0;
        foreach (ShowSlideDeckItem item in show.EnumerateDeckThumbnailItems())
        {
            if (item.Thumbnail is null)
                continue;

            item.ReleasePublishedThumbnail("unload.hidden");
            released++;
        }

        DeckThumbnailVisibilityDiagnostics.LogMoment(
            "unload.after",
            show,
            $"released={released}");
    }

    /// <inheritdoc />
    public void BindVisibleCachedThumbnails(ShowViewModel show, string moment)
    {
        ArgumentNullException.ThrowIfNull(show);
        int bound = BindEarliestCachedDisplay(show, moment);
        ShowNavigationDiagnostics.Log("DeckThumb.BindVisible", $"moment={moment} bound={bound}");
        DeckThumbnailVisibilityDiagnostics.LogMoment(moment, show, $"earlyBound={bound}");
    }

    /// <inheritdoc />
    public void BeginRestoreNavigationDisplayOnLoaded(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        _ = RestoreNavigationDisplayOnLoadedAsync(show);
    }

    /// <inheritdoc />
    public Task RestoreNavigationDisplayOnLoadedAsync(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return RestoreNavigationDisplayOnLoadedCoreAsync(show);
    }

    /// <inheritdoc />
    public void BeginCachePrefetch(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.Prefetch", "suppressed by bisect");
            return;
        }

        _ = PrefetchCacheWarmAsync(show);
    }

    /// <inheritdoc />
    public void OnShowUnloaded(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.OnShowUnloaded", "suppressed by bisect");
            return;
        }

        _renderSession.MarkShowOffFrame();
        FinalizeHiddenThumbnails(show);
    }

    /// <inheritdoc />
    public void RefreshDeckThumbnails(ShowViewModel show, string moment) =>
        RefreshDeckThumbnailsCore(show, items: null, moment);

    /// <inheritdoc />
    public void RefreshDeckThumbnail(ShowViewModel show, ShowSlideDeckItem item, string moment)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(item);
        RefreshDeckThumbnailsCore(show, new[] { item }, moment);
    }

    /// <inheritdoc />
    public void RefreshDeckThumbnailContent(ShowViewModel show, ShowSlideDeckItem item, string moment)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(item);
        RefreshDeckThumbnailContentCore(show, item, moment);
    }

    /// <inheritdoc />
    public void BeginRevisitDeckWarm(ShowViewModel show, Action onComplete)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(onComplete);

        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.RevisitWarm", "suppressed by bisect");
            onComplete();
            return;
        }

        _warmQueueComplete = onComplete;
        int warmGeneration = Volatile.Read(ref _warmGeneration);

        List<ShowSlideDeckItem> items = show.EnumerateDeckThumbnailItems()
            .Where(static item => item.Thumbnail is null)
            .Distinct()
            .ToList();
        if (items.Count == 0)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.RevisitWarm", "already bound");
            InvokeWarmQueueComplete();
            return;
        }

        ShowNavigationDiagnostics.Log("DeckThumb.RevisitWarm", $"count={items.Count}");
        ScheduleWarmAfterRenderingPasses(show, offFrame: false, warmGeneration, RevisitCompositorWarmPasses);
    }

    /// <inheritdoc />
    public void OnRevisitPreviewAttached(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        ShowNavigationDiagnostics.Log("DeckThumb.OnRevisitPreviewAttached", "noop");
    }

    /// <inheritdoc />
    public void OnThemeChanged(ShowViewModel show)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.OnThemeChanged", "suppressed by bisect");
            return;
        }

        int bound = BindEarliestCachedDisplay(show, "theme.changed");
        ShowNavigationDiagnostics.Log(
            "DeckThumb.OnThemeChanged",
            $"bound={bound} phase={_renderSession.ThumbnailRasterPhase}");

        if (_renderSession.ThumbnailRasterPhase != DeckThumbnailRasterPhase.OnFrameReady)
            return;

        List<ShowSlideDeckItem> missing = show.EnumerateDeckThumbnailItems()
            .Where(static item => item.Thumbnail is null)
            .Distinct()
            .ToList();
        if (missing.Count == 0)
            return;

        int warmGeneration = Volatile.Read(ref _warmGeneration);
        ShowNavigationDiagnostics.Log("DeckThumb.OnThemeChanged", $"warmMissing count={missing.Count}");
        ScheduleWarm(show, missing, 0, warmGeneration, offFrame: false, startup: false);
    }

    /// <inheritdoc />
    public void OnShowCompositorReady(ShowViewModel show, bool cachedRevisit = false)
    {
        ArgumentNullException.ThrowIfNull(show);
        _renderSession.MarkShowOnFrameCompositorReady();

        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.OnCompositorReady", "suppressed by bisect");
            return;
        }

        int warmGeneration = Volatile.Read(ref _warmGeneration);
        DeckThumbnailVisibilityDiagnostics.LogMoment(
            cachedRevisit ? "compositorReady.revisit" : "compositorReady.startup",
            show,
            $"warmGeneration={warmGeneration}");

        if (cachedRevisit)
        {
            ShowNavigationDiagnostics.Log(
                "DeckThumb.OnCompositorReady",
                $"cachedRevisit deferred warm warmGeneration={warmGeneration}");
            return;
        }

        if (ShowRenderBisect.DelayThumbnailUntilShowSettled)
        {
            BindVisibleCachedThumbnails(show, "compositorReady.delayed.sync");
            ShowNavigationDiagnostics.Log(
                "DeckThumb.OnCompositorReady",
                $"delayed compositorWarmPasses={RevisitCompositorWarmPasses} warmGeneration={warmGeneration}");
            ScheduleWarmAfterRenderingPasses(show, offFrame: false, warmGeneration, RevisitCompositorWarmPasses);
            return;
        }

        BindVisibleCachedThumbnails(show, "compositorReady.startup.sync");
        ShowNavigationDiagnostics.Log(
            "DeckThumb.OnCompositorReady",
            $"startup immediate warmGeneration={warmGeneration}");
        QueueWarmStartup(show, warmGeneration);
    }

    private async Task PrefetchCacheWarmAsync(ShowViewModel show)
    {
        List<ShowSlideDeckItem> items = show.EnumerateDeckThumbnailItems().Distinct().ToList();
        if (items.Count == 0)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.Prefetch", "no deck items");
            return;
        }

        try
        {
            await WaitForOnFrameRasterPhaseAsync(_renderSession.NavigationWorkToken, _renderSession).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.Prefetch", "cancelled before raster phase ready");
            return;
        }

        await PrefetchOnFrameAsync(show, items).ConfigureAwait(true);
    }

    private async Task PrefetchOnFrameAsync(ShowViewModel show, IReadOnlyList<ShowSlideDeckItem> items)
    {
        int warmGeneration = Volatile.Read(ref _warmGeneration);
        ShowNavigationDiagnostics.Log(
            "DeckThumb.Prefetch",
            $"onFrame count={items.Count} phase={_renderSession.ThumbnailRasterPhase} warmGeneration={warmGeneration}");

        foreach (ShowSlideDeckItem item in items)
        {
            if (warmGeneration != Volatile.Read(ref _warmGeneration))
            {
                ShowNavigationDiagnostics.Log("DeckThumb.Prefetch", "cancelled (warm generation bumped)");
                return;
            }

            if (item.Thumbnail is not null && !item.IsNavigationSnapshot)
                continue;

            try
            {
                await item.WarmThumbnailCacheAsync(_renderSession.NavigationWorkToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                ShowNavigationDiagnostics.Log("DeckThumb.Prefetch", "cancelled");
                return;
            }
        }

        DeckThumbnailVisibilityDiagnostics.LogMoment("prefetch.complete", show);
    }

    private async Task RestoreNavigationDisplayOnLoadedCoreAsync(ShowViewModel show)
    {
        if (_renderSession.ThumbnailRasterPhase != DeckThumbnailRasterPhase.OnFrameReady)
        {
            ShowNavigationDiagnostics.Log(
                "DeckThumb.RestoreNavigation",
                $"skipped phase={_renderSession.ThumbnailRasterPhase}");
            return;
        }

        DeckThumbnailVisibilityDiagnostics.LogMoment("revisit.loaded.before", show);
        int restored = 0;
        int alreadyVisible = 0;
        try
        {
            foreach (ShowSlideDeckItem item in show.EnumerateDeckThumbnailItems())
            {
                if (item.Thumbnail is not null)
                {
                    alreadyVisible++;
                    continue;
                }

                string cacheKey = item.GetThumbnailCacheKey();
                ImageSource? display = null;
                if (_thumbnailCache.TryTakeNavigationDisplaySpare(cacheKey, out ImageSource? spare) && spare is not null)
                    display = spare;
                else if (_thumbnailCache.HasNavigationPixels(cacheKey))
                {
                    display = await _thumbnailCache
                        .CreateNavigationDisplaySourceAsync(cacheKey, _renderSession.NavigationWorkToken)
                        .ConfigureAwait(true);
                }

                if (display is null)
                    continue;

                item.BindNavigationDisplay(display, "revisit.loaded");
                restored++;
            }
        }
        catch (OperationCanceledException)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.RestoreNavigation", "cancelled on Loaded");
            return;
        }

        DeckThumbnailVisibilityDiagnostics.LogMoment(
            "revisit.loaded.after",
            show,
            $"restored={restored} alreadyVisible={alreadyVisible}");
    }

    private void RefreshDeckThumbnailContentCore(ShowViewModel show, ShowSlideDeckItem item, string moment)
    {
        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.RefreshContent", $"suppressed moment={moment}");
            return;
        }

        string cacheKey = item.GetThumbnailCacheKey();
        _thumbnailCache.CancelInflight(cacheKey);
        _thumbnailCache.EvictCompositorSurface(cacheKey);

        DeckThumbnailRasterPhase phase = _renderSession.ThumbnailRasterPhase;
        ShowNavigationDiagnostics.Log(
            "DeckThumb.RefreshContent",
            $"moment={moment} slide={item.Slide.Id} phase={phase}");

        if (phase is DeckThumbnailRasterPhase.Blocked or DeckThumbnailRasterPhase.Returning)
            return;

        int warmGeneration = Volatile.Read(ref _warmGeneration);

        if (phase == DeckThumbnailRasterPhase.OffFrame)
        {
            QueueWarm(show, offFrame: true, warmGeneration);
            return;
        }

        if (!item.IsThumbnailContentRefreshPending)
            return;

        ScheduleWarm(show, new[] { item }, 0, warmGeneration, offFrame: false, startup: false);
    }

    private void RefreshDeckThumbnailsCore(
        ShowViewModel show,
        IReadOnlyList<ShowSlideDeckItem>? items,
        string moment)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.Refresh", $"suppressed moment={moment}");
            return;
        }

        List<ShowSlideDeckItem> targets = (items ?? show.EnumerateDeckThumbnailItems().ToList())
            .Distinct()
            .ToList();
        if (targets.Count == 0)
            return;

        _thumbnailCache.CancelInflight();
        foreach (ShowSlideDeckItem item in targets)
            _thumbnailCache.InvalidatePrefix(item.GetThumbnailCacheKeyPrefix());

        DeckThumbnailRasterPhase phase = _renderSession.ThumbnailRasterPhase;
        ShowNavigationDiagnostics.Log(
            "DeckThumb.Refresh",
            $"moment={moment} count={targets.Count} phase={phase}");

        if (phase is DeckThumbnailRasterPhase.Blocked or DeckThumbnailRasterPhase.Returning)
            return;

        int warmGeneration = Interlocked.Increment(ref _warmGeneration);

        if (phase == DeckThumbnailRasterPhase.OffFrame)
        {
            QueueWarm(show, offFrame: true, warmGeneration);
            return;
        }

        BindEarliestCachedDisplay(show, $"refresh.{moment}");
        List<ShowSlideDeckItem> warmTargets = targets
            .Where(static item => item.Thumbnail is null || item.IsThumbnailContentRefreshPending)
            .ToList();
        if (warmTargets.Count == 0)
            return;

        ScheduleWarm(show, warmTargets, 0, warmGeneration, offFrame: false, startup: false);
    }

    private int BindEarliestCachedDisplay(ShowViewModel show, string moment)
    {
        int bound = 0;
        foreach (ShowSlideDeckItem item in show.EnumerateDeckThumbnailItems())
        {
            if (item.Thumbnail is not null)
                continue;

            string cacheKey = item.GetThumbnailCacheKey();
            if (_thumbnailCache.TryGet(cacheKey, out ImageSource? compositor) && compositor is not null)
            {
                item.CommitCompositorThumbnail(compositor);
                bound++;
                continue;
            }

            if (_thumbnailCache.TryTakeNavigationDisplaySpare(cacheKey, out ImageSource? spare) && spare is not null)
            {
                item.BindNavigationDisplay(spare, moment);
                bound++;
            }
        }

        return bound;
    }

    private void ScheduleWarmAfterRenderingPasses(
        ShowViewModel show,
        bool offFrame,
        int warmGeneration,
        int passes)
    {
        ShowNavigationDiagnostics.Log(
            "DeckThumb.WarmDeferred",
            $"deferDispatcherPasses={passes} offFrame={offFrame} warmGeneration={warmGeneration}");

        void EnqueuePass(int remaining)
        {
            if (warmGeneration != Volatile.Read(ref _warmGeneration))
            {
                ShowNavigationDiagnostics.Log("DeckThumb.WarmDeferred", "cancelled (warm generation bumped)");
                return;
            }

            if (remaining > 1)
            {
                DispatcherQueue? queue =
                    DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
                if (queue is null)
                {
                    _ = DeferWarmPassAsync(show, offFrame, warmGeneration, remaining - 1);
                    return;
                }

                queue.TryEnqueue(DispatcherQueuePriority.Low, () => EnqueuePass(remaining - 1));
                return;
            }

            if (warmGeneration != Volatile.Read(ref _warmGeneration))
                return;

            if (!CanRaster(_renderSession.ThumbnailRasterPhase, offFrame))
            {
                ShowNavigationDiagnostics.Log(
                    "DeckThumb.WarmDeferred",
                    $"skipped phase={_renderSession.ThumbnailRasterPhase} offFrame={offFrame}");
                return;
            }

            QueueWarm(show, offFrame, warmGeneration);
        }

        EnqueuePass(passes);
    }

    private async Task DeferWarmPassAsync(ShowViewModel show, bool offFrame, int warmGeneration, int remaining)
    {
        try
        {
            await Task.Delay(16, _renderSession.NavigationWorkToken).ConfigureAwait(true);
            if (warmGeneration != Volatile.Read(ref _warmGeneration))
                return;

            if (remaining > 1)
            {
                DispatcherQueue? queue =
                    DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
                if (queue is null)
                {
                    await DeferWarmPassAsync(show, offFrame, warmGeneration, remaining - 1).ConfigureAwait(true);
                    return;
                }

                queue.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () => ScheduleWarmAfterRenderingPasses(show, offFrame, warmGeneration, remaining - 1));
                return;
            }

            if (!CanRaster(_renderSession.ThumbnailRasterPhase, offFrame))
            {
                ShowNavigationDiagnostics.Log(
                    "DeckThumb.WarmDeferred",
                    $"skipped phase={_renderSession.ThumbnailRasterPhase} offFrame={offFrame}");
                return;
            }

            QueueWarm(show, offFrame, warmGeneration);
        }
        catch (OperationCanceledException)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.WarmDeferred", "cancelled");
        }
    }

    private void QueueWarmStartup(ShowViewModel show, int warmGeneration)
    {
        List<ShowSlideDeckItem> items = show.EnumerateDeckThumbnailItems().Distinct().ToList();
        if (items.Count == 0)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.QueueWarm", "startup no deck items");
            return;
        }

        ShowNavigationDiagnostics.Log(
            "DeckThumb.QueueWarm",
            $"startup count={items.Count} phase={_renderSession.ThumbnailRasterPhase}");
        ScheduleWarm(show, items, 0, warmGeneration, offFrame: false, startup: true);
    }

    private void QueueWarm(ShowViewModel show, bool offFrame, int warmGeneration)
    {
        List<ShowSlideDeckItem> items = show.EnumerateDeckThumbnailItems().Distinct().ToList();
        if (items.Count == 0)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.QueueWarm", "no deck items");
            return;
        }

        ShowNavigationDiagnostics.Log(
            "DeckThumb.QueueWarm",
            $"offFrame={offFrame} count={items.Count} phase={_renderSession.ThumbnailRasterPhase}");
        ScheduleWarm(show, items, 0, warmGeneration, offFrame, startup: false);
    }

    private void ScheduleWarm(
        ShowViewModel show,
        IReadOnlyList<ShowSlideDeckItem> items,
        int index,
        int warmGeneration,
        bool offFrame,
        bool startup)
    {
        if (index >= items.Count)
        {
            ShowNavigationDiagnostics.Log("DeckThumb.WarmComplete", $"offFrame={offFrame} count={items.Count}");
            DeckThumbnailVisibilityDiagnostics.LogMoment("warm.complete", show);
            InvokeWarmQueueComplete();
            return;
        }

        DispatcherQueue? dispatcherQueue =
            DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (dispatcherQueue is null)
        {
            _ = WarmAtAsync(show, items, index, warmGeneration, offFrame, startup);
            return;
        }

        DispatcherQueuePriority priority = startup
            ? DispatcherQueuePriority.Normal
            : DispatcherQueuePriority.Low;
        dispatcherQueue.TryEnqueue(
            priority,
            () => _ = WarmAtAsync(show, items, index, warmGeneration, offFrame, startup));
    }

    private async Task WarmAtAsync(
        ShowViewModel show,
        IReadOnlyList<ShowSlideDeckItem> items,
        int index,
        int warmGeneration,
        bool offFrame,
        bool startup = false)
    {
        if (warmGeneration != Volatile.Read(ref _warmGeneration))
            return;

        if (!CanRaster(_renderSession.ThumbnailRasterPhase, offFrame))
        {
            ShowNavigationDiagnostics.Log(
                "DeckThumb.WarmAt",
                $"aborted index={index} phase={_renderSession.ThumbnailRasterPhase}");
            return;
        }

        ShowSlideDeckItem item = items[index];
        if (item.Thumbnail is null || item.IsNavigationSnapshot || item.IsThumbnailContentRefreshPending)
        {
            ShowNavigationDiagnostics.Log(
                "DeckThumb.WarmAt",
                $"raster slide={item.Slide.Id} index={index}/{items.Count} offFrame={offFrame} navSnapshot={item.IsNavigationSnapshot}");
            try
            {
                await item.WarmThumbnailCacheAsync(_renderSession.NavigationWorkToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                ShowNavigationDiagnostics.Log("DeckThumb.WarmAt", $"cancelled slide={item.Slide.Id}");
                return;
            }
        }

        ScheduleWarm(show, items, index + 1, warmGeneration, offFrame, startup);
    }

    private void InvokeWarmQueueComplete()
    {
        Action? complete = _warmQueueComplete;
        _warmQueueComplete = null;
        complete?.Invoke();
    }

    private static bool CanRaster(DeckThumbnailRasterPhase phase, bool offFrame) =>
        offFrame
            ? phase == DeckThumbnailRasterPhase.OffFrame
            : phase == DeckThumbnailRasterPhase.OnFrameReady;

    private static async Task WaitForOnFrameRasterPhaseAsync(
        CancellationToken cancellationToken,
        IShowRenderSession session)
    {
        int iterations = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeckThumbnailRasterPhase phase = session.ThumbnailRasterPhase;
            if (phase == DeckThumbnailRasterPhase.OnFrameReady)
                return;

            if (phase == DeckThumbnailRasterPhase.Blocked)
                throw new OperationCanceledException();

            if (++iterations >= MaxRasterPhaseWaitIterations)
                throw new OperationCanceledException();

            await Task.Delay(16, cancellationToken).ConfigureAwait(true);
        }
    }
}
