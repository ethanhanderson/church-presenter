using System.Diagnostics;

using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.Diagnostics;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Pipeline.Rendering;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Services.Output;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Central orchestrator for the unified output pipeline runtime.
/// </summary>
public sealed class CuePipelineRuntime
{
    private readonly object _gate = new();
    private readonly ILiveCommandExecutor _commandExecutor;
    private readonly ILayerRouter _layerRouter;
    private readonly FrameComposer _frameComposer;
    private readonly ICuePreparationCoordinator _preparationCoordinator;
    private readonly IOutputRoutingService _routing;
    private readonly IOutputTopologyService _topology;
    private readonly IStageLayoutRegistryService _stageLayouts;
    private readonly IPipelineDiagnostics _diagnostics;
    private readonly ILogger<CuePipelineRuntime> _logger;
    private readonly List<IAudienceHostAdapter> _audienceAdapters = [];
    private readonly List<IStageHostAdapter> _stageAdapters = [];
    private LiveSessionSnapshot _snapshot = LiveSessionSnapshot.CreateEmpty();
    private readonly Dictionary<OutputLayerKind, Dictionary<LayerSlotId, IMediaSlotBinding>> _activeMediaBindings = new();
    private readonly Dictionary<IMediaSlotBinding, EventHandler> _mediaBindingHandlers = new();
    private IMediaSlotBinding? _activeMediaBinding;
    private bool _audienceEnabled;
    private bool _stageEnabled;

    /// <summary>Creates the pipeline runtime.</summary>
    public CuePipelineRuntime(
        ILiveCommandExecutor commandExecutor,
        ILayerRouter layerRouter,
        FrameComposer frameComposer,
        ICuePreparationCoordinator preparationCoordinator,
        IOutputRoutingService routing,
        IOutputTopologyService topology,
        IStageLayoutRegistryService stageLayouts,
        IPipelineDiagnostics diagnostics,
        ILogger<CuePipelineRuntime> logger,
        IEnumerable<IAudienceHostAdapter>? audienceAdapters = null,
        IEnumerable<IStageHostAdapter>? stageAdapters = null)
    {
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _layerRouter = layerRouter ?? throw new ArgumentNullException(nameof(layerRouter));
        _frameComposer = frameComposer ?? throw new ArgumentNullException(nameof(frameComposer));
        _preparationCoordinator = preparationCoordinator ?? throw new ArgumentNullException(nameof(preparationCoordinator));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _stageLayouts = stageLayouts ?? throw new ArgumentNullException(nameof(stageLayouts));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (audienceAdapters != null)
            _audienceAdapters.AddRange(audienceAdapters);
        if (stageAdapters != null)
            _stageAdapters.AddRange(stageAdapters);

        _routing.Changed += HandleRoutingChanged;
        _snapshot = InitializeSnapshot();
    }

    /// <summary>Raised whenever the live session snapshot or composed frames change.</summary>
    public event EventHandler? Changed;

    /// <summary>Current live session snapshot.</summary>
    public LiveSessionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    /// <summary>Whether audience output is enabled.</summary>
    public bool AudienceEnabled
    {
        get
        {
            lock (_gate)
                return _audienceEnabled;
        }
    }

    /// <summary>Whether stage output is enabled.</summary>
    public bool StageEnabled
    {
        get
        {
            lock (_gate)
                return _stageEnabled;
        }
    }

    /// <summary>Active media transport binding, when any.</summary>
    public IMediaSlotBinding? ActiveMediaBinding
    {
        get
        {
            lock (_gate)
                return _activeMediaBinding;
        }
    }

    /// <summary>Gets the active media transport binding for a media-capable output layer.</summary>
    public IMediaSlotBinding? GetActiveMediaBinding(OutputLayerKind layerKind)
    {
        lock (_gate)
            return _activeMediaBindings.TryGetValue(layerKind, out Dictionary<LayerSlotId, IMediaSlotBinding>? bindings)
                ? bindings.Values.FirstOrDefault()
                : null;
    }

    /// <summary>Gets active media transport bindings for a media-capable output layer.</summary>
    public IReadOnlyList<IMediaSlotBinding> GetActiveMediaBindings(OutputLayerKind layerKind)
    {
        lock (_gate)
            return _activeMediaBindings.TryGetValue(layerKind, out Dictionary<LayerSlotId, IMediaSlotBinding>? bindings)
                ? bindings.Values.ToArray()
                : Array.Empty<IMediaSlotBinding>();
    }

