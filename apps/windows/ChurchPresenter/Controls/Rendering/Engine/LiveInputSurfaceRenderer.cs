#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;

using Windows.Foundation;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Bridges a local camera (<see cref="MediaCapture"/> + <see cref="MediaFrameReader"/>) into the
/// Microsoft.UI composition tree as a real layer. Each acquired color frame exposes an
/// <c>IDirect3DSurface</c> which is wrapped as a <see cref="CanvasBitmap"/> and blitted into a
/// <see cref="CompositionDrawingSurface"/> exposed as a <see cref="CompositionSurfaceBrush"/>.
/// The brush composites, masks, and fades off the UI thread like any other layer, so a live input
/// behaves identically to the media-file <see cref="VideoSurfaceRenderer"/> path. NDI/SDI sources
/// are intentionally out of scope for v1 (see plan assumptions); this implements local cameras only.
/// </summary>
public sealed class LiveInputSurfaceRenderer : IDisposable
{
    private readonly object _gate = new();
    private readonly Compositor _compositor;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly CanvasDevice _canvasDevice;
    private readonly CompositionGraphicsDevice _graphicsDevice;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceBrush? _brush;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private int _frameWidth;
    private int _frameHeight;
    private bool _disposed;
    private TaskCompletionSource<object?> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initializes a live input surface renderer bound to one compositor.</summary>
    public LiveInputSurfaceRenderer(Compositor compositor)
    {
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _canvasDevice = CanvasDevice.GetSharedDevice();
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
    }

    /// <summary>The frame-source id this renderer is connected to, when connected.</summary>
    public string? ConnectedSourceId { get; private set; }

    /// <summary>Whether the live input reader is currently delivering frames.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Connects to a local camera color frame source and returns a brush painting its frames.
    /// When <paramref name="deviceId"/> is null/empty the first available color source is used.
    /// </summary>
    public async Task<CompositionSurfaceBrush?> ConnectAsync(string? deviceId, string? fit = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisconnectAsync().ConfigureAwait(false);

        MediaFrameSourceGroup? group = await ResolveSourceGroupAsync(deviceId).ConfigureAwait(false);
        if (group == null)
            return null;

        MediaCapture capture = new();
        await capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = group,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Auto,
            SharingMode = MediaCaptureSharingMode.ExclusiveControl,
        }).AsTask(cancellationToken).ConfigureAwait(false);

        MediaFrameSource? source = capture.FrameSources.Values
            .FirstOrDefault(static s => s.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? capture.FrameSources.Values.FirstOrDefault();
        if (source == null)
        {
            capture.Dispose();
            return null;
        }

        MediaFrameReader reader = await capture.CreateFrameReaderAsync(source).AsTask(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed)
            {
                reader.Dispose();
                capture.Dispose();
                return null;
            }

            _capture = capture;
            _reader = reader;
            _firstFrame = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConnectedSourceId = source.Info.Id;

            _surface ??= _graphicsDevice.CreateDrawingSurface(
                new Size(1, 1),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                DirectXAlphaMode.Premultiplied);

            _brush ??= _compositor.CreateSurfaceBrush(_surface);
            _brush.Stretch = ResolveStretch(fit);
            _brush.HorizontalAlignmentRatio = 0.5f;
            _brush.VerticalAlignmentRatio = 0.5f;
        }

        reader.FrameArrived += OnFrameArrived;
        MediaFrameReaderStartStatus status = await reader.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
        IsConnected = status == MediaFrameReaderStartStatus.Success;
        return _brush;
    }

    private static async Task<MediaFrameSourceGroup?> ResolveSourceGroupAsync(string? deviceId)
    {
        IReadOnlyList<MediaFrameSourceGroup> groups = await MediaFrameSourceGroup.FindAllAsync();
        if (groups.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            MediaFrameSourceGroup? match = groups.FirstOrDefault(
                g => string.Equals(g.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return groups.FirstOrDefault(static g =>
            g.SourceInfos.Any(static i => i.SourceKind == MediaFrameSourceKind.Color))
            ?? groups[0];
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        _ = args;
        using MediaFrameReference? reference = sender.TryAcquireLatestFrame();
        Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface? d3dSurface = reference?.VideoMediaFrame?.Direct3DSurface;
        if (d3dSurface == null)
            return;

        lock (_gate)
        {
            if (_disposed || _surface == null)
                return;

            using CanvasBitmap bitmap = CanvasBitmap.CreateFromDirect3D11Surface(_canvasDevice, d3dSurface);
            int width = (int)bitmap.SizeInPixels.Width;
            int height = (int)bitmap.SizeInPixels.Height;
            if (width <= 0 || height <= 0)
                return;

            if (_frameWidth != width || _frameHeight != height)
            {
                _frameWidth = width;
                _frameHeight = height;
                CanvasComposition.Resize(_surface, new Size(width, height));
            }

            using CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(_surface);
            session.Clear(SceneColor.Transparent);
            session.DrawImage(bitmap);
            _firstFrame.TrySetResult(null);
        }
    }

    /// <summary>Completes once the first frame has been drawn into the surface, or after the timeout.</summary>
    public async Task WaitForFirstFrameAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task firstFrame = _firstFrame.Task;
        if (firstFrame.IsCompleted)
            return;

        Task completed = await Task.WhenAny(firstFrame, Task.Delay(timeout, cancellationToken)).ConfigureAwait(true);
        if (completed != firstFrame)
            cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Stops the reader and releases the camera while keeping the surface/brush for reuse.</summary>
    public async Task DisconnectAsync()
    {
        MediaFrameReader? reader;
        MediaCapture? capture;
        lock (_gate)
        {
            reader = _reader;
            capture = _capture;
            _reader = null;
            _capture = null;
            IsConnected = false;
            ConnectedSourceId = null;
        }

        if (reader != null)
        {
            reader.FrameArrived -= OnFrameArrived;
            try
            {
                await reader.StopAsync();
            }
            catch (Exception)
            {
                // Reader teardown races are non-fatal during shutdown/retarget.
            }

            reader.Dispose();
        }

        capture?.Dispose();
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
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _ = DisconnectAsync();

        CompositionSurfaceBrush? brush;
        CompositionDrawingSurface? surface;
        lock (_gate)
        {
            brush = _brush;
            surface = _surface;
            _brush = null;
            _surface = null;
        }

        // Defer disposal of the composition GPU resources to the next Low-priority dispatcher turn so the
        // compositor has committed the frame that released these surfaces before they are torn down. This
        // Dispose runs from LayerSlot.Dispose() during page navigation; synchronous disposal would race the
        // in-flight compositor frame and cause a native fail-fast (RoFailFast 0xC000027B) - matching the
        // deferral already used by GpuDeviceContext.
        void DisposeResources()
        {
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
