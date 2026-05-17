using System.Text.Json;


using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Media;

/// <summary>
/// Regression tests for <see cref="CuePreparationService"/>.
/// </summary>
public sealed class CuePreparationServiceTests
{
    [Fact]
    public async Task PrepareSlideCueAsync_caches_the_prepared_slide_and_precomputes_slide_media_layers()
    {
        var path = @"C:\content\presentation.cpres";
        var document = new PresentationDocument
        {
            SourcePath = path,
            Manifest = new PresentationManifestDto { Title = "Test", PresentationId = "p1" },
            Slides =
            [
                new SlideDto
                {
                    Id = "s1",
                    Type = "blank",
                    Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()),
                    MediaCues =
                    [
                        new SlideMediaCueDto
                        {
                            Id = "cue-1",
                            MediaId = "bg.png",
                            MediaType = "image",
                            Target = "mediaUnderlay",
                        },
                    ],
                },
            ],
        };

        var cache = new Mock<IShowSessionCache>();
        cache.Setup(x => x.TryGet(path)).Returns(document);
        cache.Setup(x => x.GetOrLoad(path)).Returns(document);

        var service = new CuePreparationService(
            cache.Object,
            Mock.Of<IPresentationDocumentService>(),
            Mock.Of<IMediaLibraryService>());

        var prepared = await service.PrepareSlideCueAsync(path, "s1");
        var cached = service.GetPreparedSlideCue(path, "s1");

        prepared.Should().NotBeNull();
        prepared!.Presentation.Should().BeSameAs(document);
        prepared.SlideId.Should().Be("s1");
        prepared.SlideIndex.Should().Be(0);
        prepared.MediaLayers.MediaUnderlay.Should().NotBeNull();
        prepared.MediaLayers.MediaUnderlay!.MediaId.Should().Be("bg.png");
        cached.Should().BeSameAs(prepared);
    }

    [Fact]
    public async Task GetPreparedSlideCue_returns_null_when_the_cached_document_has_been_replaced()
    {
        var path = @"C:\content\presentation.cpres";
        var documentA = CreateDocument(path, "bg-a.png");
        var documentB = CreateDocument(path, "bg-b.png");
        var currentDocument = documentA;

        var cache = new Mock<IShowSessionCache>();
        cache.Setup(x => x.TryGet(path)).Returns(() => currentDocument);
        cache.Setup(x => x.GetOrLoad(path)).Returns(() => currentDocument);

        var service = new CuePreparationService(
            cache.Object,
            Mock.Of<IPresentationDocumentService>(),
            Mock.Of<IMediaLibraryService>());

        var first = await service.PrepareSlideCueAsync(path, "s1");
        first.Should().NotBeNull();

        currentDocument = documentB;

        service.GetPreparedSlideCue(path, "s1").Should().BeNull("cached cue must be discarded when the backing document changes");
    }

    [Fact]
    public async Task PrepareSlideCueAsync_rebuilds_the_cached_cue_after_document_refresh()
    {
        var path = @"C:\content\presentation.cpres";
        var documentA = CreateDocument(path, "bg-a.png");
        var documentB = CreateDocument(path, "bg-b.png");
        var currentDocument = documentA;

        var cache = new Mock<IShowSessionCache>();
        cache.Setup(x => x.TryGet(path)).Returns(() => currentDocument);
        cache.Setup(x => x.GetOrLoad(path)).Returns(() => currentDocument);

        var service = new CuePreparationService(
            cache.Object,
            Mock.Of<IPresentationDocumentService>(),
            Mock.Of<IMediaLibraryService>());

        var first = await service.PrepareSlideCueAsync(path, "s1");
        currentDocument = documentB;

        var second = await service.PrepareSlideCueAsync(path, "s1");

        second.Should().NotBeNull();
        second.Should().NotBeSameAs(first);
        second!.Presentation.Should().BeSameAs(documentB);
        second.MediaLayers.MediaUnderlay.Should().NotBeNull();
        second.MediaLayers.MediaUnderlay!.MediaId.Should().Be("bg-b.png");
    }

    [Fact]
    public async Task InvalidatePresentationCues_removes_cached_slide_entries_for_the_presentation()
    {
        var path = @"C:\content\presentation.cpres";
        var document = CreateDocument(path, "bg-a.png");

        var cache = new Mock<IShowSessionCache>();
        cache.Setup(x => x.TryGet(path)).Returns(document);
        cache.Setup(x => x.GetOrLoad(path)).Returns(document);

        var service = new CuePreparationService(
            cache.Object,
            Mock.Of<IPresentationDocumentService>(),
            Mock.Of<IMediaLibraryService>());

        await service.PrepareSlideCueAsync(path, "s1");

        service.InvalidatePresentationCues(path);

        service.GetPreparedSlideCue(path, "s1").Should().BeNull();
    }

    [Fact]
    public void PrepareMediaCue_resolves_the_source_path_and_preserves_media_defaults()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, "test");
            var item = new MediaLibraryItem
            {
                Id = "m1",
                Name = "Walk In",
                Path = "Media/Files/walkin.png",
                Type = "image",
                CueDefaults = new MediaCueDefaults
                {
                    Target = "mediaOverlay",
                    Fit = "contain",
                    Autoplay = true,
                    Loop = false,
                    Muted = true,
                    Transition = new SlideTransition { Type = "fade", Duration = 250 },
                },
            };

            var mediaLibrary = new Mock<IMediaLibraryService>();
            mediaLibrary.Setup(x => x.ResolveStoredMediaPath(item.Path)).Returns(tempPath);

            var service = new CuePreparationService(
                Mock.Of<IShowSessionCache>(),
                Mock.Of<IPresentationDocumentService>(),
                mediaLibrary.Object);

            var prepared = service.PrepareMediaCue(item);

            prepared.Should().NotBeNull();
            prepared!.Target.Should().Be("mediaOverlay");
            prepared.Media.ResolvedSourcePath.Should().Be(tempPath);
            prepared.Media.DisplayName.Should().Be("Walk In");
            prepared.Media.Fit.Should().Be("contain");
            prepared.Media.Autoplay.Should().BeTrue();
            prepared.Media.Loop.Should().BeFalse();
            prepared.Media.Muted.Should().BeTrue();
            prepared.Media.Transition.Should().NotBeNull();
            prepared.Media.Transition!.Type.Should().Be("fade");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static PresentationDocument CreateDocument(string path, string mediaId) =>
        new()
        {
            SourcePath = path,
            Manifest = new PresentationManifestDto { Title = "Test", PresentationId = "p1" },
            Slides =
            [
                new SlideDto
                {
                    Id = "s1",
                    Type = "blank",
                    Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()),
                    MediaCues =
                    [
                        new SlideMediaCueDto
                        {
                            Id = "cue-1",
                            MediaId = mediaId,
                            MediaType = "image",
                            Target = "mediaUnderlay",
                        },
                    ],
                },
            ],
        };
}