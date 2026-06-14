#nullable enable

using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Rendering;

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

using System.Numerics;

using Windows.Foundation;
using Windows.Media.Playback;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Composition-only per-screen output host built on the rendering core. Owns one
/// composition tree with a fixed slot per visual layer in the canonical
/// <see cref="OutputLayerStack"/> z-order, each slot a front/back <see cref="SpriteVisual"/>
/// pair for crossfades. Scenes rasterize via <see cref="SceneSurfaceRenderer"/>, video via
/// <see cref="VideoSurfaceRenderer"/> (MediaPlayer composition surface), still images via
/// Win2D bitmaps, and the mask layer via <see cref="LayerMaskCompositor"/>. Replaces the
/// XAML/Win2D split of the old CompositionScreenHost.
/// </summary>
public sealed class LayerStackHost : IDisposable
{
    private static readonly OutputLayerKind[] ContentLayers = OutputLayerStack.BottomToTop
        .Where(kind => OutputLayerStack.IsVisual(kind) && kind != OutputLayerKind.Mask)
        .ToArray();

    private readonly Dictionary<OutputLayerKind, LayerSlot> _slots = new();
    private FrameworkElement? _hostElement;
    private GpuDeviceContext? _device;
    private SceneSurfaceRenderer? _sceneRenderer;
    private ContainerVisual? _rootVisual;
    private ContainerVisual? _contentRoot;
    private LayerMaskCompositor? _mask;
    private ScreenConfiguration? _configuration;
    private bool _isDetached;
    private Vector2? _lastResolvedSize;
    private RectangleClip? _rootBoundsClip;
    private RectangleClip? _contentBoundsClip;

    /// <summary>Compositor backing this host, when attached.</summary>
    public Compositor? Compositor { get; private set; }

    /// <summary>Whether the host is attached to a XAML element.</summary>
    public bool IsAttached => _rootVisual != null && !_isDetached;

    /// <summary>
    /// True once <see cref="Detach"/> has begun and the slot visuals are (being) disposed. Every
    /// async continuation that resumes on the UI thread must re-check this before touching a slot's
    /// <see cref="CompositionObject"/>, because a closed visual fail-fasts the process (0xC000027B).
    /// </summary>
    private bool IsTornDown => _isDetached || _rootVisual == null;

    /// <summary>Active screen configuration.</summary>
    public ScreenConfiguration? Configuration => _configuration;

    /// <summary>The root composition visual for this screen's canonical render, when attached.</summary>
    public ContainerVisual? RootVisual => _rootVisual;

    /// <summary>
    /// Creates a <see cref="CompositionVisualSurface"/> that captures this host's full canonical render
    /// tree, so audience/stage windows and the operator preview can paint it as a
    /// <see cref="CompositionSurfaceBrush"/> mirror (one compositor per UI thread makes the surface
    /// usable in any window on that thread). The source is this host's persistent root visual, so the
    /// surface always reflects the live render — video, crossfades, and mask included — with no second
    /// render. Returns <c>null</c> until the host is attached.
    /// </summary>
    public CompositionVisualSurface? CreateMirrorSurface()
    {
        if (Compositor == null || _rootVisual == null)
            return null;

        CompositionVisualSurface surface = Compositor.CreateVisualSurface();
        surface.SourceVisual = _rootVisual;
        surface.SourceSize = _rootVisual.Size;
        surface.SourceOffset = Vector2.Zero;
        return surface;
    }

    /// <summary>Attaches the composition tree to a XAML host element.</summary>
    public void Attach(FrameworkElement hostElement)
    {
        ArgumentNullException.ThrowIfNull(hostElement);
        _isDetached = false;

        // Re-attach path: the composition tree was already built and is simply being re-parented onto a
        // (possibly new) host element on the same UI thread. The compositor is per-UI-thread, so visuals
        // created earlier remain valid children of any element on that thread. Re-parenting closes nothing,
        // so it cannot race the shared compositor's render walk (RoFailFast 0xC000027B).
        if (_rootVisual != null)
        {
            _hostElement = hostElement;
            ElementCompositionPreview.SetElementChildVisual(hostElement, _rootVisual);
            return;
        }

        _hostElement = hostElement;
        Compositor = ElementCompositionPreview.GetElementVisual(hostElement).Compositor;
        _device = new GpuDeviceContext(Compositor);
        _device.RenderingDeviceReplaced += OnRenderingDeviceReplaced;
        _sceneRenderer = new SceneSurfaceRenderer(_device);

        Vector2 size = ResolveSize(_configuration?.RenderSize);
        _rootVisual = Compositor.CreateContainerVisual();
        _rootVisual.Size = size;
        _contentRoot = Compositor.CreateContainerVisual();
        _contentRoot.Size = size;
        _rootVisual.Children.InsertAtBottom(_contentRoot);
        _mask = new LayerMaskCompositor(Compositor, _contentRoot, size);
        _rootVisual.Children.InsertAtTop(_mask.MaskedPresentation);
        ApplyFrameBoundsClip();

        ElementCompositionPreview.SetElementChildVisual(hostElement, _rootVisual);

        if (_configuration != null)
            EnsureSurfaces(_configuration);
    }

