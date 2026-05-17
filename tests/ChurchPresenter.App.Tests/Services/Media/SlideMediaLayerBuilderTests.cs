
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Media;

public sealed class SlideMediaLayerBuilderTests
{
    [Fact]
    public void Build_maps_legacy_cue_targets_into_current_media_layers()
    {
        var slide = new PresentationSlide
        {
            Id = "slide-1",
            MediaCues =
            [
                new SlideMediaCue
                {
                    Id = "cue-1",
                    MediaId = "media-1",
                    MediaType = "video",
                    DisplayName = "Walk In",
                    Target = "slideBackgroundMedia",
                    Loop = true,
                },
                new SlideMediaCue
                {
                    Id = "cue-2",
                    MediaId = "media-2",
                    MediaType = "image",
                    Target = "slideForegroundMedia",
                },
            ],
        };

        var state = SlideMediaLayerBuilder.Build(slide);

        state.MediaUnderlay.Should().NotBeNull();
        state.MediaUnderlay!.MediaId.Should().Be("media-1");
        state.MediaUnderlay.DisplayName.Should().Be("Walk In");
        state.MediaOverlay.Should().NotBeNull();
        state.MediaOverlay!.MediaId.Should().Be("media-2");
    }

    [Fact]
    public void Merge_overlays_slide_cues_on_existing_base_layers()
    {
        var baseState = new MediaLayersState
        {
            Audio = new OutputLayerMedia
            {
                MediaId = "audio-1",
                MediaType = "audio",
            },
        };
        var slide = new PresentationSlide
        {
            Id = "slide-1",
            MediaCues =
            [
                new SlideMediaCue
                {
                    Id = "cue-1",
                    MediaId = "underlay-1",
                    MediaType = "video",
                    Target = "mediaUnderlay",
                },
            ],
        };

        var merged = SlideMediaLayerBuilder.Merge(baseState, slide);

        merged.Audio.Should().NotBeNull();
        merged.Audio!.MediaId.Should().Be("audio-1");
        merged.MediaUnderlay.Should().NotBeNull();
        merged.MediaUnderlay!.MediaId.Should().Be("underlay-1");
    }

    [Fact]
    public void Overlay_replaces_only_non_null_layers_from_the_overlay_state()
    {
        var baseState = new MediaLayersState
        {
            MediaUnderlay = new OutputLayerMedia
            {
                MediaId = "base-underlay",
                MediaType = "image",
            },
            Audio = new OutputLayerMedia
            {
                MediaId = "base-audio",
                MediaType = "audio",
            },
        };
        var overlay = new MediaLayersState
        {
            MediaOverlay = new OutputLayerMedia
            {
                MediaId = "overlay-video",
                MediaType = "video",
            },
        };

        var merged = SlideMediaLayerBuilder.Overlay(baseState, overlay);

        merged.MediaUnderlay.Should().NotBeNull();
        merged.MediaUnderlay!.MediaId.Should().Be("base-underlay");
        merged.MediaOverlay.Should().NotBeNull();
        merged.MediaOverlay!.MediaId.Should().Be("overlay-video");
        merged.Audio.Should().NotBeNull();
        merged.Audio!.MediaId.Should().Be("base-audio");
    }
}