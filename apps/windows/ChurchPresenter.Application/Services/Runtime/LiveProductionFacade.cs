using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Synchronizes legacy playback state into the new backend live-production session and frame model.
/// </summary>
public sealed class LiveProductionFacade : ILiveProductionFacade
{
    private readonly IPlaybackEngine _playback;
    private readonly IOutputRoutingService _routing;
    private readonly IOutputTopologyService _topology;
    private readonly ILiveCommandExecutor _commandExecutor;
    private readonly IRenderFrameResolver _frameResolver;
    private readonly IRenderFrameStore _frameStore;
    private readonly IStageLayoutRegistryService _stageLayouts;
    private readonly ISlideSceneCompiler _slideSceneCompiler;
    private readonly IThemeResolutionService _themeResolution;
    private readonly LiveProductionRuntimeOverrides _runtimeOverrides = new();
    private readonly object _hostFeedbackGate = new();
    private readonly Dictionary<string, OutputHostFrameFeedbackState> _hostFeedback = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaPlayerFailureState> _mediaPlayerFailures = new(StringComparer.OrdinalIgnoreCase);
    private long _version;

    public LiveProductionFacade(
        IPlaybackEngine playback,
        IOutputRoutingService routing,
        IOutputTopologyService topology,
        ILiveCommandExecutor commandExecutor,
        IRenderFrameResolver frameResolver,
        IRenderFrameStore frameStore,
        IStageLayoutRegistryService stageLayouts,
        ISlideSceneCompiler? slideSceneCompiler = null,
        IThemeResolutionService? themeResolution = null)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _frameResolver = frameResolver ?? throw new ArgumentNullException(nameof(frameResolver));
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _stageLayouts = stageLayouts ?? throw new ArgumentNullException(nameof(stageLayouts));
        _slideSceneCompiler = slideSceneCompiler ?? new SlideSceneCompiler();
        _themeResolution = themeResolution ?? new ThemeResolutionService();

