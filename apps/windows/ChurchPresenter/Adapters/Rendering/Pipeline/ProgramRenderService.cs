#nullable enable

using System.Numerics;

using ChurchPresenter.Adapters.Media.Pipeline;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls.Rendering.Engine;

using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Adapters.Rendering.Pipeline;

/// <summary>
/// The single canonical-render host for every logical output screen. Implements both the audience and
/// stage host-adapter contracts: the unified pipeline applies each screen's frame here exactly once, and
/// every consumer (audience window, stage window, operator preview) is a passive
/// <see cref="CompositionVisualSurface"/> mirror obtained via <see cref="GetScreenSurface"/>. This
/// supersedes the former per-target render hosts (audience/stage/preview adapters), eliminating the
/// duplicate preview render and the page-owned composition tree whose teardown raced the shared
/// compositor (RoFailFast 0xC000027B).
/// </summary>
public sealed class ProgramRenderService : IProgramRenderService, IAudienceHostAdapter, IStageHostAdapter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ScreenBinding> _screens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScreenBinding> _previewScreens = new(StringComparer.OrdinalIgnoreCase);
    private readonly WinUiMediaSlotRegistry _mediaSlots;

    private Panel? _anchor;
    private DispatcherQueue? _dispatcher;

    /// <summary>Initializes the render service with the shared media slot registry.</summary>
    public ProgramRenderService(WinUiMediaSlotRegistry mediaSlots)
    {
        _mediaSlots = mediaSlots ?? throw new ArgumentNullException(nameof(mediaSlots));
    }

    /// <inheritdoc />
    public event EventHandler? ScreensChanged;

    /// <inheritdoc />
    public event EventHandler<HostHealthEvent>? HostHealthRaised;

    /// <inheritdoc />
    public void Initialize(Panel anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        _anchor = anchor;
        _dispatcher = anchor.DispatcherQueue ?? App.MainWindow?.DispatcherQueue;
    }

    /// <inheritdoc />
    public CompositionVisualSurface? GetScreenSurface(string screenId)
    {
        if (string.IsNullOrWhiteSpace(screenId))
            return null;

        lock (_gate)
            return _screens.TryGetValue(screenId, out ScreenBinding? binding) ? binding.Surface : null;
    }

    /// <inheritdoc />
    public void EnsurePreviewSurface(string screenId, PixelSize previewSize)
    {
        if (string.IsNullOrWhiteSpace(screenId) || previewSize.Width <= 0 || previewSize.Height <= 0)
            return;

        if (_anchor is null || _dispatcher is null)
            return;

        _ = RunOnDispatcherAsync(_dispatcher, () =>
        {
            ScreenBinding? canonical = Resolve(screenId);
            bool isStage = canonical?.Configuration?.IsStage ?? string.Equals(screenId, "stage", StringComparison.OrdinalIgnoreCase);
            var configuration = new ScreenConfiguration
            {
                ScreenId = screenId,
                RenderSize = previewSize,
                IsStage = isStage,
            };
            EnsurePreviewScreenHost(configuration);
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    /// <inheritdoc />
    public CompositionVisualSurface? GetPreviewSurface(string screenId)
    {
        if (string.IsNullOrWhiteSpace(screenId))
            return null;

        lock (_gate)
            return _previewScreens.TryGetValue(screenId, out ScreenBinding? binding) ? binding.Surface : null;
    }

    /// <inheritdoc />
    public void ReleasePreviewSurface(string screenId)
    {
        if (string.IsNullOrWhiteSpace(screenId))
            return;

        ScreenBinding? binding;
        lock (_gate)
        {
            if (!_previewScreens.Remove(screenId, out binding))
                return;
        }

        if (_dispatcher is null)
            return;

        _ = RunOnDispatcherAsync(_dispatcher, async () =>
        {
            if (binding.Element is not null && _anchor is not null)
                _anchor.Children.Remove(binding.Element);
            binding.Host.Detach();
            await Task.CompletedTask.ConfigureAwait(true);
        }, CancellationToken.None);
    }

    /// <inheritdoc />
    public Task ApplyFrameAsync(AudienceRenderFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ScreenBinding? binding = Resolve(frame.ScreenId);
        if (binding is null || !binding.Host.IsAttached)
            return Task.CompletedTask;

        return RunOnHostAsync(binding, async () =>
        {
            if (!binding.Host.IsAttached)
                return;

            HashSet<OutputLayerKind> present = [];
            foreach (RenderLayerDescriptor layer in frame.Layers)
            {
                if (layer.Kind == OutputLayerKind.Audio)
                    continue;
                present.Add(layer.Kind);
                await ApplyLayerSafeAsync(binding, frame.ScreenId, layer, cancellationToken).ConfigureAwait(true);
            }

            await ClearAbsentLayersAsync(binding, present, cancellationToken).ConfigureAwait(true);
            RaiseHealth(HostHealthSeverity.Healthy, $"Applied frame to {frame.ScreenId}.");
            await ApplyAudiencePreviewAsync(frame, present, cancellationToken).ConfigureAwait(true);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ApplyFrameAsync(StageRenderFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ScreenBinding? binding = Resolve(frame.ScreenId);
        if (binding is null || !binding.Host.IsAttached)
            return Task.CompletedTask;

        return RunOnHostAsync(binding, async () =>
        {
            if (!binding.Host.IsAttached)
                return;

            HashSet<OutputLayerKind> present = [];
            foreach (RenderPayloadDescriptor payload in frame.Payloads)
            {
                OutputLayerKind kind = MapPayloadKind(payload);
                if (kind == OutputLayerKind.Audio)
                    continue;
                present.Add(kind);
                var layer = new RenderLayerDescriptor { Kind = kind, Payload = payload, IsVisible = true };
                await ApplyLayerSafeAsync(binding, frame.ScreenId, layer, cancellationToken).ConfigureAwait(true);
            }

            await ClearAbsentLayersAsync(binding, present, cancellationToken).ConfigureAwait(true);
            RaiseHealth(HostHealthSeverity.Healthy, $"Applied stage frame to {frame.ScreenId}.");
            await ApplyStagePreviewAsync(frame, present, cancellationToken).ConfigureAwait(true);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task EnsureSurfacesAsync(ScreenConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        DispatcherQueue? dispatcher = _dispatcher;
        if (_anchor is null || dispatcher is null)
            return Task.CompletedTask;

        return RunOnDispatcherAsync(dispatcher, () =>
        {
            EnsureScreenHost(configuration);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScreenBinding[] bindings;
        ScreenBinding[] previewBindings;
        lock (_gate)
        {
            bindings = _screens.Values.ToArray();
            previewBindings = _previewScreens.Values.ToArray();
            _screens.Clear();
            _previewScreens.Clear();
        }

        foreach (ScreenBinding binding in bindings.Concat(previewBindings))
        {
            await RunOnHostAsync(binding, () =>
            {
                if (binding.Element is not null && _anchor is not null)
                    _anchor.Children.Remove(binding.Element);
                binding.Host.Detach();
                return Task.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
        }

        RaiseScreensChanged();
        RaiseHealth(HostHealthSeverity.Healthy, string.IsNullOrWhiteSpace(reason) ? "Render service shutdown." : reason);
    }

    /// <inheritdoc />
    public async Task<IMediaSlotBinding?> BindMediaSlotAsync(
        LayerSlotId slotId,
        RenderPayloadDescriptor payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payload.Detail is not MediaRenderPayload media
            || string.IsNullOrWhiteSpace(media.Media.ResolvedSourcePath))
        {
            return null;
        }

        WinUiMediaSlotAdapter binding = _mediaSlots.GetOrCreate(
            slotId, payload.Id, media.Media.ResolvedSourcePath, media.Media.Loop);

        double volume = media.Media.Muted ? 0 : 1;
        if (IsVideoMedia(media.Media))
        {
            await binding.PreloadAsync(
                media.Media.ResolvedSourcePath,
                media.Media.Loop,
                volume,
                VideoPrebindReadyTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await binding.BindAsync(
                media.Media.ResolvedSourcePath,
                loop: media.Media.Loop,
                volume: volume,
                startPlayback: true,
                cancellationToken).ConfigureAwait(false);
        }

        return binding;
    }

    // ── Screen host lifecycle ─────────────────────────────────────────────────

    private void EnsureScreenHost(ScreenConfiguration configuration)
    {
        if (_anchor is null)
            return;

        ScreenBinding? existing = Resolve(configuration.ScreenId);
        if (existing is not null)
        {
            PixelSize previousSize = existing.Configuration?.RenderSize ?? PixelSize.FullHd;
            existing.Configuration = configuration;
            if (!previousSize.Equals(configuration.RenderSize))
                existing.ClearLayerCache();
            existing.Host.EnsureSurfaces(configuration);
            existing.Host.SynchronizeMirrorSurface(existing.Surface);
            return;
        }

        // The per-screen host element is a full-size, always-laid-out child of the (opacity-0) anchor.
        // It is present and composing so the canonical render ticks continuously; a collapsed/0x0/
        // off-screen element would not render and the mirror surface would be blank.
        var element = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _anchor.Children.Add(element);

        var host = new LayerStackHost();
        host.EnsureSurfaces(configuration);
        host.Attach(element);
        host.EnsureSurfaces(configuration);
        CompositionVisualSurface? surface = host.CreateMirrorSurface();

        DispatcherQueue dispatcher = _dispatcher ?? element.DispatcherQueue;
        var binding = new ScreenBinding(configuration.ScreenId, host, dispatcher)
        {
            Configuration = configuration,
            Surface = surface,
            Element = element,
        };

        lock (_gate)
            _screens[configuration.ScreenId] = binding;

        RaiseScreensChanged();
    }

    private void EnsurePreviewScreenHost(ScreenConfiguration configuration)
    {
        if (_anchor is null)
            return;

        lock (_gate)
        {
            if (_previewScreens.TryGetValue(configuration.ScreenId, out ScreenBinding? existing))
            {
                PixelSize previousSize = existing.Configuration?.RenderSize ?? PixelSize.FullHd;
                existing.Configuration = configuration;
                if (!previousSize.Equals(configuration.RenderSize))
                    existing.ClearLayerCache();
                existing.Host.EnsureSurfaces(configuration);
                existing.Host.SynchronizeMirrorSurface(existing.Surface);
                return;
            }
        }

        var element = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _anchor.Children.Add(element);

        var host = new LayerStackHost();
        host.EnsureSurfaces(configuration);
        host.Attach(element);
        host.EnsureSurfaces(configuration);
        CompositionVisualSurface? surface = host.CreateMirrorSurface();

        DispatcherQueue dispatcher = _dispatcher ?? element.DispatcherQueue;
        var binding = new ScreenBinding(configuration.ScreenId, host, dispatcher)
        {
            Configuration = configuration,
            Surface = surface,
            Element = element,
            IsPreview = true,
        };

        lock (_gate)
            _previewScreens[configuration.ScreenId] = binding;
    }

    private async Task ApplyAudiencePreviewAsync(
        AudienceRenderFrame frame,
        HashSet<OutputLayerKind> present,
        CancellationToken cancellationToken)
    {
        ScreenBinding? preview = ResolvePreview(frame.ScreenId);
        if (preview is null || !preview.Host.IsAttached)
            return;

        await RunOnHostAsync(preview, async () =>
        {
            foreach (RenderLayerDescriptor layer in frame.Layers)
            {
                if (layer.Kind == OutputLayerKind.Audio)
                    continue;
                await ApplyLayerSafeAsync(preview, frame.ScreenId, layer, cancellationToken).ConfigureAwait(true);
            }

            await ClearAbsentLayersAsync(preview, present, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyStagePreviewAsync(
        StageRenderFrame frame,
        HashSet<OutputLayerKind> present,
        CancellationToken cancellationToken)
    {
        ScreenBinding? preview = ResolvePreview(frame.ScreenId);
        if (preview is null || !preview.Host.IsAttached)
            return;

        await RunOnHostAsync(preview, async () =>
        {
            foreach (RenderPayloadDescriptor payload in frame.Payloads)
            {
                OutputLayerKind kind = MapPayloadKind(payload);
                if (kind == OutputLayerKind.Audio)
                    continue;
                var layer = new RenderLayerDescriptor { Kind = kind, Payload = payload, IsVisible = true };
                await ApplyLayerSafeAsync(preview, frame.ScreenId, layer, cancellationToken).ConfigureAwait(true);
            }

            await ClearAbsentLayersAsync(preview, present, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(false);
    }

    private ScreenBinding? ResolvePreview(string screenId)
    {
        lock (_gate)
            return _previewScreens.TryGetValue(screenId, out ScreenBinding? binding) ? binding : null;
    }

    private void RaiseScreensChanged() => ScreensChanged?.Invoke(this, EventArgs.Empty);

    // ── Layer apply (shared by audience and stage frames) ─────────────────────

    private async Task ClearAbsentLayersAsync(
        ScreenBinding binding,
        HashSet<OutputLayerKind> present,
        CancellationToken cancellationToken)
    {
        if (!binding.Host.IsAttached)
            return;

        foreach (OutputLayerKind kind in binding.AppliedLayers.Where(k => !present.Contains(k)).ToArray())
        {
            await binding.Host.ClearLayerAsync(kind, LayerTransitionState.None, cancellationToken).ConfigureAwait(true);
            binding.AppliedLayers.Remove(kind);
            binding.LastPayloadKeys.Remove(kind);
        }
    }

    private async Task ApplyLayerSafeAsync(
        ScreenBinding binding,
        string screenId,
        RenderLayerDescriptor layer,
        CancellationToken cancellationToken)
    {
        try
        {
            await ApplyLayerAsync(binding, screenId, layer, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            binding.AppliedLayers.Remove(layer.Kind);
            binding.LastPayloadKeys.Remove(layer.Kind);
            RaiseHealth(HostHealthSeverity.Degraded, $"Failed applying {layer.Kind} layer to {screenId}: {ex.Message}");
        }
    }

    private async Task ApplyLayerAsync(
        ScreenBinding binding,
        string screenId,
        RenderLayerDescriptor layer,
        CancellationToken cancellationToken)
    {
        if (layer.Kind == OutputLayerKind.Mask)
        {
            binding.Host.ApplyMaskScene(
                layer.IsVisible && !layer.IsSuppressed && TryResolveScene(layer.Payload.Detail, out SlideScene maskScene)
                    ? maskScene
                    : null);
            return;
        }

        bool cleared = layer.ClearState != LayerClearState.None || layer.IsSuppressed || !layer.IsVisible;
        if (cleared)
        {
            await binding.Host.ClearLayerAsync(layer.Kind, layer.Transition, cancellationToken).ConfigureAwait(true);
            binding.AppliedLayers.Remove(layer.Kind);
            binding.LastPayloadKeys.Remove(layer.Kind);
            return;
        }

        string stateKey = BuildLayerStateKey(layer, binding.Configuration?.RenderSize);
        if (binding.LastPayloadKeys.TryGetValue(layer.Kind, out string? previous)
            && string.Equals(previous, stateKey, StringComparison.Ordinal))
        {
            return;
        }

        if (layer.Payload.Detail is MediaRenderPayload media
            && !string.IsNullOrWhiteSpace(media.Media.ResolvedSourcePath))
        {
            LayerSlotId slotId = LayerSlotId.For(screenId, layer.Kind);
            if (IsStillMedia(media.Media))
            {
                await binding.Host.ApplyImageLayerAsync(
                    layer.Kind, media.Media.ResolvedSourcePath, media.Media.Fit, layer.Transition, cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                WinUiMediaSlotAdapter mediaBinding = _mediaSlots.GetOrCreate(
                    slotId, layer.Payload.Id, media.Media.ResolvedSourcePath, media.Media.Loop);

                if (!mediaBinding.IsBoundTo(media.Media.ResolvedSourcePath, media.Media.Loop))
                {
                    await mediaBinding.BindAsync(
                        media.Media.ResolvedSourcePath,
                        loop: media.Media.Loop,
                        volume: media.Media.Muted ? 0 : 1,
                        startPlayback: false,
                        cancellationToken).ConfigureAwait(true);
                }

                await binding.Host.ApplyVideoLayerAsync(
                    layer.Kind,
                    mediaBinding.Player,
                    media.Media.Fit,
                    layer.Transition,
                    startPlayback: ct => mediaBinding.PlayAsync(ct),
                    cancellationToken).ConfigureAwait(true);
            }

            binding.AppliedLayers.Add(layer.Kind);
            binding.LastPayloadKeys[layer.Kind] = stateKey;
            return;
        }

        if (layer.Payload.Detail is LiveVideoRenderPayload live)
        {
            await binding.Host.ApplyLiveVideoLayerAsync(
                layer.Kind, live.SourceId, live.Fit, layer.Transition, cancellationToken).ConfigureAwait(true);
            binding.AppliedLayers.Add(layer.Kind);
            binding.LastPayloadKeys[layer.Kind] = stateKey;
            return;
        }

        if (TryResolveScene(layer.Payload.Detail, out SlideScene scene))
        {
            await binding.Host.ApplySceneLayerAsync(layer.Kind, scene, layer.Transition, null, cancellationToken)
                .ConfigureAwait(true);
            binding.AppliedLayers.Add(layer.Kind);
            binding.LastPayloadKeys[layer.Kind] = stateKey;
            return;
        }

        await binding.Host.ClearLayerAsync(layer.Kind, layer.Transition, cancellationToken).ConfigureAwait(true);
        binding.AppliedLayers.Remove(layer.Kind);
        binding.LastPayloadKeys.Remove(layer.Kind);
    }

    private ScreenBinding? Resolve(string screenId)
    {
        lock (_gate)
            return _screens.TryGetValue(screenId, out ScreenBinding? binding) ? binding : null;
    }

    private void RaiseHealth(HostHealthSeverity severity, string message) =>
        HostHealthRaised?.Invoke(this, new HostHealthEvent { Severity = severity, Message = message });

    private static readonly TimeSpan VideoPrebindReadyTimeout = TimeSpan.FromSeconds(3);

    private static OutputLayerKind MapPayloadKind(RenderPayloadDescriptor payload) =>
        payload.Detail switch
        {
            MediaRenderPayload => OutputLayerKind.Media,
            LiveVideoRenderPayload => OutputLayerKind.LiveVideo,
            MaskRenderPayload => OutputLayerKind.Mask,
            _ => OutputLayerKind.Slide,
        };

    private static bool TryResolveScene(RenderPayloadDetail? detail, out SlideScene scene)
    {
        switch (detail)
        {
            case PresentationRenderPayload presentation:
                scene = presentation.Scene;
                return true;
            case OverlayRenderPayload { Scene: { } overlayScene }:
                scene = overlayScene;
                return true;
            case MaskRenderPayload { Scene: { } maskScene }:
                scene = maskScene;
                return true;
            default:
                scene = SlideScene.Empty;
                return false;
        }
    }

    private static string BuildLayerStateKey(RenderLayerDescriptor layer, PixelSize? renderSize) =>
        string.Join(
            "|",
            renderSize?.Width ?? 0,
            renderSize?.Height ?? 0,
            layer.Payload.Id,
            layer.Payload.Kind,
            layer.IsVisible,
            layer.IsSuppressed,
            layer.ClearState,
            layer.Transition.Type ?? string.Empty,
            layer.Transition.Phase,
            layer.Transition.Duration.Ticks,
            DescribeDetail(layer.Payload.Detail));

    private static string DescribeDetail(RenderPayloadDetail? detail) =>
        detail switch
        {
            PresentationRenderPayload p => $"{p.Scene.Id}:{p.Scene.Version}:{p.ThemeVariantId}",
            OverlayRenderPayload { Scene: { } s } => $"{s.Id}:{s.Version}",
            MediaRenderPayload m => $"{m.Media.MediaId}:{m.Media.ResolvedSourcePath}:{m.Media.Fit}:{m.Media.Loop}:{m.Media.Muted}",
            LiveVideoRenderPayload l => $"{l.SourceId}:{l.Fit}:{l.HasAudio}",
            MaskRenderPayload k => $"{k.MaskId}:{k.MaskKind}:{k.SourcePath}:{k.Scene?.Version}:{k.Invert}",
            _ => string.Empty,
        };

    private static bool IsStillMedia(OutputLayerMedia media)
    {
        if (string.Equals(media.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            return true;
        string? extension = Path.GetExtension(media.ResolvedSourcePath);
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private static bool IsVideoMedia(OutputLayerMedia media)
    {
        if (string.Equals(media.MediaType, "video", StringComparison.OrdinalIgnoreCase))
            return true;
        string? extension = Path.GetExtension(media.ResolvedSourcePath);
        return extension is ".mp4" or ".mov" or ".m4v" or ".avi" or ".mkv" or ".webm" or ".wmv";
    }

    private static async Task RunOnHostAsync(ScreenBinding binding, Func<Task> action, CancellationToken cancellationToken) =>
        await RunOnDispatcherAsync(binding.DispatcherQueue, action, cancellationToken).ConfigureAwait(false);

    private static async Task RunOnDispatcherAsync(DispatcherQueue dispatcher, Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dispatcher.HasThreadAccess)
        {
            await action().ConfigureAwait(true);
            return;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<object?>)state!).TrySetCanceled(), completion);

        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action().ConfigureAwait(true);
                completion.TrySetResult(null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            return;
        }

        await completion.Task.ConfigureAwait(false);
    }

    private sealed class ScreenBinding(string screenId, LayerStackHost host, DispatcherQueue dispatcherQueue)
    {
        public string ScreenId { get; } = screenId;

        public LayerStackHost Host { get; } = host;

        public DispatcherQueue DispatcherQueue { get; } = dispatcherQueue;

        public ScreenConfiguration? Configuration { get; set; }

        public CompositionVisualSurface? Surface { get; set; }

        public FrameworkElement? Element { get; set; }

        public bool IsPreview { get; init; }

        public HashSet<OutputLayerKind> AppliedLayers { get; } = [];

        public Dictionary<OutputLayerKind, string> LastPayloadKeys { get; } = new();

        public void ClearLayerCache()
        {
            AppliedLayers.Clear();
            LastPayloadKeys.Clear();
        }
    }
}
