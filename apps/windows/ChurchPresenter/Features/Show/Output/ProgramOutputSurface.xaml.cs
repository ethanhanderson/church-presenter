using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

using ChurchPresenter.Adapters.Rendering.Pipeline;
using ChurchPresenter.Hosting;
using ChurchPresenter.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using Windows.Foundation;

namespace ChurchPresenter.Features.Show.Output;

/// <summary>
/// Operator preview panel host. Binds its mirror view to the selected output screen canonical render.
/// </summary>
public sealed partial class ProgramOutputSurface : UserControl
{
    private const int PreviewAttachRenderingPasses = 4;
    private const int MaxPreviewAttachAttempts = 120;

    private int _deferredAttachPassesRemaining;
    private int _deferredAttachAttempts;
    private Action? _deferredAttachCompleted;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(OutputViewModel),
        typeof(ProgramOutputSurface),
        new PropertyMetadata(null));

    public ProgramOutputSurface()
    {
        InitializeComponent();
        Loaded += ProgramOutputSurface_Loaded;
        Unloaded += ProgramOutputSurface_Unloaded;
    }

    public OutputViewModel? ViewModel
    {
        get => (OutputViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Called after <see cref="IShowRenderSession.OperatorPreviewAttachAllowed"/> — not from the control Loaded handler during navigation transitions.</summary>
    public void SchedulePreviewAttachWhenCompositorReady(Action? onAttached = null)
    {
        ShowNavigationDiagnostics.Log("Preview.ScheduleWhenReady", "explicit after OnFrameReady");
        ScheduleDeferredAttach(onAttached);
    }

    public void TeardownForNavigation()
    {
        CancelDeferredAttach();
        if (ShowRenderBisect.SuppressOperatorPreviewAttach)
        {
            ShowNavigationDiagnostics.Log("Preview.Teardown", "suppressed by bisect");
            return;
        }

        ShowNavigationDiagnostics.Log("Preview.Teardown", "hide shell overlay");
        ViewModel?.DetachPreviewPage(PreviewClipHost);
    }

    private void ProgramOutputSurface_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PreviewClipHost.SizeChanged += PreviewClipHost_SizeChanged;
    }

    private void PreviewClipHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is null
            || App.Services?.GetService<IShowRenderSession>() is not { OperatorPreviewAttachAllowed: true }
            || App.Services?.GetService<OperatorPreviewSession>()?.IsOverlayVisible != true)
            return;

        ViewModel.AttachPreview(PreviewClipHost);
    }

    private void ScheduleDeferredAttach(Action? onAttached = null)
    {
        CancelDeferredAttach();
        _deferredAttachCompleted = onAttached;
        _deferredAttachPassesRemaining = PreviewAttachRenderingPasses;
        ShowNavigationDiagnostics.Log("Preview.ScheduleAttach", $"waitDispatcherPasses={PreviewAttachRenderingPasses}");
        EnqueueDeferredAttachPass();
    }

    private void EnqueueDeferredAttachPass()
    {
        if (_deferredAttachPassesRemaining <= 0)
            return;

        _deferredAttachPassesRemaining--;
        if (_deferredAttachPassesRemaining > 0)
        {
            DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
            if (queue is null)
            {
                TryAttachPreview();
                return;
            }

            queue.TryEnqueue(DispatcherQueuePriority.Low, EnqueueDeferredAttachPass);
            return;
        }

        _deferredAttachPassesRemaining = 0;
        TryAttachPreview();
    }

    private void CancelDeferredAttach()
    {
        _deferredAttachPassesRemaining = 0;
        _deferredAttachAttempts = 0;
        _deferredAttachCompleted = null;
    }

    private void TryAttachPreview()
    {
        if (ShowRenderBisect.SuppressOperatorPreviewAttach)
        {
            ShowNavigationDiagnostics.Log("Preview.Attach", "suppressed by bisect");
            CompleteDeferredAttach();
            return;
        }

        if (App.Services?.GetService<IShowRenderSession>() is { OperatorPreviewAttachAllowed: false } session)
        {
            if (_deferredAttachAttempts++ >= MaxPreviewAttachAttempts)
            {
                ShowNavigationDiagnostics.Log(
                    "Preview.Attach",
                    $"gave up waiting for OnFrameReady phase={session.ThumbnailRasterPhase}");
                CompleteDeferredAttach();
                return;
            }

            ShowNavigationDiagnostics.Log(
                "Preview.Attach",
                $"deferred phase={session.ThumbnailRasterPhase}");
            ScheduleDeferredAttach();
            return;
        }

        if (!IsRenderAnchorReady())
        {
            if (_deferredAttachAttempts++ >= MaxPreviewAttachAttempts)
            {
                ShowNavigationDiagnostics.Log("Preview.Attach", "gave up waiting for render anchor");
                CompleteDeferredAttach();
                return;
            }

            ShowNavigationDiagnostics.Log("Preview.Attach", "anchor not ready; retry next frame");
            ScheduleDeferredAttach();
            return;
        }

        _deferredAttachAttempts = 0;

        try
        {
            OperatorPreviewSession? previewSession = App.Services?.GetService<OperatorPreviewSession>();
            if (previewSession?.IsOverlayVisible == true)
            {
                ShowNavigationDiagnostics.Log("Preview.Attach", "shell overlay already visible; reposition");
                ViewModel?.AttachPreview(PreviewClipHost);
                CompleteDeferredAttach();
                return;
            }

            ShowNavigationDiagnostics.Log("Preview.Attach", "position shell overlay");
            ViewModel?.AttachPreview(PreviewClipHost);
            CompleteDeferredAttach();
        }
        catch (Exception ex)
        {
            ShowNavigationDiagnostics.Log("Preview.AttachFailed", ex.Message);
            CompleteDeferredAttach();
        }
    }

    private void CompleteDeferredAttach()
    {
        Action? onAttached = _deferredAttachCompleted;
        _deferredAttachCompleted = null;
        _deferredAttachAttempts = 0;
        if (onAttached is null)
            return;

        DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            onAttached();
            return;
        }

        queue.TryEnqueue(DispatcherQueuePriority.Low, () => onAttached());
    }

    private static bool IsRenderAnchorReady()
    {
        if (App.MainWindow is not MainWindow mainWindow)
            return false;

        return ElementCompositionPreview.GetElementVisual(mainWindow.RenderAnchorPanel)?.Compositor is not null;
    }

    private void ProgramOutputSurface_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PreviewClipHost.SizeChanged -= PreviewClipHost_SizeChanged;
        CancelDeferredAttach();
    }

}
