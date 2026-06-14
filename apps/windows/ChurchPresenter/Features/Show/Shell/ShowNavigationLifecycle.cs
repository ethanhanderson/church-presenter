using ChurchPresenter.Features.Show.Output;
using ChurchPresenter.Hosting;
using ChurchPresenter.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ChurchPresenter.Features.Show.Shell;

internal interface IShowNavigationLifecycleHost
{
    int DiagnosticsInstanceId { get; }

    ShowViewModel ViewModel { get; }

    ProgramOutputSurface? ProgramOutputSurfaceHost { get; }

    DispatcherQueue DispatcherQueue { get; }

    void OnCachedRevisitLayoutRefresh();

    void OnCachedRevisitFinished();

    void OnStartupCompositorReady();
}

/// <summary>
/// Show page navigation lifecycle: render session phases, deck thumbnails, and preview attach timing.
/// </summary>
internal sealed class ShowNavigationLifecycle
{
    private readonly IShowNavigationLifecycleHost _host;
    private bool _hasCompletedInitialLoad;

    public ShowNavigationLifecycle(IShowNavigationLifecycleHost host)
    {
        _host = host;
    }

    public bool HasCompletedInitialLoad => _hasCompletedInitialLoad;

    public void TeardownForNavigation()
    {
        ShowNavigationDiagnostics.Log(
            "TeardownForNavigation",
            $"bisect={ShowRenderBisect.DescribeActiveFlags()}",
            _host.DiagnosticsInstanceId);
        App.Services?.GetService<IShowRenderSession>()?.BeginShowNavigationTeardown();
        App.Services?.GetService<ISlideDeckThumbnailCache>()?.CancelInflight();
        App.Services?.GetService<IShowDeckThumbnailService>()?.PrepareForNavigationThumbnails(_host.ViewModel);
        App.Services?.GetService<IShowDeckThumbnailService>()?.CancelWarm();
        App.Services?.GetService<IShowDeckThumbnailService>()?.ScheduleFinalizeAfterLeave(_host.ViewModel);
        _host.ProgramOutputSurfaceHost?.TeardownForNavigation();
    }

    public void OnLoaded()
    {
        App.Services?.GetService<IShowRenderSession>()?.MarkShowReturning();
        ShowNavigationDiagnostics.Log(
            "Loaded",
            $"revisit={_hasCompletedInitialLoad}",
            _host.DiagnosticsInstanceId);

        if (_hasCompletedInitialLoad)
        {
            _ = RunCachedRevisitLoadedAsync();
            return;
        }

        _ = RunDeferredShowPageLoadedAsync();
    }

    public void OnUnloaded()
    {
        ShowNavigationDiagnostics.Log("Unloaded", $"instance={_host.DiagnosticsInstanceId}", _host.DiagnosticsInstanceId);
        App.Services?.GetService<IShowDeckThumbnailService>()?.OnShowUnloaded(_host.ViewModel);
    }

    private async Task RunCachedRevisitLoadedAsync()
    {
        IShowRenderSession? session = App.Services?.GetService<IShowRenderSession>();
        CancellationToken navigationToken = session?.NavigationWorkToken ?? CancellationToken.None;
        try
        {
            ShowNavigationDiagnostics.Log("CachedRevisit", "await slide transition", _host.DiagnosticsInstanceId);
            await Task.Delay(400, navigationToken).ConfigureAwait(true);

            ScheduleAfterCompositorSettled(
                () =>
                {
                    _host.OnCachedRevisitLayoutRefresh();
                    _ = FinishCachedRevisitAfterTransitionAsync(navigationToken);
                },
                passes: 2);
        }
        catch (OperationCanceledException)
        {
            ShowNavigationDiagnostics.Log("CachedRevisit", "cancelled", _host.DiagnosticsInstanceId);
        }
    }

    private async Task FinishCachedRevisitAfterTransitionAsync(CancellationToken navigationToken)
    {
        try
        {
            await Task.Delay(350, navigationToken).ConfigureAwait(true);
            ShowNavigationDiagnostics.Log("CachedRevisit", "restore deck + compositor", _host.DiagnosticsInstanceId);

            IShowDeckThumbnailService? thumbnails = App.Services?.GetService<IShowDeckThumbnailService>();
            DeckThumbnailVisibilityDiagnostics.LogMoment("revisit.beforeWarm", _host.ViewModel);
            thumbnails?.OnShowCompositorReady(_host.ViewModel, cachedRevisit: true);
            thumbnails?.BeginRevisitDeckWarm(
                _host.ViewModel,
                ScheduleOperatorPreviewAttach);

            _host.OnCachedRevisitFinished();
        }
        catch (OperationCanceledException)
        {
            ShowNavigationDiagnostics.Log("CachedRevisit", "finish cancelled", _host.DiagnosticsInstanceId);
        }
    }

    private void ScheduleAfterCompositorSettled(Action action, int passes = 4)
    {
        ShowNavigationDiagnostics.Log("CompositorSettled", $"defer dispatcher passes={passes}", _host.DiagnosticsInstanceId);

        void EnqueuePass(int remaining)
        {
            if (remaining > 1)
            {
                _host.DispatcherQueue.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () => EnqueuePass(remaining - 1));
                return;
            }

            ShowNavigationDiagnostics.Log("CompositorSettled", "completed", _host.DiagnosticsInstanceId);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _host.ViewModel.StatusMessage = $"Show reload failed: {ex.Message}";
                ShowNavigationDiagnostics.Log("ReloadFailed", ex.Message, _host.DiagnosticsInstanceId);
            }
        }

        EnqueuePass(passes);
    }

    private async Task RunDeferredShowPageLoadedAsync()
    {
        try
        {
            DeckThumbnailVisibilityDiagnostics.LogMoment("startup.beforeInit", _host.ViewModel);
            await _host.ViewModel.InitializeAsync();
            DeckThumbnailVisibilityDiagnostics.LogMoment("startup.afterInit", _host.ViewModel);

            _hasCompletedInitialLoad = true;

            App.Services?.GetService<IShowDeckThumbnailService>()?.BeginCachePrefetch(_host.ViewModel);

            _host.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Normal,
                StartStartupDeckThumbnails);

            _ = _host.ViewModel.LoadShowControlsAsync();
        }
        catch (Exception ex)
        {
            _host.ViewModel.StatusMessage = $"Show startup failed: {ex.Message}";
        }
    }

    private void StartStartupDeckThumbnails()
    {
        App.Services?.GetService<IShowDeckThumbnailService>()?.OnShowCompositorReady(_host.ViewModel);
        ScheduleOperatorPreviewAttach();
        _host.OnStartupCompositorReady();
    }

    private void ScheduleOperatorPreviewAttach()
    {
        ScheduleAfterCompositorSettled(
            () => _host.ProgramOutputSurfaceHost?.SchedulePreviewAttachWhenCompositorReady(),
            passes: 4);
    }
}
