using System.Text.Json;


using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Runtime;

public sealed class LiveSessionServiceParityTests
{
    private static PresentationDocument MinimalDoc()
    {
        var layers = JsonSerializer.SerializeToElement(Array.Empty<object>());
        return new PresentationDocument
        {
            SourcePath = @"C:\t\a.cpres",
            Manifest = new PresentationManifestDto { Title = "T", PresentationId = "p1" },
            Slides = new List<SlideDto>
            {
                new() { Id = "s1", Type = "blank", Layers = layers },
                new() { Id = "s2", Type = "blank", Layers = layers },
            },
        };
    }

    [Fact]
    public void GoLive_and_GoToSlide_updates_current_slide()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        var doc = MinimalDoc();
        svc.GoLive(doc, doc.SourcePath);
        svc.CurrentSlideId.Should().BeNull();
        svc.GoToSlide("s1");
        svc.CurrentSlideId.Should().Be("s1");
        svc.GoToSlide("s2");
        svc.CurrentSlideId.Should().Be("s2");
    }

    [Fact]
    public void ClearPresentation_and_undo_restores_slide()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        svc.GoLive(MinimalDoc(), null);
        svc.GoToSlide("s1");
        svc.ClearPresentation();
        svc.FinishClearPresentation();
        svc.CurrentSlideId.Should().Be("s1", "slide clear hides output only; program slide stays cued");
        svc.Suppress.Presentation.Should().BeTrue();
        svc.UndoClearPresentation();
        svc.CurrentSlideId.Should().Be("s1");
        svc.Suppress.Presentation.Should().BeFalse();
    }

    [Fact]
    public void ClearPresentation_sets_isClearing_before_finish()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        svc.GoLive(MinimalDoc(), null);
        svc.GoToSlide("s1");

        svc.ClearPresentation();

        svc.IsClearing.Presentation.Should().BeTrue();
        svc.Suppress.Presentation.Should().BeFalse();
    }

    [Fact]
    public void ClearMedia_and_undo_restores_layers()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        svc.GoLive(MinimalDoc(), null);
        svc.MediaLayers.MediaUnderlay = new OutputLayerMedia { MediaId = "m1", MediaType = "image" };
        svc.ClearMedia();
        svc.FinishClearMedia();
        svc.MediaLayers.MediaUnderlay.Should().BeNull();
        svc.Suppress.Media.Should().BeFalse("media clear restores slide-only layers, not blank suppress");
        svc.UndoClearMedia();
        svc.MediaLayers.MediaUnderlay.Should().NotBeNull();
    }

    [Fact]
    public void ClearMedia_noop_when_no_media_layers()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        svc.GoLive(MinimalDoc(), null);
        svc.Suppress.Media.Should().BeFalse();
        svc.ClearMedia();
        svc.Suppress.Media.Should().BeFalse();
        svc.IsClearing.Media.Should().BeFalse();
    }
}