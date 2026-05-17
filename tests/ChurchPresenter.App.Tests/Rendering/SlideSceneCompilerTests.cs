using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Rendering;

public sealed class SlideSceneCompilerTests
{
    [Fact]
    public void Compile_creates_host_neutral_nodes_for_current_slide_layers()
    {
        PresentationProject project = CreateProject(CreateMixedSlide(10));
        SlideSceneCompiler compiler = new();

        SceneCompileResult result = compiler.Compile(new SceneCompileRequest
        {
            Project = project,
            Slide = project.Slides[0],
            RuntimeTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timer.main"] = "05:00",
            },
        });

        result.Diagnostics.Errors.Should().BeEmpty();
        result.Scene.Nodes.Should().HaveCount(10);
        result.Scene.Nodes.OfType<TextSceneNode>().Should().Contain(text => text.Text.Contains("05:00", StringComparison.Ordinal));
        result.Scene.Nodes.OfType<ShapeSceneNode>().Should().NotBeEmpty();
        result.Scene.Nodes.OfType<MediaSceneNode>().Should().NotBeEmpty();
        result.Scene.Dependencies.Should().Contain(dependency => dependency.Kind == SceneDependencyKind.Media);
    }

    [Fact]
    public void Compile_preserves_transparent_background_as_alpha_scene()
    {
        PresentationProject project = CreateProject(new PresentationSlide
        {
            Id = "slide-transparent",
            Background = new TransparentSlideBackground(),
            Layers =
            [
                CreateTextLayer("text-1", "Lower third"),
            ],
        });
        SlideSceneCompiler compiler = new();

        SlideScene scene = compiler.Compile(new SceneCompileRequest
        {
            Project = project,
            Slide = project.Slides[0],
        }).Scene;

        scene.Background.AlphaMode.Should().Be(RenderAlphaMode.StraightAlpha);
    }

    [Fact]
    public void Compile_is_deterministic_for_unchanged_content()
    {
        PresentationProject project = CreateProject(CreateMixedSlide(25));
        SlideSceneCompiler compiler = new();
        SceneCompileRequest request = new()
        {
            Project = project,
            Slide = project.Slides[0],
        };

        SlideScene first = compiler.Compile(request).Scene;
        SlideScene second = compiler.Compile(request).Scene;

        second.Id.Should().Be(first.Id);
        second.Version.Should().Be(first.Version);
    }

    [Fact]
    public void Compile_binds_theme_text_placeholder_to_slide_raw_text()
    {
        PresentationSlide slide = new()
        {
            Id = "slide-themed",
            Section = "verse",
            TextBlocks = [PresentationModelUtilities.CreateTextBlock("Amazing grace", "verse")],
        };
        PresentationProject project = CreateProject(slide);
        ThemeTemplateSlide themeSlide = new()
        {
            Id = "theme-verse",
            LayoutType = "verse",
            Layers =
            [
                new TextLayer
                {
                    Id = "theme-text",
                    Name = "Lyrics",
                    Content = "Placeholder",
                    TextBinding = new ThemeTextBinding { Role = "verse" },
                    Transform = new LayerTransformModel
                    {
                        X = 100,
                        Y = 100,
                        Width = 1000,
                        Height = 200,
                        Opacity = 1,
                    },
                    Style = PresentationModelUtilities.CreateDefaultTextStyle(),
                },
            ],
        };

        SlideScene scene = new SlideSceneCompiler().Compile(new SceneCompileRequest
        {
            Project = project,
            Slide = slide,
            ThemeSlide = themeSlide,
        }).Scene;

        scene.Nodes.OfType<TextSceneNode>().Should().ContainSingle(node =>
            node.Text == "Amazing grace" &&
            node.SourceReference == "themeLayer:theme-text");
    }

    [Fact]
    public void Compile_attaches_presentation_dependency_stamp_and_failure()
    {
        PresentationProject project = CreateProject(CreateMixedSlide(1));
        SlideSceneCompiler compiler = new();
        ContentAccessFailure failure = new()
        {
            Kind = ContentAccessFailureKind.Outdated,
            Path = project.SourcePath,
            Message = "Presentation changed after cue preparation.",
        };
        ContentResourceStamp stamp = new()
        {
            Path = project.SourcePath,
            Exists = true,
            Failure = failure,
        };

        SlideScene scene = compiler.Compile(new SceneCompileRequest
        {
            Project = project,
            Slide = project.Slides[0],
            DependencyStamps = new Dictionary<string, ContentResourceStamp>(StringComparer.OrdinalIgnoreCase)
            {
                [project.SourcePath] = stamp,
            },
        }).Scene;

        scene.Dependencies.Should().ContainSingle(dependency =>
            dependency.Kind == SceneDependencyKind.Presentation &&
            dependency.Stamp == stamp &&
            dependency.FailureKind == ContentAccessFailureKind.Outdated);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void Compile_records_performance_metrics_for_high_layer_counts(int layerCount)
    {
        PresentationProject project = CreateProject(CreateMixedSlide(layerCount));
        SlideSceneCompiler compiler = new();

        SceneCompileResult result = compiler.Compile(new SceneCompileRequest
        {
            Project = project,
            Slide = project.Slides[0],
            Intent = RenderIntent.Benchmark,
        });

        result.Scene.Nodes.Should().HaveCount(layerCount);
        result.Diagnostics.NodeCount.Should().Be(layerCount);
        result.Diagnostics.Performance.NodeCount.Should().Be(layerCount);
        result.Diagnostics.CompileDuration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    private static PresentationProject CreateProject(PresentationSlide slide)
    {
        return new PresentationProject
        {
            SourcePath = "C:/content/Presentations/test.cpres",
            Manifest = new PresentationManifest
            {
                PresentationId = "presentation-1",
                Title = "Test Presentation",
                UpdatedAt = "2026-05-04T00:00:00Z",
                SlideSize = new SlideSizeDto { Width = 1920, Height = 1080 },
            },
            Slides = [slide],
        };
    }

    private static PresentationSlide CreateMixedSlide(int layerCount)
    {
        PresentationSlide slide = new()
        {
            Id = "slide-1",
            UpdatedAt = "2026-05-04T00:00:00Z",
            Background = new SolidSlideBackground { Color = "#000000" },
        };

        for (int index = 0; index < layerCount; index++)
        {
            slide.Layers.Add((index % 3) switch
            {
                0 => CreateTextLayer($"text-{index}", $"Layer {index} {{{{timer.main}}}}"),
                1 => CreateShapeLayer($"shape-{index}"),
                _ => CreateMediaLayer($"media-{index}"),
            });
        }

        return slide;
    }

    private static TextLayer CreateTextLayer(string id, string text)
    {
        return new TextLayer
        {
            Id = id,
            Name = "Text",
            Content = text,
            Transform = new LayerTransformModel
            {
                X = 100,
                Y = 100,
                Width = 800,
                Height = 120,
                Opacity = 1,
            },
            Style = new TextStyleModel
            {
                Font = new TextFontModel { Family = "Segoe UI", Size = 72, Weight = 700 },
                Color = "#FFFFFF",
            },
        };
    }

    private static ShapeLayer CreateShapeLayer(string id)
    {
        return new ShapeLayer
        {
            Id = id,
            Name = "Shape",
            ShapeType = "rectangle",
            Transform = new LayerTransformModel
            {
                X = 120,
                Y = 240,
                Width = 640,
                Height = 160,
                Opacity = 0.9,
            },
            Style = new ShapeStyleModel
            {
                Fill = "#1D4ED8",
                Stroke = "#FFFFFF",
                StrokeWidth = 2,
            },
        };
    }

    private static MediaLayer CreateMediaLayer(string id)
    {
        return new MediaLayer
        {
            Id = id,
            Name = "Media",
            MediaId = $"asset-{id}",
            MediaType = "image",
            Fit = "cover",
            Transform = new LayerTransformModel
            {
                X = 240,
                Y = 360,
                Width = 640,
                Height = 360,
                Opacity = 1,
            },
        };
    }
}
