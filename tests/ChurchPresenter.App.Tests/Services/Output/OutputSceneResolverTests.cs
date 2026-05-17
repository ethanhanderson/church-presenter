
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Output;

/// <summary>
/// Locks the live/snapshot output scene contract before the WinUI renderer stack is replaced.
/// </summary>
public sealed class OutputSceneResolverTests
{
    [Fact]
    public void ResolveProgram_preserves_engine_media_when_slide_has_no_replacement_cues()
    {
        var slide = new PresentationSlide
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
        };
        var project = new PresentationProject();
        project.Slides.Add(slide);
        var frame = new RenderFrame
        {
            Project = project,
            Slide = slide,
            ProgramSlideId = "s1",
            MediaLayers = new MediaLayersState
            {
                MediaUnderlay = new OutputLayerMedia
                {
                    MediaId = @"C:\media\walkin.mp4",
                    MediaType = "video",
                    DisplayName = "Walk In",
                    Loop = true,
                    Autoplay = true,
                },
            },
        };

        var scene = OutputSceneResolver.ResolveFromRenderFrame(frame);

        scene.Media.Underlay.Media.Should().NotBeNull();
        scene.Media.Underlay.Media!.MediaId.Should().Be(@"C:\media\walkin.mp4");
        scene.Media.Underlay.Media.DisplayName.Should().Be("Walk In");
        scene.Presentation.Slide.Should().BeSameAs(slide);
    }

    [Fact]
    public void ResolveProgram_extracts_web_layers_without_losing_slide_order()
    {
        var slide = new PresentationSlide
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
            Layers =
            [
                new TextLayer
                {
                    Id = "text-1",
                    Content = "Hello",
                    Transform = PresentationModelUtilities.CreateDefaultTransform(null),
                },
                new WebLayer
                {
                    Id = "web-1",
                    Url = "https://example.com",
                    Zoom = 1.25,
                    Transform = PresentationModelUtilities.CreateDefaultTransform(null),
                },
                new ShapeLayer
                {
                    Id = "shape-1",
                    Transform = PresentationModelUtilities.CreateDefaultTransform(null),
                    Style = new ShapeStyleModel(),
                },
            ],
        };
        var project = new PresentationProject();
        project.Slides.Add(slide);
        var scene = OutputSceneResolver.ResolveFromRenderFrame(new RenderFrame
        {
            Project = project,
            Slide = slide,
            ProgramSlideId = "s1",
        });

        scene.Web.Layers.Should().ContainSingle();
        scene.Web.Layers[0].Id.Should().Be("web-1");
        scene.Web.Layers[0].Layer.Url.Should().Be("https://example.com");
        scene.Presentation.Layers.Select(layer => layer.Id)
            .Should()
            .Equal("text-1", "web-1", "shape-1");
        scene.Presentation.Layers[1].Kind.Should().Be(PresentationSceneLayerKind.Web);
        scene.Presentation.Layers[1].UsesExternalContent.Should().BeTrue();
    }

    [Fact]
    public void ResolveProgram_respects_visible_layer_filter_for_builds()
    {
        var slide = new PresentationSlide
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
            Layers =
            [
                new TextLayer
                {
                    Id = "visible-layer",
                    Content = "Visible",
                    Transform = PresentationModelUtilities.CreateDefaultTransform(null),
                },
                new TextLayer
                {
                    Id = "hidden-layer",
                    Content = "Hidden",
                    Transform = PresentationModelUtilities.CreateDefaultTransform(null),
                },
            ],
        };
        var project = new PresentationProject();
        project.Slides.Add(slide);
        var scene = OutputSceneResolver.ResolveFromRenderFrame(new RenderFrame
        {
            Project = project,
            Slide = slide,
            ProgramSlideId = "s1",
            VisibleLayerIds = ["visible-layer"],
        });

        scene.Presentation.Layers.Should().ContainSingle();
        scene.Presentation.Layers[0].Id.Should().Be("visible-layer");
    }

    [Fact]
    public void ResolveProgram_preserves_transition_and_clear_flags()
    {
        var slide = new PresentationSlide
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
            Animations = new SlideAnimations
            {
                Transition = new SlideTransition { Type = "fade", Duration = 450 },
            },
        };
        var project = new PresentationProject();
        project.Arrangement = new PresentationArrangement();
        project.Slides.Add(slide);
        var frame = new RenderFrame
        {
            Project = project,
            Slide = slide,
            ProgramSlideId = "s1",
            IsClear = true,
            SuppressPresentation = true,
            Transition = slide.Animations.Transition,
            MediaTransition = new SlideTransition { Type = "fade", Duration = 300 },
        };

        var scene = OutputSceneResolver.ResolveFromRenderFrame(frame);

        scene.IsClear.Should().BeTrue();
        scene.Presentation.Suppressed.Should().BeTrue();
        scene.Transition.Should().NotBeNull();
        scene.Transition!.Type.Should().Be("fade");
        scene.MediaTransition.Should().NotBeNull();
        scene.MediaTransition!.Duration.Should().Be(300);
    }

    [Fact]
    public void ResolveProgram_maps_background_media_into_presentation_background_scene()
    {
        var slide = new PresentationSlide
        {
            Id = "s1",
            Background = new VideoSlideBackground
            {
                MediaId = "intro-loop.mp4",
                Fit = "contain",
                Loop = true,
                Muted = true,
                Opacity = 0.75,
            },
        };
        var project = new PresentationProject();
        project.Slides.Add(slide);
        var scene = OutputSceneResolver.ResolveFromRenderFrame(new RenderFrame
        {
            Project = project,
            Slide = slide,
            ProgramSlideId = "s1",
        });

        scene.Presentation.Background.Media.Should().NotBeNull();
        scene.Presentation.Background.Media!.MediaId.Should().Be("intro-loop.mp4");
        scene.Presentation.Background.Media.MediaType.Should().Be("video");
        scene.Presentation.Background.Media.Opacity.Should().Be(0.75);
    }

    [Fact]
    public void ResolveSnapshot_keeps_web_and_media_descriptors_for_static_surfaces()
    {
        var slide = new PresentationSlide
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
            Layers =
            [
                new WebLayer
                {
                    Id = "web-1",
                    Url = "https://example.com/live",
                    Zoom = 1.1,
                    Transform = PresentationModelUtilities.CreateDefaultTransform(null),
                },
            ],
            MediaCues =
            [
                new SlideMediaCue
                {
                    Id = "cue-1",
                    MediaId = "announcement.png",
                    MediaType = "image",
                    Target = "mediaUnderlay",
                },
            ],
        };
        var project = new PresentationProject();
        project.Slides.Add(slide);

        var scene = OutputSceneResolver.ResolveSnapshot(project, slide, mediaLayers: SlideMediaLayerBuilder.Build(slide));

        scene.Web.Layers.Should().ContainSingle();
        scene.Web.Layers[0].Layer.Url.Should().Be("https://example.com/live");
        scene.Media.Underlay.Media.Should().NotBeNull();
        scene.Media.Underlay.Media!.MediaId.Should().Be("announcement.png");
    }
}