    /// <summary>Sets audience output enabled state.</summary>
    public Task SetAudienceEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _audienceEnabled = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Sets stage output enabled state.</summary>
    public Task SetStageEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _stageEnabled = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Dispatches a prepared slide cue through the pipeline.</summary>
    public async Task<DispatchReceipt> DispatchSlideCueAsync(
        PreparedSlideCue cue,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cue);
        Stopwatch prepareWatch = Stopwatch.StartNew();
        PreparedCueRecord? prepared = await _preparationCoordinator.PrepareSlideAsync(cue, cancellationToken).ConfigureAwait(false);
        prepareWatch.Stop();

        if (prepared == null)
        {
            return new DispatchReceipt
            {
                Succeeded = false,
                Error = "Slide cue preparation failed.",
                Timing = new PipelineTiming { PrepareTimeMs = prepareWatch.Elapsed.TotalMilliseconds },
            };
        }

        return await ApplyPreparedCueAsync(prepared, source, prepareWatch.Elapsed.TotalMilliseconds, CacheHitTier.Hot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Dispatches a prepared media cue through the pipeline.</summary>
    public async Task<DispatchReceipt> DispatchMediaCueAsync(
        PreparedMediaCue cue,
        string? target = null,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cue);
        PreparedMediaCue dispatchCue = NormalizeMediaCueTarget(cue, target);
        Stopwatch prepareWatch = Stopwatch.StartNew();
        PreparedCueRecord? prepared = await _preparationCoordinator.PrepareMediaAsync(dispatchCue, cancellationToken).ConfigureAwait(false);
        prepareWatch.Stop();

        if (prepared == null)
        {
            return new DispatchReceipt
            {
                Succeeded = false,
                Error = "Media cue preparation failed.",
                Timing = new PipelineTiming { PrepareTimeMs = prepareWatch.Elapsed.TotalMilliseconds },
            };
        }

        return await ApplyPreparedCueAsync(prepared, source, prepareWatch.Elapsed.TotalMilliseconds, CacheHitTier.Hot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Dispatches a normalized live command through the pipeline.</summary>
    public Task<DispatchReceipt> DispatchAsync(LiveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch routeWatch = Stopwatch.StartNew();
        LiveSessionSnapshot updated;
        lock (_gate)
        {
            ActionResult result = _commandExecutor.Execute(_snapshot.SessionState, command);
            updated = _snapshot with
            {
                SessionState = result.State,
                Version = result.State.Version,
                ActiveLookId = _routing.ActiveLookId,
            };
            _snapshot = updated;
        }

        routeWatch.Stop();
        return ComposeAndApplyAsync(command.Id, command.CorrelationId, routeWatch.Elapsed.TotalMilliseconds, 0, CacheHitTier.Miss);
    }

    /// <summary>Clears the specified output layers.</summary>
    public Task<DispatchReceipt> ClearLayersAsync(IEnumerable<OutputLayerKind> layers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layers);
        cancellationToken.ThrowIfCancellationRequested();

        OutputLayerKind[] materializedLayers = layers.ToArray();

        Stopwatch routeWatch = Stopwatch.StartNew();
        lock (_gate)
            _snapshot = _layerRouter.ClearLayers(_snapshot, materializedLayers);
        routeWatch.Stop();

        return ComposeAndApplyAsync(Guid.NewGuid(), correlationId: null, 0, routeWatch.Elapsed.TotalMilliseconds, CacheHitTier.Miss);
    }

    /// <summary>Clears a configured clear group.</summary>
    public Task<DispatchReceipt> ClearGroupAsync(string clearGroupId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clearGroupId);
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch routeWatch = Stopwatch.StartNew();
        lock (_gate)
            _snapshot = _layerRouter.ClearGroup(_snapshot, clearGroupId);
        routeWatch.Stop();

        return ComposeAndApplyAsync(Guid.NewGuid(), correlationId: null, 0, routeWatch.Elapsed.TotalMilliseconds, CacheHitTier.Miss);
    }

    /// <summary>Releases runtime clear overrides for the specified layers.</summary>
    public Task<DispatchReceipt> ReleaseClearedLayersAsync(IEnumerable<OutputLayerKind> layers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layers);
        cancellationToken.ThrowIfCancellationRequested();

