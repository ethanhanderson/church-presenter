using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.Rendering;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Output;

namespace ChurchPresenter.Rendering.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net80)]
public class PipelineLatencyBenchmarks
{
    private readonly PreparedCueCache _cache = new();
    private readonly FrameComposer _composer = new(new BackendRenderFrameResolver());
    private readonly LayerRouter _router;
    private LiveSessionSnapshot _snapshot = LiveSessionSnapshot.CreateEmpty();
    private CueRequest _cacheRequest = new();
    private CueRequest _cacheMissRequest = new();
    private PreparedCueRecord _cacheRecord = new();
    private PreparedCueRecord _replacementRecord = new();

    public PipelineLatencyBenchmarks()
    {
        BackendRenderFrameResolver frameResolver = new();
        BackendRenderEngine renderEngine = new(frameResolver);
        LiveCommandExecutor commandExecutor = new(renderEngine);
        _router = new LayerRouter(renderEngine, commandExecutor, new BenchmarkRoutingService(), new ShowTransitionDefaults());
    }

    [GlobalSetup]
    public void Setup()
    {
        _snapshot = LiveSessionSnapshot.CreateEmpty() with
        {
            SessionState = LiveSessionSnapshot.CreateEmpty().SessionState with
            {
                Screens = new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase)
                {
                    ["main"] = new OutputScreen
                    {
                        Id = "main",
                        Name = "Main",
                        Kind = OutputScreenKind.Audience,
                    },
                },
                ActiveLook = new LookPreset
                {
                    Id = "benchmark-look",
                    Name = "Benchmark Look",
                    ScreenRoutes =
                    [
                        new ScreenLayerRouting
                        {
                            ScreenId = "main",
                            Layers =
                            [
                                new LayerRoute { LayerKind = OutputLayerKind.Slide, IsEnabled = true },
                            ],
                        },
                    ],
                },
                Layers = new Dictionary<OutputLayerKind, LayerState>(LiveRenderSessionState.CreateEmptyLayers())
                {
                    [OutputLayerKind.Slide] = new LayerState
                    {
                        Kind = OutputLayerKind.Slide,
                        IsVisible = true,
                        Payload = new RenderPayloadDescriptor
                        {
                            Id = "benchmark-slide",
                            Kind = RenderPayloadKind.Presentation,
                            DisplayName = "Benchmark Slide",
                            Detail = new PresentationRenderPayload
                            {
                                SlideId = "benchmark-slide",
                            },
                        },
                    },
                },
            },
        };

        _cacheRequest = new CueRequest
        {
            CueKind = CueKind.Slide,
            Layer = OutputLayerKind.Slide,
            ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = "benchmark-slide" },
        };
        _cacheMissRequest = _cacheRequest with
        {
            ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = "benchmark-miss" },
        };
        _cacheRecord = new PreparedCueRecord
        {
            Layer = OutputLayerKind.Slide,
            ContentRef = _cacheRequest.ContentRef,
            Payload = new RenderPayloadDescriptor
            {
                Id = "benchmark-slide",
                Kind = RenderPayloadKind.Presentation,
            },
            SizeCostBytes = 1024,
        };
        _replacementRecord = _cacheRecord with
        {
            ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = "replacement-slide" },
            Payload = _cacheRecord.Payload with { Id = "replacement-slide" },
        };
        _cache.Set(_cacheRequest, _cacheRecord);
    }

    [Benchmark]
    public RenderFrameSet ComposeAudienceFrames() => _composer.Compose(_snapshot);

    [Benchmark]
    public bool PreparedCueCacheLookup()
    {
        return _cache.TryGet(_cacheRequest, out PreparedCueRecord _, out _);
    }

    [Benchmark]
    public bool PreparedCueCacheMiss()
    {
        return _cache.TryGet(_cacheMissRequest, out PreparedCueRecord _, out _);
    }

    [Benchmark]
    public LiveSessionSnapshot ReplaceLayerPayload()
    {
        return _router.ReplaceLayerPayload(_snapshot, OutputLayerKind.Slide, _replacementRecord);
    }

    private sealed class BenchmarkRoutingService : IOutputRoutingService
    {
        private readonly OutputLookDefinition _look = new()
        {
            Id = "benchmark-look",
            Name = "Benchmark Look",
        };

        public event EventHandler? Changed;

        public IReadOnlyList<OutputFeedDefinition> Feeds { get; } =
        [
            new OutputFeedDefinition { Id = "main", DisplayName = "Main" },
        ];

        public IReadOnlyList<OutputLookDefinition> Looks => [_look];

        public string ActiveLookId => _look.Id;

        public OutputLookDefinition ActiveLook => _look;

        public bool RoutesLayer(string feedId, OutputLayerKind layerKind) => true;

        public Task SetActiveLookAsync(string lookId, CancellationToken cancellationToken = default)
        {
            _ = lookId;
            cancellationToken.ThrowIfCancellationRequested();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task ResetToDefaultAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetLayerRoutingAsync(
            string feedId,
            OutputLayerKind layerKind,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            _ = feedId;
            _ = layerKind;
            _ = enabled;
            cancellationToken.ThrowIfCancellationRequested();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetRoutesAsync(IEnumerable<OutputLookFeedRouting> routes, CancellationToken cancellationToken = default)
        {
            _ = routes;
            cancellationToken.ThrowIfCancellationRequested();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetClearGroupsAsync(
            IEnumerable<OutputLookClearGroupDefinition> clearGroups,
            CancellationToken cancellationToken = default)
        {
            _ = clearGroups;
            cancellationToken.ThrowIfCancellationRequested();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
