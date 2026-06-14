#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;

using Windows.Foundation;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Shared GPU device context for the rendering core. Wraps the process-wide shared
/// <see cref="CanvasDevice"/> and a per-<see cref="Compositor"/>
/// <see cref="CompositionGraphicsDevice"/>, and recovers transparently from device-lost.
/// Surface engines subscribe to <see cref="RenderingDeviceReplaced"/> to re-rasterize
/// their content after the underlying GPU device is recreated.
/// </summary>
public sealed class GpuDeviceContext : IDisposable
{
    private readonly object _gate = new();
    private readonly Compositor _compositor;
    private CanvasDevice _canvasDevice;
    private CompositionGraphicsDevice _graphicsDevice;
    private bool _disposed;

    /// <summary>Initializes a device context bound to a compositor.</summary>
    public GpuDeviceContext(Compositor compositor)
    {
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _canvasDevice = CanvasDevice.GetSharedDevice();
        _canvasDevice.DeviceLost += OnCanvasDeviceLost;
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
        _graphicsDevice.RenderingDeviceReplaced += OnRenderingDeviceReplaced;
    }

    /// <summary>
    /// Raised after the rendering device has been replaced (device-lost recovery).
    /// Consumers must re-create and re-draw any cached drawing surfaces.
    /// </summary>
    public event EventHandler? RenderingDeviceReplaced;

    /// <summary>The compositor this context renders into.</summary>
    public Compositor Compositor => _compositor;

    /// <summary>The current shared Win2D canvas device.</summary>
    public CanvasDevice CanvasDevice
    {
        get
        {
            lock (_gate)
                return _canvasDevice;
        }
    }

    /// <summary>The current composition graphics device.</summary>
    public CompositionGraphicsDevice GraphicsDevice
    {
        get
        {
            lock (_gate)
                return _graphicsDevice;
        }
    }

    /// <summary>Creates a premultiplied-BGRA drawing surface sized for one layer.</summary>
    public CompositionDrawingSurface CreateDrawingSurface(Size size)
    {
        double width = Math.Max(1d, size.Width);
        double height = Math.Max(1d, size.Height);
        lock (_gate)
        {
            return _graphicsDevice.CreateDrawingSurface(
                new Size(width, height),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                DirectXAlphaMode.Premultiplied);
        }
    }

    private void OnCanvasDeviceLost(CanvasDevice sender, object args)
    {
        _ = args;
        lock (_gate)
        {
            if (_disposed)
                return;

            sender.DeviceLost -= OnCanvasDeviceLost;
            _canvasDevice = CanvasDevice.GetSharedDevice();
            _canvasDevice.DeviceLost += OnCanvasDeviceLost;
            CanvasComposition.SetCanvasDevice(_graphicsDevice, _canvasDevice);
        }

        RenderingDeviceReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void OnRenderingDeviceReplaced(CompositionGraphicsDevice sender, RenderingDeviceReplacedEventArgs args)
    {
        _ = sender;
        _ = args;
        RenderingDeviceReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CompositionGraphicsDevice? toDispose;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _canvasDevice.DeviceLost -= OnCanvasDeviceLost;
            _graphicsDevice.RenderingDeviceReplaced -= OnRenderingDeviceReplaced;
            toDispose = _graphicsDevice;
        }

        // Defer disposal of the CompositionGraphicsDevice to the next Low-priority dispatcher
        // turn. By then the compositor will have committed the current frame (which processed
        // the SetElementChildVisual(null) / Children.RemoveAll() tree changes that unrooted
        // our visuals), so the render thread is no longer referencing surfaces from this device.
        // Disposing synchronously inside Detach() (which fires during ProgramOutputSurface
        // Unloaded, within the same NavigateToType call that also loads another page) would race
        // with the in-flight compositor frame and cause a native fail-fast (0xC000027B).
        //
        // IMPORTANT: enqueue a managed lambda, not the projected method group `toDispose.Dispose`.
        // CompositionGraphicsDevice is a WinRT object; passing its Dispose method group directly as a
        // DispatcherQueueHandler makes CsWinRT try to marshal the WinRT target as the delegate and throws
        // InvalidCastException, which would abort the whole Detach() and leave the host half-torn-down.
        DispatcherQueue? dq = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        bool scheduled = dq != null && dq.TryEnqueue(DispatcherQueuePriority.Low, () => toDispose.Dispose());

        // If the disposal could not be scheduled (no dispatcher / queue shutting down), leave the device for
        // GC finalization rather than disposing synchronously here, which could race the in-flight frame.
        _ = scheduled;
    }
}