        OutputLayerKind[] materializedLayers = layers.ToArray();

        lock (_gate)
            _snapshot = _layerRouter.ReleaseClearedLayers(_snapshot, materializedLayers);

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new DispatchReceipt { Succeeded = true, CommandId = Guid.NewGuid() });
    }

    /// <summary>Activates a Look preset.</summary>
    public async Task<DispatchReceipt> SetLookAsync(string lookId, CancellationToken cancellationToken = default)
    {
        await _routing.SetActiveLookAsync(lookId, cancellationToken).ConfigureAwait(false);
        lock (_gate)
            _snapshot = RebuildTopologySnapshot(_snapshot);

        Changed?.Invoke(this, EventArgs.Empty);
        return await ComposeAndApplyAsync(Guid.NewGuid(), correlationId: null, 0, 0, CacheHitTier.Miss).ConfigureAwait(false);
    }

    /// <summary>Updates generated overlay state.</summary>
    public Task<DispatchReceipt> SetOverlayAsync(OverlayContentState overlay, CancellationToken cancellationToken = default)
    {
        LiveCommand command = LiveCommandExecutor.SetOverlay(overlay);
        return DispatchAsync(command, cancellationToken);
    }

    /// <summary>Updates timer state.</summary>
    public Task<DispatchReceipt> SetTimerAsync(TimerSnapshot timer, CancellationToken cancellationToken = default)
    {
        LiveCommand command = LiveCommandExecutor.SetTimer(timer);
        return DispatchAsync(command, cancellationToken);
    }

    /// <summary>Updates capture-session state.</summary>
    public Task<DispatchReceipt> SetCaptureSessionAsync(CaptureSessionState captureSession, CancellationToken cancellationToken = default)
    {
        LiveCommand command = LiveCommandExecutor.SetCaptureSession(captureSession);
        return DispatchAsync(command, cancellationToken);
    }

    /// <summary>Assigns a stage layout to a stage screen.</summary>
    public Task<DispatchReceipt> SetStageLayoutAsync(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience,
        CancellationToken cancellationToken = default)
    {
        LiveCommand command = LiveCommandExecutor.SetStageLayout(screenId, stageLayoutId, deliveryMode);
        return DispatchAsync(command, cancellationToken);
    }

    /// <summary>Executes a macro expansion through the pipeline.</summary>
    public Task<DispatchReceipt> ExecuteMacroAsync(
        LiveMacroDefinition macro,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ActionBatch batch = _commandExecutor.ExpandMacro(macro, source);

        Stopwatch routeWatch = Stopwatch.StartNew();
        lock (_gate)
        {
            ActionResult result = _commandExecutor.Execute(_snapshot.SessionState, batch);
            _snapshot = _snapshot with
            {
                SessionState = result.State,
                Version = result.State.Version,
                ActiveLookId = _routing.ActiveLookId,
            };
        }

        routeWatch.Stop();
        return ComposeAndApplyAsync(batch.SourceCommandId, batch.CorrelationId, 0, routeWatch.Elapsed.TotalMilliseconds, CacheHitTier.Miss);
    }

    /// <summary>Clears all primary live output layers and ends the current live session.</summary>
    public Task<DispatchReceipt> EndLiveAsync(CancellationToken cancellationToken = default) =>
        ClearLayersAsync(
        [
            OutputLayerKind.Slide,
            OutputLayerKind.Media,
            OutputLayerKind.Audio,
            OutputLayerKind.Announcements,
            OutputLayerKind.Messages,
            OutputLayerKind.Props,
            OutputLayerKind.LiveVideo,
        ],
        cancellationToken);

    /// <summary>Re-composes the current live snapshot after global transition defaults change.</summary>
    public Task<DispatchReceipt> InvalidateTransitionDefaultsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ComposeAndApplyAsync(Guid.NewGuid(), correlationId: null, 0, 0, CacheHitTier.Miss, cancellationToken);
    }

    /// <summary>Replaces a live layer payload after content invalidation.</summary>
    public async Task<DispatchReceipt> ReplaceLayerPayloadAsync(
        OutputLayerKind layer,
        PreparedCueRecord preparedCue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedCue);
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch routeWatch = Stopwatch.StartNew();
        lock (_gate)
            _snapshot = _layerRouter.ReplaceLayerPayload(_snapshot, layer, preparedCue);
        routeWatch.Stop();

        return await ComposeAndApplyAsync(Guid.NewGuid(), correlationId: null, 0, routeWatch.Elapsed.TotalMilliseconds, CacheHitTier.Hot).ConfigureAwait(false);
    }

    private async Task<DispatchReceipt> ApplyPreparedCueAsync(
        PreparedCueRecord prepared,
        LiveCommandSource? source,
        double prepareTimeMs,
        CacheHitTier cacheHitTier,
        CancellationToken cancellationToken = default)
    {
        _ = source;
        Stopwatch routeWatch = Stopwatch.StartNew();
        lock (_gate)
        {
            _snapshot = _layerRouter.ApplyPreparedCue(_snapshot, prepared);
        }
        routeWatch.Stop();

        return await ComposeAndApplyAsync(Guid.NewGuid(), correlationId: null, prepareTimeMs, routeWatch.Elapsed.TotalMilliseconds, cacheHitTier, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DispatchReceipt> ComposeAndApplyAsync(
        Guid commandId,
        string? correlationId,
        double prepareTimeMs,
        double routeTimeMs,
        CacheHitTier cacheHitTier,
        CancellationToken cancellationToken = default)
    {
        Stopwatch composeWatch = Stopwatch.StartNew();
        LiveSessionSnapshot snapshot;
        bool audienceEnabled;
        bool stageEnabled;
        lock (_gate)
        {
            snapshot = _snapshot;
            audienceEnabled = _audienceEnabled;
            stageEnabled = _stageEnabled;
        }

        RenderFrameSet frames = _frameComposer.Compose(snapshot);
        int mediaPlaybackLayerCount = CountPlaybackLayers(frames);
        composeWatch.Stop();

        Stopwatch applyWatch = Stopwatch.StartNew();
        try
        {
            // There is ONE canonical render per screen, owned by the app-lifetime render service and
            // always on (so the operator preview works even when an output window is off). Apply each
            // screen's frame to its canonical render exactly once; the audience/stage windows and the
            // operator preview are passive surface mirrors of these renders. The audienceEnabled/
            // stageEnabled flags now gate only whether the output WINDOWS are shown (OutputWindowService)
            // and how feedback visibility is reported — not whether the render runs.
            List<Task> applyTasks = [];

            foreach (OutputScreen screen in snapshot.SessionState.Screens.Values.Where(static s => s.Kind == OutputScreenKind.Audience))
                applyTasks.Add(ApplyAudienceScreenAsync(screen, frames, cancellationToken));

            foreach (OutputScreen screen in snapshot.SessionState.Screens.Values.Where(static s => s.Kind == OutputScreenKind.Stage))
                applyTasks.Add(ApplyStageScreenAsync(screen, frames, cancellationToken));

            await Task.WhenAll(applyTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed applying composed frames for command {CommandId}.", commandId);
            applyWatch.Stop();
            return new DispatchReceipt
            {
                Succeeded = false,
                CommandId = commandId,
                CorrelationId = correlationId,
                Error = ex.Message,
                Timing = new PipelineTiming
                {
                    PrepareTimeMs = prepareTimeMs,
                    RouteTimeMs = routeTimeMs,
                    ComposeTimeMs = composeWatch.Elapsed.TotalMilliseconds,
                    ApplyTimeMs = applyWatch.Elapsed.TotalMilliseconds,
                    CacheHitTier = cacheHitTier,
                },
            };
        }

        applyWatch.Stop();
        RecordAppliedFrameFeedback(
            frames, snapshot, audienceEnabled, stageEnabled, applyWatch.Elapsed, composeWatch.Elapsed);
        PipelineTiming timing = new()
        {
            PrepareTimeMs = prepareTimeMs,
            RouteTimeMs = routeTimeMs,
            ComposeTimeMs = composeWatch.Elapsed.TotalMilliseconds,
            ApplyTimeMs = applyWatch.Elapsed.TotalMilliseconds,
            MediaReadyTimeMs = _activeMediaBinding?.IsReady == true ? applyWatch.Elapsed.TotalMilliseconds : 0,
            LoopGapMs = _activeMediaBinding?.LoopGapMs ?? 0,
            CacheHitTier = cacheHitTier,
        };
        _diagnostics.RecordTiming(timing);
        Changed?.Invoke(this, EventArgs.Empty);

        return new DispatchReceipt
        {
            Succeeded = true,
            CommandId = commandId,
            CorrelationId = correlationId,
            Timing = timing,
        };
    }

    private async Task ApplyAudienceScreenAsync(
        OutputScreen screen,
        RenderFrameSet frames,
        CancellationToken cancellationToken)
    {
        ScreenConfiguration configuration = new()
        {
            ScreenId = screen.Id,
            RenderSize = screen.RenderSize,
            IsStage = false,
        };
        frames.AudienceFrames.TryGetValue(screen.Id, out AudienceRenderFrame? audienceFrame);

        foreach (IAudienceHostAdapter adapter in _audienceAdapters)
        {
            await adapter.EnsureSurfacesAsync(configuration, cancellationToken).ConfigureAwait(false);
            if (audienceFrame != null)
            {
                await BindFramePlaybackLayersAsync(adapter, audienceFrame, cancellationToken).ConfigureAwait(false);
                await adapter.ApplyFrameAsync(audienceFrame, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ApplyStageScreenAsync(
        OutputScreen screen,
        RenderFrameSet frames,
        CancellationToken cancellationToken)
    {
        ScreenConfiguration configuration = new()
        {
            ScreenId = screen.Id,
            RenderSize = screen.RenderSize,
            IsStage = true,
        };

        foreach (IStageHostAdapter adapter in _stageAdapters)
        {
            await adapter.EnsureSurfacesAsync(configuration, cancellationToken).ConfigureAwait(false);
            if (frames.StageFrames.TryGetValue(screen.Id, out StageRenderFrame? frame))
                await adapter.ApplyFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BindFramePlaybackLayersAsync(
        IAudienceHostAdapter adapter,
        AudienceRenderFrame frame,
        CancellationToken cancellationToken)
    {
        foreach (RenderLayerDescriptor layer in frame.Layers)
        {
            if (!ShouldBindPlaybackLayer(layer))
                continue;

            LayerSlotId slotId = LayerSlotId.For(frame.ScreenId, layer.Kind);
            IMediaSlotBinding? binding = await adapter
                .BindMediaSlotAsync(slotId, layer.Payload, cancellationToken)
                .ConfigureAwait(false);
            if (binding == null)
                continue;

            lock (_gate)
                RegisterActiveMediaBinding(layer.Kind, binding);
        }
    }

    private static int CountPlaybackLayers(RenderFrameSet frames) =>
        frames.AudienceFrames.Values.Sum(static frame => frame.Layers.Count(ShouldBindPlaybackLayer));

    private static bool ShouldBindPlaybackLayer(RenderLayerDescriptor layer) =>
        layer.Kind is OutputLayerKind.Media or OutputLayerKind.Audio or OutputLayerKind.Announcements
        && layer.Payload.Detail is MediaRenderPayload
        && layer.IsVisible
        && !layer.IsSuppressed
        && layer.ClearState == LayerClearState.None;

    /// <summary>
    /// Writes host frame-application feedback back into the live session for operator diagnostics.
    /// Each output screen reports the resolved/applied backend frame sequence, apply timing, and
    /// visibility so <see cref="PipelineOperatorQueryProjection"/> can surface stale or dropped
    /// frames. Applied sequence advances only when the screen's output is actually enabled, so a
    /// disabled audience/stage output reads as resolved-ahead-of-applied (stale) rather than current.
    /// </summary>
    private void RecordAppliedFrameFeedback(
        RenderFrameSet frames,
        LiveSessionSnapshot snapshot,
        bool audienceEnabled,
        bool stageEnabled,
        TimeSpan applyDuration,
        TimeSpan composeDuration)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            GeneratedStateSnapshot generated = _snapshot.SessionState.GeneratedState;
            Dictionary<string, OutputHostFrameFeedbackState> map =
                new(generated.HostFeedback, StringComparer.OrdinalIgnoreCase);

            foreach (OutputScreen screen in snapshot.SessionState.Screens.Values)
            {
                long? resolvedSequence;
                bool visible;
                if (screen.Kind == OutputScreenKind.Audience
                    && frames.AudienceFrames.TryGetValue(screen.Id, out AudienceRenderFrame? audienceFrame))
                {
                    resolvedSequence = audienceFrame.Sequence;
                    visible = audienceEnabled;
                }
                else if (screen.Kind == OutputScreenKind.Stage
                    && frames.StageFrames.TryGetValue(screen.Id, out StageRenderFrame? stageFrame))
                {
                    resolvedSequence = stageFrame.Sequence;
                    visible = stageEnabled;
                }
                else
                {
                    continue;
                }

                map.TryGetValue(screen.Id, out OutputHostFrameFeedbackState? prior);
                long? appliedSequence = visible ? resolvedSequence : prior?.LastAppliedFrameSequence;
                int dropped = prior?.DroppedFrameCount ?? 0;
                if (visible
                    && prior?.LastAppliedFrameSequence is { } previousApplied
                    && resolvedSequence is { } current
                    && current > previousApplied + 1)
                {
                    dropped += (int)Math.Min(int.MaxValue, current - previousApplied - 1);
                }

                map[screen.Id] = new OutputHostFrameFeedbackState
                {
                    ScreenId = screen.Id,
                    EndpointId = screen.Id,
                    LastResolvedFrameSequence = resolvedSequence,
                    LastAppliedFrameSequence = appliedSequence,
                    LastAppliedAt = visible ? now : prior?.LastAppliedAt,
                    LastRenderDuration = composeDuration,
                    LastApplyDuration = applyDuration,
                    DroppedFrameCount = dropped,
                    IsVisible = visible,
                    EndpointHealth = EndpointHealth.Connected,
                };
            }

            _snapshot = _snapshot with
            {
                SessionState = _snapshot.SessionState with
                {
                    GeneratedState = generated with { HostFeedback = map },
                },
            };
        }
    }

    private void RegisterActiveMediaBinding(OutputLayerKind layerKind, IMediaSlotBinding binding)
    {
        if (!_activeMediaBindings.TryGetValue(layerKind, out Dictionary<LayerSlotId, IMediaSlotBinding>? bindings))
        {
            bindings = [];
            _activeMediaBindings[layerKind] = bindings;
        }

        bindings[binding.SlotId] = binding;
        _activeMediaBinding = binding;

        if (_mediaBindingHandlers.ContainsKey(binding))
            return;

        EventHandler handler = HandleMediaBindingStateChanged;
        binding.StateChanged += handler;
        _mediaBindingHandlers[binding] = handler;
    }

    private void HandleMediaBindingStateChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void HandleRoutingChanged(object? sender, EventArgs args)
    {
        lock (_gate)
            _snapshot = RebuildTopologySnapshot(_snapshot);
        Changed?.Invoke(this, EventArgs.Empty);
        _ = InvalidateTransitionDefaultsAsync();
    }

    private static PreparedMediaCue NormalizeMediaCueTarget(PreparedMediaCue cue, string? target)
    {
        string targetName = string.IsNullOrWhiteSpace(target) ? cue.Target : target;
        MediaPlaybackLayerTarget layerTarget = MediaPlaybackLayerTargetNames.FromLayerName(targetName);
        OutputLayerKind layerKind = layerTarget == MediaPlaybackLayerTarget.Audio
            ? OutputLayerKind.Audio
            : OutputLayerKind.Media;

        return cue with
        {
            Target = MediaPlaybackLayerTargetNames.ToLayerName(layerTarget),
            LayerKind = layerKind,
        };
    }

    private LiveSessionSnapshot InitializeSnapshot()
    {
        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty();
        return RebuildTopologySnapshot(snapshot);
    }

    private LiveSessionSnapshot RebuildTopologySnapshot(LiveSessionSnapshot snapshot)
    {
        OutputTopologySnapshot topology = _topology.GetSnapshot();
        IReadOnlyDictionary<string, StageLayout> stageLayoutCatalog = _stageLayouts.GetLayouts();
        IReadOnlyDictionary<string, string> stageAssignments = _stageLayouts.GetDefaultLayoutIdsByScreenId();

        return snapshot with
        {
            SessionState = snapshot.SessionState with
            {
                Screens = topology.Screens,
                Endpoints = topology.Endpoints,
                ScreenMappings = topology.ScreenMappings,
                StageLayouts = stageLayoutCatalog,
                StageLayoutIdsByScreenId = stageAssignments,
            },
            ActiveLookId = _routing.ActiveLookId,
        };
    }
}