        _playback.StateChanged += HandleChanged;
        _routing.Changed += HandleChanged;
        Current = BuildSnapshot();
    }

    /// <inheritdoc />
    public event EventHandler<LiveProductionChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LiveProductionSnapshot Current { get; private set; }

    /// <inheritdoc />
    public Task SetLookAsync(string lookId, CancellationToken cancellationToken = default)
    {
        return _routing.SetActiveLookAsync(lookId, cancellationToken);
    }

    /// <inheritdoc />
    public ActionResult SetOverlay(OverlayContentState overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return ApplyRuntimeCommand(LiveCommandExecutor.SetOverlay(overlay));
    }

    /// <inheritdoc />
    public ActionResult SetTimer(TimerSnapshot timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        return ApplyRuntimeCommand(LiveCommandExecutor.SetTimer(timer));
    }

    /// <inheritdoc />
    public ActionResult SetCaptureSession(CaptureSessionState captureSession)
    {
        ArgumentNullException.ThrowIfNull(captureSession);
        return ApplyRuntimeCommand(LiveCommandExecutor.SetCaptureSession(captureSession));
    }

    /// <inheritdoc />
    public void ReportOutputHostFeedback(OutputHostFrameFeedbackState feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback.ScreenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback.EndpointId);

        OutputHostFrameFeedbackState enrichedFeedback = feedback with
        {
            LastResolvedFrameSequence = feedback.LastResolvedFrameSequence ?? ResolveFrameSequence(feedback.ScreenId),
        };

        lock (_hostFeedbackGate)
        {
            _hostFeedback[CreateHostFeedbackKey(enrichedFeedback.ScreenId, enrichedFeedback.EndpointId)] = enrichedFeedback;
        }

        PublishHostFeedbackSnapshot();
    }

    /// <inheritdoc />
    public void ReportMediaPlayerFailure(MediaPlayerFailureState failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure.PlayerId);

        lock (_hostFeedbackGate)
        {
            _mediaPlayerFailures[failure.PlayerId] = failure;
        }

        PublishHostFeedbackSnapshot();
    }

    /// <inheritdoc />
    public ActionResult ExecuteCommands(
        IEnumerable<LiveCommand> commands,
        LiveCommandSource? source = null,
        string? macroId = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return ApplyRuntimeBatch(_commandExecutor.Expand(commands, source, macroId));
    }

    /// <inheritdoc />
    public ActionResult ExecuteMacro(LiveMacroDefinition macro, LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        return ApplyRuntimeBatch(_commandExecutor.ExpandMacro(macro, source));
    }

    /// <inheritdoc />
    public ActionResult ClearGroup(string clearGroupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clearGroupId);

        return ApplyRuntimeCommand(LiveCommandExecutor.ClearGroup(clearGroupId));
    }

    /// <inheritdoc />
    public ActionResult ClearLayers(IEnumerable<BackendOutputLayerKind> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        return ApplyRuntimeCommand(LiveCommandExecutor.ClearLayers(layers.ToHashSet()));
    }

    /// <inheritdoc />
    public ActionResult ReleaseClearedLayers(IEnumerable<BackendOutputLayerKind> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        HashSet<BackendOutputLayerKind> layerSet = layers.ToHashSet();
        if (layerSet.Count == 0)
        {
            return new ActionResult
            {
                Succeeded = true,
                State = Current.SessionState,
                Frames = Current.Frames,
            };
        }

        _runtimeOverrides.ReleaseClearedLayers(layerSet);
        Current = BuildSnapshot();
        Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });

        return new ActionResult
        {
            Succeeded = true,
            State = Current.SessionState,
            Frames = Current.Frames,
        };
    }

    /// <inheritdoc />
    public ActionResult SetStageLayout(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageLayoutId);

        return ApplyRuntimeCommand(LiveCommandExecutor.SetStageLayout(screenId, stageLayoutId, deliveryMode));
    }

    private void HandleChanged(object? sender, EventArgs args)
    {
        Current = BuildSnapshot();
        Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
    }

    private ActionResult ApplyRuntimeCommand(LiveCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyRuntimeBatch(_commandExecutor.Expand(command));
    }

    private ActionResult ApplyRuntimeBatch(ActionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        LookPreset activeLookBeforeApply = Current.SessionState.ActiveLook;
        LiveRenderSessionState stateBeforeApply = Current.SessionState;
        ActionResult result = _commandExecutor.Execute(Current.SessionState, batch);
        if (result.Succeeded)
            _runtimeOverrides.RecordAppliedBatch(batch, activeLookBeforeApply, stateBeforeApply);

        Current = Current with
        {
            SessionState = AttachHostFeedback(result.State),
            Frames = result.Frames,
        };
        _frameStore.Save(result.Frames);

        Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
        return result;
    }

    private LiveProductionSnapshot BuildSnapshot()
    {
        PlaybackState playbackState = _playback.CurrentState;
        OutputTopologySnapshot topology = _topology.GetSnapshot();
        (LiveRenderSessionState sessionState, RenderFrameSet frames) = BuildSessionState(
            playbackState,
            _routing,
            topology,
            _commandExecutor,
            _frameResolver,
            _stageLayouts,
            _runtimeOverrides,
            _slideSceneCompiler,
            _themeResolution,
            ++_version);
        _frameStore.Save(frames);

        return new LiveProductionSnapshot
        {
            PlaybackState = playbackState,
            SessionState = AttachHostFeedback(sessionState),
            Frames = frames,
            Topology = topology,
        };
    }

    private void PublishHostFeedbackSnapshot()
    {
        Current = Current with
        {
            SessionState = AttachHostFeedback(Current.SessionState),
        };
        Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
    }

    private LiveRenderSessionState AttachHostFeedback(LiveRenderSessionState state)
    {
        OutputHostFrameFeedbackState[] feedback;
        MediaPlayerFailureState[] failures;
        lock (_hostFeedbackGate)
        {
            feedback = _hostFeedback.Values.ToArray();
            failures = _mediaPlayerFailures.Values.ToArray();
        }

        if (feedback.Length == 0 && failures.Length == 0)
            return state;

        return state with
        {
            GeneratedState = state.GeneratedState with
            {
                HostFeedback = feedback.ToDictionary(
                    item => CreateHostFeedbackKey(item.ScreenId, item.EndpointId),
                    StringComparer.OrdinalIgnoreCase),
                MediaPlayerFailures = failures.ToDictionary(
                    item => item.PlayerId,
                    StringComparer.OrdinalIgnoreCase),
            },
        };
    }

    private long? ResolveFrameSequence(string screenId)
    {
        if (Current.Frames.AudienceFrames.TryGetValue(screenId, out AudienceRenderFrame? audienceFrame))
            return audienceFrame.Sequence;

        if (Current.Frames.StageFrames.TryGetValue(screenId, out StageRenderFrame? stageFrame))
            return stageFrame.Sequence;

        return null;
    }

    private static string CreateHostFeedbackKey(string screenId, string endpointId) => $"{screenId}:{endpointId}";

    internal static (LiveRenderSessionState SessionState, RenderFrameSet Frames) BuildSessionState(
        PlaybackState playbackState,
        IOutputRoutingService routing,
        OutputTopologySnapshot topology,
        ILiveCommandExecutor commandExecutor,
        IRenderFrameResolver frameResolver,
        IStageLayoutRegistryService stageLayouts,
        LiveProductionRuntimeOverrides runtimeOverrides,
        ISlideSceneCompiler slideSceneCompiler,
        IThemeResolutionService themeResolution,
        long version)
    {
        ArgumentNullException.ThrowIfNull(playbackState);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(commandExecutor);
        ArgumentNullException.ThrowIfNull(frameResolver);
        ArgumentNullException.ThrowIfNull(stageLayouts);
        ArgumentNullException.ThrowIfNull(runtimeOverrides);
        ArgumentNullException.ThrowIfNull(slideSceneCompiler);
        ArgumentNullException.ThrowIfNull(themeResolution);

        LiveRenderSessionState sessionState = BuildBaseSessionState(playbackState, topology, stageLayouts, version);
        ActionBatch playbackBatch = BuildPlaybackBatch(playbackState, routing, commandExecutor, slideSceneCompiler, themeResolution);
        if (playbackBatch.Actions.Count > 0)
        {
            ActionResult playbackResult = commandExecutor.Execute(sessionState, playbackBatch);
            sessionState = playbackResult.State;
        }

        RenderFrameSet frames = frameResolver.Resolve(sessionState);
        foreach (ActionBatch batch in runtimeOverrides.CreateReplayBatches(commandExecutor, sessionState))
        {
            ActionResult runtimeResult = commandExecutor.Execute(sessionState, batch);
            sessionState = runtimeResult.State;
            frames = runtimeResult.Frames;
        }

        return (sessionState, frames);
    }

    private static LiveRenderSessionState BuildBaseSessionState(
        PlaybackState playbackState,
        OutputTopologySnapshot topology,
        IStageLayoutRegistryService stageLayouts,
        long version)
    {
        ArgumentNullException.ThrowIfNull(stageLayouts);

        PixelSize renderSize = ResolveRenderSize(playbackState);
        Dictionary<string, OutputScreen> screens = topology.Screens
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value with
                {
                    RenderSize = renderSize,
                },
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, StageLayout> availableStageLayouts = stageLayouts.GetLayouts();
        IReadOnlyDictionary<string, string> stageAssignments = stageLayouts.GetDefaultLayoutIdsByScreenId();
        Dictionary<string, StageScreenState> stageScreens = screens.Values
            .Where(static screen => screen.Kind == OutputScreenKind.Stage)
            .ToDictionary(
                static screen => screen.Id,
                screen => new StageScreenState
                {
                    ScreenId = screen.Id,
                    Name = screen.Name,
                    ActiveLayoutId = stageAssignments.TryGetValue(screen.Id, out string? layoutId)
                        ? layoutId
                        : null,
                },
                StringComparer.OrdinalIgnoreCase);

        return new LiveRenderSessionState
        {
            Screens = screens,
            Endpoints = topology.Endpoints,
            ScreenMappings = topology.ScreenMappings,
            StageLayouts = availableStageLayouts,
            StageLayoutIdsByScreenId = stageAssignments,
            StageScreens = stageScreens,
            StagePresentation = BuildStagePresentation(playbackState),
            Version = version,
        };
    }

    private static StagePresentationSnapshot BuildStagePresentation(PlaybackState playbackState)
    {
        PresentationProject? project = playbackState.Presentation?.Project;
        PresentationSlide? currentSlide = ResolveCurrentSlide(project, playbackState.CurrentSlideId);
        PresentationSlide? nextSlide = ResolveNextSlide(project, playbackState);

        return new StagePresentationSnapshot
        {
            CurrentSlideText = ResolveStageSlideText(currentSlide),
            NextSlideText = ResolveStageSlideText(nextSlide),
            Notes = currentSlide?.Notes,
            CurrentSlidePreview = CreateStageSlidePreview(currentSlide, "current-slide-preview"),
            NextSlidePreview = CreateStageSlidePreview(nextSlide, "next-slide-preview"),
            CurrentGroupName = ResolveStageGroupName(currentSlide),
        };
    }

    private static PresentationSlide? ResolveNextSlide(PresentationProject? project, PlaybackState playbackState)
    {
        if (project == null || project.Slides.Count == 0)
            return null;

        int currentIndex = playbackState.CurrentSlideIndex;
        if (currentIndex < 0 || currentIndex >= project.Slides.Count)
        {
            currentIndex = project.Slides.FindIndex(slide =>
                string.Equals(slide.Id, playbackState.CurrentSlideId, StringComparison.OrdinalIgnoreCase));
        }

        if (currentIndex < 0)
            return null;

        return project.Slides
            .Skip(currentIndex + 1)
            .FirstOrDefault(static slide => !slide.Disabled);
    }

    private static string? ResolveStageSlideText(PresentationSlide? slide)
    {
        if (slide == null)
            return null;

        string[] textLayers = slide.Layers
            .OfType<TextLayer>()
            .Where(static layer => layer.Visible && !string.IsNullOrWhiteSpace(layer.Content))
            .Select(static layer => layer.Content.Trim())
            .ToArray();

        return textLayers.Length > 0
            ? string.Join(Environment.NewLine, textLayers)
            : null;
    }

    private static RenderPayloadDescriptor? CreateStageSlidePreview(PresentationSlide? slide, string sourceReference)
    {
        if (slide == null)
            return null;

        return new RenderPayloadDescriptor
        {
            Id = slide.Id,
            Kind = RenderPayloadKind.Presentation,
            DisplayName = string.IsNullOrWhiteSpace(slide.SectionLabel) ? slide.Id : slide.SectionLabel,
            SourceReference = sourceReference,
        };
    }

    private static string? ResolveStageGroupName(PresentationSlide? slide)
    {
        if (slide == null)
            return null;

        if (!string.IsNullOrWhiteSpace(slide.SectionLabel))
            return slide.SectionLabel;

        return string.IsNullOrWhiteSpace(slide.Section) ? null : slide.Section;
    }

    private static ActionBatch BuildPlaybackBatch(
        PlaybackState playbackState,
        IOutputRoutingService routing,
        ILiveCommandExecutor commandExecutor,
        ISlideSceneCompiler slideSceneCompiler,
        IThemeResolutionService themeResolution)
    {
        ArgumentNullException.ThrowIfNull(commandExecutor);
        ArgumentNullException.ThrowIfNull(slideSceneCompiler);
        ArgumentNullException.ThrowIfNull(themeResolution);

        LiveCommandSource source = new() { Kind = LiveCommandSourceKind.Automation, Id = "playback-sync" };
        return commandExecutor.Expand(
            BuildPlaybackCommands(playbackState, routing, source, slideSceneCompiler, themeResolution),
            source);
    }

    private static IReadOnlyList<LiveCommand> BuildPlaybackCommands(
        PlaybackState playbackState,
        IOutputRoutingService routing,
        LiveCommandSource source,
        ISlideSceneCompiler slideSceneCompiler,
        IThemeResolutionService themeResolution)
    {
        List<LiveCommand> commands =
        [
            LiveCommandExecutor.SetLook(ConvertLook(routing.ActiveLook, routing.Feeds), source),
            .. BuildLayerCommands(playbackState, routing.ActiveLook, source, slideSceneCompiler, themeResolution),
        ];

        return commands;
    }

    private static IReadOnlyList<LiveCommand> BuildLayerCommands(
        PlaybackState playbackState,
        OutputLookDefinition activeLook,
        LiveCommandSource source,
        ISlideSceneCompiler slideSceneCompiler,
        IThemeResolutionService themeResolution)
    {
        List<LiveCommand> commands = [];
        PresentationDocument? presentation = playbackState.Presentation;
        PresentationProject? project = presentation?.Project;
        PresentationSlide? slide = ResolveCurrentSlide(project, playbackState.CurrentSlideId);
        LayerTransitionState? slideTransition = ToLayerTransitionState(
            TransitionResolver.Resolve(slide, project?.Arrangement, playbackState.GlobalSlideFallback));
        LayerTransitionState? mediaTransition = ToLayerTransitionState(
            TransitionResolver.Normalize(playbackState.GlobalMediaFallback));

        if (!string.IsNullOrWhiteSpace(playbackState.CurrentSlideId))
        {
            commands.Add(LiveCommandExecutor.SetLayerPayload(
                ResolvePresentationLayer(playbackState.PresentationLayerKind),
                BuildPresentationPayload(playbackState, project, slide, activeLook, slideSceneCompiler, themeResolution),
                source,
                slideTransition));
        }

        RenderPayloadDescriptor? mediaPayload = BuildMediaPayload(playbackState.MediaLayers);
        if (mediaPayload != null)
        {
            commands.Add(LiveCommandExecutor.SetLayerPayload(BackendOutputLayerKind.Media, mediaPayload, source, mediaTransition));
        }

        RenderPayloadDescriptor? audioPayload = BuildAudioPayload(playbackState.MediaLayers);
        if (audioPayload != null)
        {
            commands.Add(LiveCommandExecutor.SetLayerPayload(BackendOutputLayerKind.Audio, audioPayload, source, mediaTransition));
        }

        return commands;
    }

    private static BackendOutputLayerKind ResolvePresentationLayer(BackendOutputLayerKind layerKind) =>
        layerKind is BackendOutputLayerKind.Announcements
            ? BackendOutputLayerKind.Announcements
            : BackendOutputLayerKind.Slide;

    private static PixelSize ResolveRenderSize(PlaybackState playbackState)
    {
        SlideSizeDto size = PresentationModelUtilities.GetBaseSlideSize(
            playbackState.Presentation?.Project?.Manifest.AspectRatio,
            playbackState.Presentation?.Project?.Manifest.SlideSize);

        return new PixelSize(
            (int)Math.Round(Convert.ToDouble(size.Width)),
            (int)Math.Round(Convert.ToDouble(size.Height)));
    }

    private static LayerTransitionState? ToLayerTransitionState(SlideTransition? transition)
    {
        if (transition == null)
            return null;

        return new LayerTransitionState
        {
            Type = transition.Type,
            Duration = TimeSpan.FromMilliseconds(Math.Max(0, transition.Duration)),
            Phase = LayerTransitionPhase.Pending,
        };
    }

    private static RenderPayloadDescriptor BuildPresentationPayload(
        PlaybackState playbackState,
        PresentationProject? project,
        PresentationSlide? slide,
        OutputLookDefinition activeLook,
        ISlideSceneCompiler slideSceneCompiler,
        IThemeResolutionService themeResolution)
    {
        string slideId = playbackState.CurrentSlideId!;
        ThemeResolutionResult theme = themeResolution.ResolveThemeSlide(project, slide);
        SceneCompileResult? compileResult = slide == null
            ? null
            : slideSceneCompiler.Compile(new SceneCompileRequest
            {
                Project = project,
                Slide = slide,
                ThemeSlide = theme.ThemeSlide,
                ArrangementInstanceKey = playbackState.CurrentSlideInstanceKey,
                BuildIndex = playbackState.BuildIndex,
                VisibleLayerIds = playbackState.VisibleLayerIds?.ToHashSet(StringComparer.OrdinalIgnoreCase),
                Intent = RenderIntent.AudienceOutput,
            });
        IReadOnlyDictionary<string, SlideScene> variantScenes = CompileThemeVariantScenes(
            project,
            slide,
            activeLook,
            playbackState,
            slideSceneCompiler,
            themeResolution);

        return new RenderPayloadDescriptor
        {
            Id = slideId,
            Kind = RenderPayloadKind.Presentation,
            DisplayName = FirstNonWhiteSpace(slide?.SectionLabel, slide?.Id, slideId),
            SourceReference = slideId,
            Detail = new PresentationRenderPayload
            {
                PresentationId = project?.Manifest.PresentationId,
                PresentationPath = project?.SourcePath ?? playbackState.PresentationPath,
                SlideId = slideId,
                ArrangementInstanceKey = playbackState.CurrentSlideInstanceKey,
                Scene = compileResult?.Scene ?? SlideScene.Empty,
                ThemeId = theme.Binding?.ThemeId ?? project?.Manifest.ThemeId,
                VariantScenes = variantScenes,
                BuildIndex = playbackState.BuildIndex,
            },
        };
    }

    private static IReadOnlyDictionary<string, SlideScene> CompileThemeVariantScenes(
        PresentationProject? project,
        PresentationSlide? slide,
        OutputLookDefinition activeLook,
        PlaybackState playbackState,
        ISlideSceneCompiler slideSceneCompiler,
        IThemeResolutionService themeResolution)
    {
        if (project == null || slide == null)
            return new Dictionary<string, SlideScene>(StringComparer.OrdinalIgnoreCase);

        string[] variantIds = activeLook.Routes
            .Select(route => route.ResolveLayerRoute(BackendOutputLayerKind.Slide)?.ThemeVariantId)
            .Where(static variantId => !string.IsNullOrWhiteSpace(variantId))
            .Select(static variantId => variantId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Dictionary<string, SlideScene> scenes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string variantId in variantIds)
        {
            ThemeResolutionResult variantTheme = themeResolution.ResolveThemeSlide(project, slide, variantId);
            SceneCompileResult compileResult = slideSceneCompiler.Compile(new SceneCompileRequest
            {
                Project = project,
                Slide = slide,
                ThemeSlide = variantTheme.ThemeSlide,
                ThemeVariantId = variantId,
                ArrangementInstanceKey = playbackState.CurrentSlideInstanceKey,
                BuildIndex = playbackState.BuildIndex,
                VisibleLayerIds = playbackState.VisibleLayerIds?.ToHashSet(StringComparer.OrdinalIgnoreCase),
                Intent = RenderIntent.AudienceOutput,
            });
            scenes[variantId] = compileResult.Scene;
        }

        return scenes;
    }

    private static LookPreset ConvertLook(OutputLookDefinition look, IReadOnlyList<OutputFeedDefinition> feeds)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(feeds);

        HashSet<string> audienceScreenIds = feeds
            .Select(feed => feed.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new LookPreset
        {
            Id = string.IsNullOrWhiteSpace(look.Id) ? "default" : look.Id,
            Name = string.IsNullOrWhiteSpace(look.Name) ? "Default" : look.Name,
            ScreenRoutes = look.Routes
                .Where(route => audienceScreenIds.Contains(route.FeedId))
                .Select(route => new ScreenLayerRouting
                {
                    ScreenId = route.FeedId,
                    Layers = OutputRoutingDefaults.RoutableLayers
                        .Select(layerKind => new LayerRoute
                        {
                            LayerKind = layerKind,
                            IsEnabled = route.Routes(layerKind),
                            ThemeVariantId = route.ResolveLayerRoute(layerKind)?.ThemeVariantId,
                            MaskId = layerKind == BackendOutputLayerKind.Mask
                                ? route.ResolveLayerRoute(layerKind)?.MaskId
                                : null,
                        })
                        .ToArray(),
                })
                .ToArray(),
            ClearGroups = look.ClearGroups
                .Select(clearGroup => new ClearGroup
                {
                    Id = clearGroup.Id,
                    Name = clearGroup.Name,
                    Layers = OutputRoutingDefaults.ResolveClearGroupLayers(clearGroup),
                })
                .Where(static clearGroup => clearGroup.Layers.Count > 0)
                .ToArray(),
        };
    }

    private static PresentationSlide? ResolveCurrentSlide(PresentationProject? project, string? slideId)
    {
        if (project == null || string.IsNullOrWhiteSpace(slideId))
            return null;

        return project.Slides.FirstOrDefault(slide =>
            string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
    }

    private static RenderPayloadDescriptor? BuildMediaPayload(MediaLayersState mediaLayers)
    {
        OutputLayerMedia? media = mediaLayers.MediaOverlay ?? mediaLayers.MediaUnderlay;
        if (media == null || string.IsNullOrWhiteSpace(media.MediaId))
            return null;

        return new RenderPayloadDescriptor
        {
            Id = media.MediaId,
            Kind = ResolveMediaKind(media.MediaType),
            DisplayName = string.IsNullOrWhiteSpace(media.DisplayName) ? media.MediaId : media.DisplayName,
            SourceReference = mediaLayers.MediaOverlay != null ? "mediaOverlay" : "mediaUnderlay",
            Detail = new MediaRenderPayload
            {
                Media = media,
                Target = mediaLayers.MediaOverlay != null ? "mediaOverlay" : "mediaUnderlay",
            },
        };
    }

    private static RenderPayloadDescriptor? BuildAudioPayload(MediaLayersState mediaLayers)
    {
        OutputLayerMedia? audio = mediaLayers.Audio;
        if (audio == null || string.IsNullOrWhiteSpace(audio.MediaId))
            return null;

        return new RenderPayloadDescriptor
        {
            Id = audio.MediaId,
            Kind = RenderPayloadKind.Audio,
            DisplayName = string.IsNullOrWhiteSpace(audio.DisplayName) ? audio.MediaId : audio.DisplayName,
            SourceReference = "audio",
            Detail = new MediaRenderPayload
            {
                Media = audio,
                Target = "audio",
            },
        };
    }

    private static RenderPayloadKind ResolveMediaKind(string? mediaType)
    {
        return string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? RenderPayloadKind.Video
            : RenderPayloadKind.Image;
    }

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
