#nullable enable

using ChurchPresenter.Adapters.Rendering.Pipeline;
using ChurchPresenter.Hosting;

using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// A framework-managed XAML brush that paints a logical screen's canonical render. Wraps the
/// per-screen <see cref="CompositionVisualSurface"/> exposed by <see cref="IProgramRenderService"/> in a
/// <see cref="CompositionSurfaceBrush"/> and surfaces it through <see cref="XamlCompositionBrushBase"/>.
/// </summary>
public sealed class ScreenMirrorBrush : XamlCompositionBrushBase
{
    private readonly IProgramRenderService _service;
    private string _screenId;
    private bool _usePreviewSurface;
    private bool _connected;
    private bool _deferredCompositionClearScheduled;

    /// <summary>Creates a mirror brush bound to the given screen's canonical render.</summary>
    public ScreenMirrorBrush(IProgramRenderService service, string screenId)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _screenId = string.IsNullOrWhiteSpace(screenId) ? "audience" : screenId;
    }

    /// <summary>When true, paints the preview-sized render instead of the canonical output surface.</summary>
    public bool UsePreviewSurface
    {
        get => _usePreviewSurface;
        set
        {
            if (_usePreviewSurface == value)
                return;

            _usePreviewSurface = value;
            if (_connected)
                UpdateBrush();
        }
    }

    /// <summary>Re-points the brush at another screen's canonical render (instant brush swap).</summary>
    public void SetScreen(string screenId)
    {
        if (string.IsNullOrWhiteSpace(screenId) || string.Equals(_screenId, screenId, StringComparison.OrdinalIgnoreCase))
            return;

        _screenId = screenId;
        if (_connected)
            UpdateBrush();
    }

    /// <inheritdoc />
    protected override void OnConnected()
    {
        _connected = true;
        _deferredCompositionClearScheduled = false;
        _service.ScreensChanged += OnScreensChanged;
        ShowNavigationDiagnostics.Log("MirrorBrush.OnConnected", $"screen={_screenId} brushHash={GetHashCode()}");
        UpdateBrush();
    }

    /// <inheritdoc />
    protected override void OnDisconnected()
    {
        _connected = false;
        _service.ScreensChanged -= OnScreensChanged;
        ShowNavigationDiagnostics.Log("MirrorBrush.OnDisconnected", $"screen={_screenId} brushHash={GetHashCode()}");

        ScheduleDeferredCompositionClear();
    }

    private void ScheduleDeferredCompositionClear()
    {
        if (_deferredCompositionClearScheduled)
            return;

        _deferredCompositionClearScheduled = true;
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (dispatcherQueue is null)
        {
            if (!_connected)
                CompositionBrush = null;

            _deferredCompositionClearScheduled = false;
            return;
        }

        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _deferredCompositionClearScheduled = false;
            if (_connected)
                return;

            CompositionBrush = null;
        });
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_connected)
            UpdateBrush();
    }

    private void UpdateBrush()
    {
        Compositor compositor = ResolveCompositor();
        CompositionVisualSurface? surface = _usePreviewSurface
            ? _service.GetPreviewSurface(_screenId)
            : _service.GetScreenSurface(_screenId);
        if (surface is null)
        {
            CompositionBrush = null;
            ShowNavigationDiagnostics.Log(
                "MirrorBrush.UpdateBrush",
                $"screen={_screenId} preview={_usePreviewSurface} surface=null");
            return;
        }

        // Drop wrapper reference only; eager Dispose during cached Show reload races the compositor.
        CompositionBrush = null;

        CompositionSurfaceBrush brush = compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        brush.HorizontalAlignmentRatio = 0f;
        brush.VerticalAlignmentRatio = 0f;
        CompositionBrush = brush;
        ShowNavigationDiagnostics.Log(
            "MirrorBrush.UpdateBrush",
            $"screen={_screenId} preview={_usePreviewSurface} ok");
    }

    private static Compositor ResolveCompositor()
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            var visual = ElementCompositionPreview.GetElementVisual(mainWindow.RenderAnchorPanel);
            if (visual?.Compositor is Compositor anchorCompositor)
                return anchorCompositor;
        }

        return CompositionTarget.GetCompositorForCurrentThread();
    }
}
