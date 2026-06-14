using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Rendering;

/// <summary>
/// Tests for scaling slide scenes to output surfaces.
/// </summary>
[ReferenceArea(ReferenceAreas.Rendering)]
public sealed class SlideSceneFitterTests
{
    [Fact]
    public void Uniform_scales_text_metrics_for_high_dpi_output()
    {
        SlideScene scene = new()
        {
            Id = "scene:test",
            Version = "v1",
            RenderSize = PixelSize.FullHd,
            Nodes =
            [
                new TextSceneNode
                {
                    Id = "title",
                    FontSize = 72,
                    Padding = 12,
                    Transform = new SceneNodeTransform { X = 100, Y = 200, Width = 800, Height = 120 },
                },
            ],
        };

        var target = new PixelSize(3840, 2160);
        (SlideScene fitted, UniformFitMetrics metrics) = SlideSceneFitter.Uniform(scene, target);

        metrics.ContentSize.Width.Should().Be(3840);
        metrics.ContentSize.Height.Should().Be(2160);
        metrics.OffsetX.Should().Be(0);
        metrics.OffsetY.Should().Be(0);

        TextSceneNode text = fitted.Nodes.Should().ContainSingle().Which.Should().BeOfType<TextSceneNode>().Subject;
        text.FontSize.Should().Be(144);
        text.Padding.Should().Be(24);
        text.Transform.X.Should().Be(200);
        text.Transform.Y.Should().Be(400);
        text.Transform.Width.Should().Be(1600);
    }

    [Fact]
    public void Uniform_letterboxes_mismatched_aspect_ratios()
    {
        SlideScene scene = new()
        {
            Id = "scene:test",
            Version = "v1",
            RenderSize = PixelSize.FullHd,
            Nodes = [],
        };

        var target = new PixelSize(1920, 1200);
        (_, UniformFitMetrics metrics) = SlideSceneFitter.Uniform(scene, target);

        metrics.ContentSize.Width.Should().Be(1920);
        metrics.ContentSize.Height.Should().Be(1080);
        metrics.OffsetY.Should().Be(60);
        metrics.IsFullBleed.Should().BeFalse();
    }
}
