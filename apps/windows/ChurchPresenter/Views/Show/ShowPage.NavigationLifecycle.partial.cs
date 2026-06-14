using ChurchPresenter.Features.Show.Output;
using ChurchPresenter.Features.Show.Shell;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ChurchPresenter.Views;

public sealed partial class ShowPage
{
    private static int _diagnosticsSequence;
    private readonly int _diagnosticsInstanceId = Interlocked.Increment(ref _diagnosticsSequence);

    int IShowNavigationLifecycleHost.DiagnosticsInstanceId => _diagnosticsInstanceId;

    ShowViewModel IShowNavigationLifecycleHost.ViewModel => ViewModel;

    ProgramOutputSurface? IShowNavigationLifecycleHost.ProgramOutputSurfaceHost =>
        OutputColumn?.PreviewHost;

    DispatcherQueue IShowNavigationLifecycleHost.DispatcherQueue => DispatcherQueue;

    public void TeardownForNavigation() => _navigationLifecycle.TeardownForNavigation();

    void IShowNavigationLifecycleHost.OnCachedRevisitLayoutRefresh()
    {
    }

    void IShowNavigationLifecycleHost.OnCachedRevisitFinished()
    {
    }

    void IShowNavigationLifecycleHost.OnStartupCompositorReady()
    {
    }
}