    /// <summary>Detaches and disposes the composition tree.</summary>
    public void Detach()
    {
        // Signal all in-flight async continuations (CrossfadeAsync, FadeAsync, ClearLayerAsync)
        // to abandon visual access before we dispose the slots.
        _isDetached = true;

        // Unroot the composition tree synchronously so the compositor stops presenting these visuals and
        // IsAttached/IsTornDown immediately report detached.
        if (_hostElement != null)
            ElementCompositionPreview.SetElementChildVisual(_hostElement, null);
        _contentRoot?.Children.RemoveAll();
        _rootVisual?.Children.RemoveAll();

        // Capture the resources, then null all host fields up front so the host is fully detached even
        // before the deferred release runs (a stale Attach must build a fresh tree, not reuse these).
        List<LayerSlot> slots = _slots.Values.ToList();
        _slots.Clear();
        GpuDeviceContext? device = _device;
        if (device != null)
            device.RenderingDeviceReplaced -= OnRenderingDeviceReplaced;

        _mask = null;
        _contentRoot = null;
        _rootVisual = null;
        _device = null;
        _sceneRenderer = null;
        Compositor = null;
        _hostElement = null;

        // Defer the native GPU/surface release until AFTER the compositor commits the unroot above.
        // Releasing slot sprite brushes and disposing the graphics device (which CLOSES their drawing
        // surfaces) in the same compositor frame as the unroot races the in-flight frame and fail-fasts
        // (RoFailFast 0xC000027B) - this is what crashed when navigating away from a Show preview with a
        // live slide. On the next Low-priority turn the unroot is committed and the render thread no longer
        // references these objects, so we can release brushes first, then dispose the device that owns
        // their surfaces.
        void ReleaseResources()
        {
            foreach (LayerSlot slot in slots)
                slot.Dispose();
            device?.Dispose();
        }

        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (dispatcherQueue != null)
            dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ReleaseResources);
        else
            ReleaseResources();

