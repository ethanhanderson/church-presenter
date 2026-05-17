using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Rendering.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net80)]
public class SlideSceneCompilerBenchmarks
{
    private readonly SlideSceneCompiler _compiler = new();
    private readonly BackendRenderFrameResolver _frameResolver = new();
    private PresentationProject _project = new();
    private LiveRenderSessionState _sessionState = new();

    [Params(10, 50, 100, 250, 500, 1000)]
    public int LayerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _project = RenderingBenchmarkFixtures.CreateProject(LayerCount);
        SlideScene scene = _compiler.Compile(new SceneCompileRequest
        {
            Project = _project,
            Slide = _project.Slides[0],
            Intent = RenderIntent.Benchmark,
        }).Scene;

        _sessionState = RenderingBenchmarkFixtures.CreateLiveState(scene);
    }

    [Benchmark]
    public SlideScene CompileScene()
    {
        return _compiler.Compile(new SceneCompileRequest
        {
            Project = _project,
            Slide = _project.Slides[0],
            Intent = RenderIntent.Benchmark,
        }).Scene;
    }

    [Benchmark]
    public SlideScene CompileThumbnailScene()
    {
        return _compiler.Compile(new SceneCompileRequest
        {
            Project = _project,
            Slide = _project.Slides[0],
            Intent = RenderIntent.Thumbnail,
        }).Scene;
    }

    [Benchmark]
    public RenderFrameSet ResolveAudienceFrames()
    {
        return _frameResolver.Resolve(_sessionState);
    }
}

internal static class RenderingBenchmarkFixtures
{
    public static PresentationProject CreateProject(int layerCount)
    {
        PresentationSlide slide = new()
        {
            Id = "benchmark-slide",
            UpdatedAt = "2026-05-04T00:00:00Z",
            Background = new TransparentSlideBackground(),
        };

        for (int index = 0; index < layerCount; index++)
        {
            slide.Layers.Add((index % 4) switch
            {
                0 => CreateTextLayer(index),
                1 => CreateShapeLayer(index),
                2 => CreateMediaLayer(index),
                _ => CreateVectorLayer(index),
            });
        }

        return new PresentationProject
        {
            SourcePath = "C:/benchmarks/benchmark.cpres",
            Manifest = new PresentationManifest
            {
                PresentationId = "benchmark-presentation",
                Title = "Benchmark Presentation",
                UpdatedAt = "2026-05-04T00:00:00Z",
                SlideSize = new SlideSizeDto { Width = 1920, Height = 1080 },
            },
            Slides = [slide],
        };
    }

    public static LiveRenderSessionState CreateLiveState(SlideScene scene)
    {
        return new LiveRenderSessionState
        {
            Screens = new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase)
            {
                ["main"] = new OutputScreen
                {
                    Id = "main",
                    Name = "Main",
                    Kind = OutputScreenKind.Audience,
                },
                ["stream"] = new OutputScreen
                {
                    Id = "stream",
                    Name = "Stream",
                    Kind = OutputScreenKind.Audience,
                    AlphaMode = RenderAlphaMode.StraightAlpha,
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
                            new LayerRoute { LayerKind = OutputLayerKind.Media, IsEnabled = true },
                        ],
                    },
                    new ScreenLayerRouting
                    {
                        ScreenId = "stream",
                        Layers =
                        [
                            new LayerRoute { LayerKind = OutputLayerKind.Slide, IsEnabled = true, ThemeVariantId = "lower-third" },
                            new LayerRoute { LayerKind = OutputLayerKind.Media, IsEnabled = false },
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
                        Id = scene.SlideId ?? scene.Id,
                        Kind = RenderPayloadKind.Presentation,
                        DisplayName = "Benchmark Slide",
                        Detail = new PresentationRenderPayload
                        {
                            PresentationId = scene.PresentationId,
                            SlideId = scene.SlideId,
                            Scene = scene,
                        },
                    },
                },
                [OutputLayerKind.Media] = new LayerState
                {
                    Kind = OutputLayerKind.Media,
                    IsVisible = true,
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = "walk-in",
                        Kind = RenderPayloadKind.Video,
                        DisplayName = "Walk-in Loop",
                    },
                },
            },
            Version = 1,
        };
    }

    private static TextLayer CreateTextLayer(int index)
    {
        return new TextLayer
        {
            Id = $"text-{index}",
            Name = $"Text {index}",
            Content = $"Benchmark lyric layer {index} {{{{clock}}}}",
            Transform = CreateTransform(index, width: 900, height: 120),
            Style = new TextStyleModel
            {
                Font = new TextFontModel { Family = "Segoe UI", Size = 64, Weight = 700 },
                Color = "#FFFFFF",
            },
        };
    }

    private static ShapeLayer CreateShapeLayer(int index)
    {
        return new ShapeLayer
        {
            Id = $"shape-{index}",
            Name = $"Shape {index}",
            ShapeType = index % 8 == 0 ? "ellipse" : "rectangle",
            Transform = CreateTransform(index, width: 600, height: 180),
            Style = new ShapeStyleModel
            {
                Fill = "#1D4ED8",
                Stroke = "#FFFFFF",
                StrokeWidth = 2,
            },
        };
    }

    private static MediaLayer CreateMediaLayer(int index)
    {
        return new MediaLayer
        {
            Id = $"media-{index}",
            Name = $"Media {index}",
            MediaId = $"asset-{index}",
            MediaType = index % 6 == 0 ? "video" : "image",
            Fit = "cover",
            Transform = CreateTransform(index, width: 640, height: 360),
        };
    }

    private static VectorLayer CreateVectorLayer(int index)
    {
        return new VectorLayer
        {
            Id = $"vector-{index}",
            Name = $"Vector {index}",
            Path = "M 0 0 L 100 0 L 100 100 L 0 100 Z",
            Transform = CreateTransform(index, width: 160, height: 160),
        };
    }

    private static LayerTransformModel CreateTransform(int index, double width, double height)
    {
        return new LayerTransformModel
        {
            X = 40 + (index * 37 % 1500),
            Y = 40 + (index * 29 % 820),
            Width = width,
            Height = height,
            Rotation = index % 9 == 0 ? 5 : 0,
            Opacity = 1,
        };
    }
}
