using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Documents;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Rendering;

[ReferenceArea(ReferenceAreas.Rendering)]
public sealed class SlideCheckerboardToneTests
{
    [Fact]
    public void UseDarkCheckerboard_TransparentSlideWithWhiteText_UsesDarkVariant()
    {
        var slide = new PresentationSlide
        {
            Background = new TransparentSlideBackground(),
            Layers =
            [
                new TextLayer
                {
                    Visible = true,
                    Content = "Hello",
                    Style = new TextStyleModel { Color = "#FFFFFF" },
                    Transform = new LayerTransformModel { Width = 1600, Height = 400 },
                },
            ],
        };

        SlideCheckerboardTone.UseDarkCheckerboard(slide).Should().BeTrue();
    }

    [Fact]
    public void UseDarkCheckerboard_TransparentSlideWithBlackText_UsesLightVariant()
    {
        var slide = new PresentationSlide
        {
            Background = new TransparentSlideBackground(),
            Layers =
            [
                new TextLayer
                {
                    Visible = true,
                    Content = "Hello",
                    Style = new TextStyleModel { Color = "#000000" },
                    Transform = new LayerTransformModel { Width = 1600, Height = 400 },
                },
            ],
        };

        SlideCheckerboardTone.UseDarkCheckerboard(slide).Should().BeFalse();
    }

    [Fact]
    public void UseDarkCheckerboard_SolidWhiteBackground_UsesDarkVariant()
    {
        var slide = new PresentationSlide
        {
            Background = new SolidSlideBackground { Color = "#FFFFFF" },
        };

        SlideCheckerboardTone.UseDarkCheckerboard(slide).Should().BeTrue();
    }

    [Fact]
    public void UseDarkCheckerboard_SolidBlackBackground_UsesLightVariant()
    {
        var slide = new PresentationSlide
        {
            Background = new SolidSlideBackground { Color = "#000000" },
        };

        SlideCheckerboardTone.UseDarkCheckerboard(slide).Should().BeFalse();
    }

    [Fact]
    public void UseDarkCheckerboard_EmptyTransparentSlide_DefaultsToLightVariant()
    {
        var slide = new PresentationSlide
        {
            Background = new TransparentSlideBackground(),
        };

        SlideCheckerboardTone.UseDarkCheckerboard(slide).Should().BeFalse();
    }

    [Fact]
    public void GetCacheKeySegment_ReflectsContentTone()
    {
        var lightTextSlide = new PresentationSlide
        {
            Background = new TransparentSlideBackground(),
            Layers =
            [
                new TextLayer
                {
                    Visible = true,
                    Content = "A",
                    Style = new TextStyleModel { Color = "#FFFFFF" },
                    Transform = new LayerTransformModel { Width = 100, Height = 100 },
                },
            ],
        };

        SlideCheckerboardTone.GetCacheKeySegment(lightTextSlide).Should().Be("cb-dark");
    }
}