        // NOTE: _isDetached intentionally stays true after Detach. Clearing it here would let an
        // in-flight animation/fade continuation (CompleteCrossfadeAsync, ClearLayerAsync) resume and
        // touch the just-disposed slot visuals, fail-fasting the process. Only Attach re-arms the host.
    }

    /// <summary>Creates or reconciles per-layer slot visuals for one screen.</summary>
    public void EnsureSurfaces(ScreenConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        if (Compositor == null || _contentRoot == null || _rootVisual == null)
            return;

        Vector2 size = ResolveSize(configuration.RenderSize);
        bool sizeChanged = _lastResolvedSize is { } previous && previous != size;
        _lastResolvedSize = size;
        _rootVisual.Size = size;
        _contentRoot.Size = size;
        _mask?.Resize(size);
        ApplyFrameBoundsClip();

        foreach (OutputLayerKind kind in ContentLayers)
        {
            if (_slots.ContainsKey(kind))
            {
                _slots[kind].Resize(size);
                continue;
            }

            LayerSlot slot = LayerSlot.Create(Compositor, kind, size);
            _contentRoot.Children.InsertAtTop(slot.Root);
            _slots[kind] = slot;
        }

        if (sizeChanged)
            ReRasterizeAllLayers();
    }

    /// <summary>Re-draws every layer that still has cached content after the render size changed.</summary>
    private void ReRasterizeAllLayers()
    {
        if (IsTornDown)
            return;

        foreach (LayerSlot slot in _slots.Values)
        {
            if (slot.LastContent is { } content)
                ReapplyContent(slot.Kind, content);
        }
    }

    /// <summary>Updates the capture size on an existing mirror surface after the root visual resizes.</summary>
    public void SynchronizeMirrorSurface(CompositionVisualSurface? surface)
    {
        if (surface == null || _rootVisual == null)
            return;

        surface.SourceSize = _rootVisual.Size;
    }

    /// <summary>Renders a scene into a layer slot and transitions it in.</summary>
    public Task ApplySceneLayerAsync(
        OutputLayerKind kind,
        SlideScene scene,
        LayerTransitionState transition,
        ISceneImageSource? images,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        cancellationToken.ThrowIfCancellationRequested();
        if (_sceneRenderer == null || Compositor == null || !_slots.TryGetValue(kind, out LayerSlot? slot))
            return Task.CompletedTask;

        slot.SetVideo(null);
        slot.SetLiveInput(null);
        slot.LastContent = new SlotContent(scene, images, null, null);
        CompositionSurfaceBrush brush = _sceneRenderer.RenderSceneToBrush(
            scene,
            slot.Inactive,
            slot.Root.Size,
            images,
            SceneDrawOptions.Output);
        slot.Inactive.Brush = brush;
        return CrossfadeAsync(slot, transition, cancellationToken);
    }

    /// <summary>Binds a media player surface into a layer slot and transitions it in.</summary>
    /// <param name="kind">Layer slot to bind the video surface into.</param>
    /// <param name="player">Media player whose frame-server output is composited.</param>
    /// <param name="fit">How the video frame is fitted within the slot.</param>
    /// <param name="transition">Enter transition applied once the first frame is ready.</param>
    /// <param name="startPlayback">
    /// Invoked after the frame-server surface is wired but before the enter transition, so
    /// playback can begin and produce a first frame while we wait, preventing an empty fade-in.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyVideoLayerAsync(
        OutputLayerKind kind,
        MediaPlayer player,
        string? fit,
        LayerTransitionState transition,
        Func<CancellationToken, Task>? startPlayback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        cancellationToken.ThrowIfCancellationRequested();
        if (Compositor == null || !_slots.TryGetValue(kind, out LayerSlot? slot))
            return;

        slot.SetLiveInput(null);
        VideoSurfaceRenderer video = slot.EnsureVideo(Compositor);
        CompositionSurfaceBrush? brush = video.CreateBrush(player, fit);
        if (brush == null)
            return;

        slot.LastContent = new SlotContent(null, null, null, player) { Fit = fit };
        slot.Inactive.Brush = brush;

        if (startPlayback != null)
            await startPlayback(cancellationToken).ConfigureAwait(true);

        await video.WaitForFirstFrameAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(true);

        // Guard: the host can be detached (and the slot disposed) while we awaited playback/first-frame.
        if (IsTornDown)
            return;

        await CrossfadeAsync(slot, transition, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Connects a live camera input into a layer slot and transitions it in.</summary>
    /// <param name="kind">Layer slot to bind the live input into (normally LiveVideo).</param>
    /// <param name="sourceId">Live-input source id; null selects the first available color camera.</param>
    /// <param name="fit">How the live frame is fitted within the slot.</param>
    /// <param name="transition">Enter transition applied once the first frame is ready.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ApplyLiveVideoLayerAsync(
        OutputLayerKind kind,
        string? sourceId,
        string? fit,
        LayerTransitionState transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Compositor == null || !_slots.TryGetValue(kind, out LayerSlot? slot))
            return;

        LiveInputSurfaceRenderer live = slot.EnsureLiveInput(Compositor);
        CompositionSurfaceBrush? brush = await live.ConnectAsync(sourceId, fit, cancellationToken).ConfigureAwait(true);
        if (brush == null)
            return;

        // Guard: the host can be detached (and the slot disposed) while we awaited the camera connect.
        if (IsTornDown)
            return;

        slot.SetVideo(null);
        slot.LastContent = new SlotContent(null, null, null, null) { Fit = fit, LiveSourceId = sourceId };
        slot.Inactive.Brush = brush;

        await live.WaitForFirstFrameAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(true);

        if (IsTornDown)
            return;

        await CrossfadeAsync(slot, transition, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Loads a still image into a layer slot and transitions it in.</summary>
    public async Task ApplyImageLayerAsync(
        OutputLayerKind kind,
        string sourcePath,
        string? fit,
        LayerTransitionState transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        if (_device == null || Compositor == null || !_slots.TryGetValue(kind, out LayerSlot? slot))
            return;

        CompositionSurfaceBrush? brush = await CreateImageBrushAsync(sourcePath, fit, slot.Root.Size, cancellationToken)
            .ConfigureAwait(true);
        if (brush == null)
            return;

        // Guard: the host can be detached (and the slot disposed) while we awaited the bitmap load.
        if (IsTornDown)
            return;

        slot.SetVideo(null);
        slot.SetLiveInput(null);
        slot.LastContent = new SlotContent(null, null, sourcePath, null) { Fit = fit };
        slot.Inactive.Brush = brush;
        await CrossfadeAsync(slot, transition, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Clears a layer slot, optionally fading it out first.</summary>
    public async Task ClearLayerAsync(
        OutputLayerKind kind,
        LayerTransitionState transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == OutputLayerKind.Mask)
        {
            _mask?.ApplyMask(null);
            return;
        }

        if (Compositor == null || !_slots.TryGetValue(kind, out LayerSlot? slot))
            return;

        if (IsAnimated(transition))
            await FadeAsync(slot.Active, 0f, transition.Duration, cancellationToken).ConfigureAwait(true);

        // Guard: if the host was detached while the fade ran the slot visuals are disposed.
        if (IsTornDown)
            return;

        slot.Active.Opacity = 0f;
        slot.Inactive.Opacity = 0f;
        slot.Active.Brush = null;
        slot.Inactive.Brush = null;
        slot.SetVideo(null);
        slot.SetLiveInput(null);
        slot.LastContent = null;
    }

    /// <summary>Applies (or clears, when null) the mask layer from a compiled alpha scene.</summary>
    public void ApplyMaskScene(SlideScene? maskScene)
    {
        if (_mask == null || _sceneRenderer == null || Compositor == null)
            return;

        if (maskScene == null)
        {
            _mask.ApplyMask(null);
            return;
        }

        Vector2 size = _rootVisual?.Size ?? ResolveSize(_configuration?.RenderSize);
        var surfaceSize = new PixelSize(Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y));
        (SlideScene fittedMask, UniformFitMetrics fit) = SlideSceneFitter.Uniform(maskScene, surfaceSize);
        CompositionDrawingSurface surface = _device!.CreateDrawingSurface(
            new Size(surfaceSize.Width, surfaceSize.Height));
        using (CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(surface))
        {
            if (fit.IsFullBleed)
                SceneSurfaceRenderer.Draw(session, fittedMask, options: SceneDrawOptions.Output);
            else
            {
                session.Clear(SceneColor.Transparent);
                Matrix3x2 previous = session.Transform;
                session.Transform = Matrix3x2.CreateTranslation((float)fit.OffsetX, (float)fit.OffsetY) * previous;
                try
                {
                    SceneSurfaceRenderer.Draw(session, fittedMask, options: SceneDrawOptions.Output);
                }
                finally
                {
                    session.Transform = previous;
                }
            }
        }

        CompositionSurfaceBrush maskBrush = Compositor.CreateSurfaceBrush(surface);
        maskBrush.Stretch = CompositionStretch.Fill;
        _mask.ApplyMask(maskBrush);
    }

    private async Task<CompositionSurfaceBrush?> CreateImageBrushAsync(
        string sourcePath,
        string? fit,
        Vector2 size,
        CancellationToken cancellationToken)
    {
        if (_device == null || Compositor == null)
            return null;

        CanvasBitmap bitmap = await CanvasBitmap.LoadAsync(_device.CanvasDevice, sourcePath).AsTask(cancellationToken)
            .ConfigureAwait(true);
        using (bitmap)
        {
            CompositionDrawingSurface surface = _device.CreateDrawingSurface(
                new Size(bitmap.Size.Width, bitmap.Size.Height));
            using (CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(surface))
            {
                session.Clear(SceneColor.Transparent);
                session.DrawImage(bitmap);
            }

            CompositionSurfaceBrush brush = Compositor.CreateSurfaceBrush(surface);
            brush.Stretch = ResolveStretch(fit);
            brush.HorizontalAlignmentRatio = 0.5f;
            brush.VerticalAlignmentRatio = 0.5f;
            _ = size;
            return brush;
        }
    }

    private Task CrossfadeAsync(LayerSlot slot, LayerTransitionState transition, CancellationToken cancellationToken)
    {
        if (Compositor == null || IsTornDown)
            return Task.CompletedTask;

        if (!IsAnimated(transition))
        {
            if (IsTornDown)
                return Task.CompletedTask;

            slot.Active.Opacity = 0f;
            slot.Inactive.Clip = null;
            slot.Inactive.Offset = Vector3.Zero;
            slot.Inactive.Scale = Vector3.One;
            slot.Inactive.Opacity = 1f;
            slot.SwapFrontBack();
            return Task.CompletedTask;
        }

        Task animation = LayerTransitionAnimator.AnimateAsync(
            Compositor, slot.Inactive, slot.Active, slot.Root.Size, transition);
        return CompleteCrossfadeAsync(this, slot, animation);
    }

    private static async Task CompleteCrossfadeAsync(LayerStackHost host, LayerSlot slot, Task animation)
    {
        await animation.ConfigureAwait(true);
        // Guard: if the host was detached while the animation ran the slot visuals are disposed.
        if (host.IsTornDown)
            return;
        slot.Active.Opacity = 0f;
        slot.SwapFrontBack();
    }

    private Task FadeAsync(Visual visual, float toOpacity, TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Compositor == null || duration <= TimeSpan.Zero)
        {
            visual.Opacity = toOpacity;
            return Task.CompletedTask;
        }

        float seconds = (float)Math.Max(0.001, duration.TotalSeconds);
        TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CompositionScopedBatch batch = Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => completion.TrySetResult(null);
        visual.StartAnimation("Opacity", CreateFade(Compositor, toOpacity, seconds));
        batch.End();

        // The Completed event can fail to fire when the visual is detached or the compositor is
        // paused (hidden output window / suspended app); fall back to a timeout so a clear/fade can
        // never permanently stall the apply pipeline and freeze the UI.
        return CompleteFadeWithTimeoutAsync(completion.Task, duration + TimeSpan.FromSeconds(2));
    }

    private static async Task CompleteFadeWithTimeoutAsync(Task completion, TimeSpan timeout)
    {
        if (completion.IsCompleted)
        {
            await completion.ConfigureAwait(true);
            return;
        }

        Task winner = await Task.WhenAny(completion, Task.Delay(timeout)).ConfigureAwait(true);
        if (ReferenceEquals(winner, completion))
            await completion.ConfigureAwait(true);
    }

    private static ScalarKeyFrameAnimation CreateFade(Compositor compositor, float to, float seconds)
    {
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to);
        animation.Duration = TimeSpan.FromSeconds(seconds);
        return animation;
    }

    private static bool IsAnimated(LayerTransitionState transition) =>
        transition.Duration > TimeSpan.Zero
        && transition.Phase != LayerTransitionPhase.None
        && !string.Equals(transition.Type, "cut", StringComparison.OrdinalIgnoreCase);

    private static CompositionStretch ResolveStretch(string? fit) =>
        fit?.ToLowerInvariant() switch
        {
            "contain" or "uniform" => CompositionStretch.Uniform,
            "fill" or "stretch" => CompositionStretch.Fill,
            "none" => CompositionStretch.None,
            _ => CompositionStretch.UniformToFill,
        };

    private void OnRenderingDeviceReplaced(object? sender, EventArgs e)
    {
        FrameworkElement? hostElement = _hostElement;
        if (hostElement == null || IsTornDown)
            return;

        _ = hostElement.DispatcherQueue.TryEnqueue(() =>
        {
            // The device-replaced event can be raised on a background thread and the re-enqueued work
            // can run after Detach; re-check before re-rasterizing into the (now disposed) slots.
            if (IsTornDown)
                return;

            foreach (LayerSlot slot in _slots.Values)
            {
                if (slot.LastContent is { } content)
                    ReapplyContent(slot.Kind, content);
            }
        });
    }

    private void ReapplyContent(OutputLayerKind kind, SlotContent content)
    {
        LayerTransitionState cut = LayerTransitionState.None;
        if (content.Scene != null)
            _ = ApplySceneLayerAsync(kind, content.Scene, cut, content.Images);
        else if (content.Player != null)
            _ = ApplyVideoLayerAsync(kind, content.Player, content.Fit, cut);
        else if (content.ImagePath != null)
            _ = ApplyImageLayerAsync(kind, content.ImagePath, content.Fit, cut);
        else if (content.LiveSourceId != null)
            _ = ApplyLiveVideoLayerAsync(kind, content.LiveSourceId, content.Fit, cut);
    }

    /// <inheritdoc />
    public void Dispose() => Detach();

    private void ApplyFrameBoundsClip()
    {
        if (Compositor == null || _rootVisual == null || _contentRoot == null)
            return;

        float width = _rootVisual.Size.X;
        float height = _rootVisual.Size.Y;
        if (width <= 0 || height <= 0)
            return;

        RectangleClip rootClip = _rootBoundsClip ??= Compositor.CreateRectangleClip();
        rootClip.Left = 0;
        rootClip.Top = 0;
        rootClip.Right = width;
        rootClip.Bottom = height;
        _rootVisual.Clip = rootClip;

        RectangleClip contentClip = _contentBoundsClip ??= Compositor.CreateRectangleClip();
        contentClip.Left = 0;
        contentClip.Top = 0;
        contentClip.Right = width;
        contentClip.Bottom = height;
        _contentRoot.Clip = contentClip;
    }

    private static Vector2 ResolveSize(PixelSize? renderSize) =>
        renderSize == null
            ? new Vector2(1920, 1080)
            : new Vector2(Math.Max(1, renderSize.Width), Math.Max(1, renderSize.Height));

    private sealed record SlotContent(SlideScene? Scene, ISceneImageSource? Images, string? ImagePath, MediaPlayer? Player)
    {
        public string? Fit { get; init; }

        public string? LiveSourceId { get; init; }
    }

    private sealed class LayerSlot : IDisposable
    {
        private bool _frontActive = true;

        private LayerSlot(OutputLayerKind kind, ContainerVisual root, SpriteVisual front, SpriteVisual back)
        {
            Kind = kind;
            Root = root;
            Front = front;
            Back = back;
        }

        public OutputLayerKind Kind { get; }

        public ContainerVisual Root { get; }

        public SpriteVisual Front { get; }

        public SpriteVisual Back { get; }

        public SpriteVisual Active => _frontActive ? Front : Back;

        public SpriteVisual Inactive => _frontActive ? Back : Front;

        public VideoSurfaceRenderer? Video { get; private set; }

        public LiveInputSurfaceRenderer? LiveInput { get; private set; }

        public SlotContent? LastContent { get; set; }

        public static LayerSlot Create(Compositor compositor, OutputLayerKind kind, Vector2 size)
        {
            ContainerVisual root = compositor.CreateContainerVisual();
            root.Size = size;
            SpriteVisual front = compositor.CreateSpriteVisual();
            front.Size = size;
            front.Opacity = 1f;
            SpriteVisual back = compositor.CreateSpriteVisual();
            back.Size = size;
            back.Opacity = 0f;
            root.Children.InsertAtBottom(back);
            root.Children.InsertAtTop(front);
            return new LayerSlot(kind, root, front, back);
        }

        public void Resize(Vector2 size)
        {
            Root.Size = size;
            Front.Size = size;
            Back.Size = size;
        }

        public VideoSurfaceRenderer EnsureVideo(Compositor compositor) => Video ??= new VideoSurfaceRenderer(compositor);

        public LiveInputSurfaceRenderer EnsureLiveInput(Compositor compositor) =>
            LiveInput ??= new LiveInputSurfaceRenderer(compositor);

        public void SetVideo(VideoSurfaceRenderer? video)
        {
            if (ReferenceEquals(Video, video))
                return;

            Video?.Dispose();
            Video = video;
        }

        public void SetLiveInput(LiveInputSurfaceRenderer? liveInput)
        {
            if (ReferenceEquals(LiveInput, liveInput))
                return;

            LiveInput?.Dispose();
            LiveInput = liveInput;
        }

        public void SwapFrontBack() => _frontActive = !_frontActive;

        public void Dispose()
        {
            // IMPORTANT: this runs from LayerStackHost.Detach's DEFERRED release, on a Low-priority turn
            // AFTER the host's visual tree has been unrooted and that unroot committed by the compositor.
            // At that point the render thread no longer references these visuals, so it is safe to stop
            // animations and release the surface brush references. Releasing the brushes here (before the
            // owning graphics device is disposed) prevents a live brush from referencing a closed drawing
            // surface, which is what fail-fasts (RoFailFast / 0xC000027B). Do NOT call this while the host
            // is still attached/rooted - that synchronous brush release is what the older code crashed on.
            StopAnimations(Front);
            StopAnimations(Back);
            Front.Brush = null;
            Back.Brush = null;

            Video?.Dispose();
            LiveInput?.Dispose();
        }

        private static void StopAnimations(SpriteVisual visual)
        {
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Offset");
            visual.StopAnimation("Scale");
        }
    }
}
