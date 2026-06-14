#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;

using Windows.Foundation;
using Windows.Media.Playback;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Bridges a <see cref="MediaPlayer"/> into the Microsoft.UI composition tree as a real
/// layer using frame-server mode. WinUI 3 cannot consume <see cref="MediaPlayer.GetSurface"/>
/// (system compositor surface), so each available frame is copied to a Win2D
/// <see cref="CanvasRenderTarget"/> (an <c>IDirect3DSurface</c>) and blitted into a
/// <see cref="CompositionDrawingSurface"/> exposed as a <see cref="CompositionSurfaceBrush"/>.
/// The brush composites, masks, and fades off the UI thread like any other layer and can be
/// shared so audience and operator preview show the exact same frame.
/// <para>
/// <b>Contract:</b> the caller is responsible for setting
/// <see cref="MediaPlayer.IsVideoFrameServerEnabled"/> to <c>true</c> on the player
/// <em>before</em> any media source is opened on it.  Changing that property after the source
/// is already open has no effect (Windows Media documentation), causing
/// <see cref="MediaPlayer.VideoFrameAvailable"/> to never fire and the surface to stay frozen.
/// </para>
/// </summary>
public sealed class VideoSurfaceRenderer : IDisposable
{
    private readonly object _gate = new();
    private readonly Compositor _compositor;
    private readonly DispatcherQueue? _dispatcherQueue;
    private CanvasDevice _canvasDevice;
    private readonly CompositionGraphicsDevice _graphicsDevice;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceBrush? _brush;
    private CanvasRenderTarget? _frameTarget;
    private MediaPlayer? _player;
    private int _frameWidth;
    private int _frameHeight;
    private int _renderScheduled;
    private bool _disposed;
    private TaskCompletionSource<object?> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initializes a video surface renderer bound to one compositor.</summary>
    public VideoSurfaceRenderer(Compositor compositor)
    {
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));

        // Capture the dispatcher of the thread that owns the compositor (the UI thread). Frame-server
        // callbacks arrive on a worker thread, but the CompositionDrawingSurface is affine to this
        // dispatcher, so all surface drawing is marshalled back here.
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _canvasDevice = CanvasDevice.GetSharedDevice();
        _canvasDevice.DeviceLost += OnDeviceLost;
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
    }

    /// <summary>
    /// Wires frame-server callbacks on the player and returns a brush that paints its frames
    /// at the source's native framerate. Rebinding to the same player reuses the existing surface.
    /// The player must already have <see cref="MediaPlayer.IsVideoFrameServerEnabled"/> set to
    /// <c>true</c> before its source was first opened (see class-level contract).
    /// </summary>
    public CompositionSurfaceBrush? CreateBrush(MediaPlayer player, string? fit = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!ReferenceEquals(player, _player))
        {
            Unsubscribe();
            _player = player;
            _firstFrame = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.IsVideoFrameServerEnabled = true;
            player.VideoFrameAvailable += OnVideoFrameAvailable;
        }

        _surface ??= _graphicsDevice.CreateDrawingSurface(
            new Size(1, 1),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        if (_brush == null)
        {
            _brush = _compositor.CreateSurfaceBrush(_surface);
            _brush.Stretch = ResolveStretch(fit);
            _brush.HorizontalAlignmentRatio = 0.5f;
            _brush.VerticalAlignmentRatio = 0.5f;
        }
        else
        {
            _brush.Stretch = ResolveStretch(fit);
        }

        return _brush;
    }

    private void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        _ = args;
        // Raised on the MediaPlayer frame-server worker thread. The target CompositionDrawingSurface
        // has thread affinity to the UI-thread compositor that created it, so the GPU copy and surface
        // draw MUST run on that dispatcher — touching the surface from this worker thread triggers a
        // native cross-thread fail-fast in the composition runtime (0xc000027b). We marshal the render
        // to the owning dispatcher and coalesce: at most one render is queued at a time and it always
        // copies the player's current frame, so playback tracks the source's native frame cadence
        // (rendering the latest decoded frame) without piling up a backlog when the UI thread is busy.
        DispatcherQueue? dispatcher = _dispatcherQueue;
        if (dispatcher == null)
        {
            RenderLatestFrame(sender);
            return;
        }

        if (Interlocked.Exchange(ref _renderScheduled, 1) == 1)
            return;

        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.High, () => RenderLatestFrame(sender)))
            Interlocked.Exchange(ref _renderScheduled, 0);
    }

    private void RenderLatestFrame(MediaPlayer player)
    {
        // Let the next incoming frame schedule a fresh render while this one draws, so we always
        // converge on the most recent decoded frame instead of replaying a stale queued one.
        Interlocked.Exchange(ref _renderScheduled, 0);

        // Any uncaught exception here would be unobserved and silently stop further frames (frozen
        // video), so every GPU operation is guarded. A device-lost (GPU TDR, driver reset, RDP,
        // display topology change) is funneled into Win2D's DeviceLost event for recovery rather
        // than thrown, because the captured _canvasDevice would otherwise stay lost forever.
        try
        {
            lock (_gate)
            {
                if (_disposed || _surface == null)
                    return;

                int width = (int)player.PlaybackSession.NaturalVideoWidth;
                int height = (int)player.PlaybackSession.NaturalVideoHeight;
                if (width <= 0 || height <= 0)
                    return;

                if (_frameTarget == null || _frameWidth != width || _frameHeight != height)
                {
                    _frameTarget?.Dispose();
                    _frameTarget = new CanvasRenderTarget(_canvasDevice, width, height, 96);
                    _frameWidth = width;
                    _frameHeight = height;
                    CanvasComposition.Resize(_surface, new Size(width, height));
                }

                player.CopyFrameToVideoSurface(_frameTarget);
                // The surface is always resized to match the frame before reaching this point,
                // so DrawImage fills the entire surface; no prior clear needed.
                using CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(_surface);
                session.DrawImage(_frameTarget);
                _firstFrame.TrySetResult(null);
            }
        }
        catch (Exception ex)
        {
            HandleFrameException(ex);
        }
    }

    private void HandleFrameException(Exception ex)
    {
        CanvasDevice device;
        lock (_gate)
        {
            if (_disposed)
                return;
            device = _canvasDevice;
        }

        // If the GPU device was lost, ask Win2D to raise DeviceLost so OnDeviceLost can rebuild the
        // device/surface and resume painting. Other (transient) errors are dropped; the next frame
        // callback retries. Either way we never rethrow onto the frame-server thread.
        if (device.IsDeviceLost(ex.HResult))
        {
            try
            {
                device.RaiseDeviceLost();
            }
            catch (Exception)
            {
                // RaiseDeviceLost can itself throw if the device is already being torn down; ignore.
            }
        }
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
    {
        _ = args;
        lock (_gate)
        {
            if (_disposed)
                return;

            sender.DeviceLost -= OnDeviceLost;
            _canvasDevice = CanvasDevice.GetSharedDevice();
            _canvasDevice.DeviceLost += OnDeviceLost;

            // Rebuild the GPU resources on the new device. The surface brush stays valid because the
            // composition graphics device is retargeted in place, so consumers keep their brush.
            CanvasComposition.SetCanvasDevice(_graphicsDevice, _canvasDevice);
            _frameTarget?.Dispose();
            _frameTarget = null;
            _frameWidth = 0;
            _frameHeight = 0;
        }
    }

    /// <summary>
    /// Completes once the first frame has been drawn into the surface, or after the timeout.
    /// Lets callers delay an enter transition until real pixels are present so video does not
    /// appear only after the fade completes on first take-live.
    /// </summary>
    public async Task WaitForFirstFrameAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task firstFrame = _firstFrame.Task;
        if (firstFrame.IsCompleted)
            return;

        Task completed = await Task.WhenAny(firstFrame, Task.Delay(timeout, cancellationToken)).ConfigureAwait(true);
        if (completed != firstFrame)
            cancellationToken.ThrowIfCancellationRequested();
    }

    private void Unsubscribe()
    {
        if (_player != null)
            _player.VideoFrameAvailable -= OnVideoFrameAvailable;
        _player = null;
    }

    private static CompositionStretch ResolveStretch(string? fit) =>
        fit?.ToLowerInvariant() switch
        {
            "contain" or "uniform" => CompositionStretch.Uniform,
            "fill" or "stretch" => CompositionStretch.Fill,
            "none" => CompositionStretch.None,
            _ => CompositionStretch.UniformToFill,
        };

    /// <inheritdoc />
    public void Dispose()
    {
        CanvasRenderTarget? frameTarget;
        CompositionSurfaceBrush? brush;
        CompositionDrawingSurface? surface;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _canvasDevice.DeviceLost -= OnDeviceLost;
            Unsubscribe();
            _firstFrame.TrySetResult(null);

            frameTarget = _frameTarget;
            brush = _brush;
            surface = _surface;
            _frameTarget = null;
            _brush = null;
            _surface = null;
        }

        // Defer disposal of the composition GPU resources to the next Low-priority dispatcher turn.
        // This Dispose runs from LayerSlot.Dispose() inside LayerStackHost.Detach(), which fires during
        // page navigation (within the same NavigateToType that loads the next page). Disposing the
        // brush/surface/graphics device synchronously would race the in-flight compositor frame still
        // referencing them and cause a native fail-fast (RoFailFast 0xC000027B) - matching the deferral
        // already used by GpuDeviceContext.
        void DisposeResources()
        {
            frameTarget?.Dispose();
            brush?.Dispose();
            surface?.Dispose();
            _graphicsDevice.Dispose();
        }

        if (_dispatcherQueue != null)
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, DisposeResources);
        else
            DisposeResources();
    }
}
