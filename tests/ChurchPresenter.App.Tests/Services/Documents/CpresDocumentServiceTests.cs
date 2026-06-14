using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Documents;

/// <summary><c>.cpres</c> open/save surface delegates to <see cref="ChurchPresenter.Core.Cpres"/> bundle I/O.</summary>
public sealed class CpresDocumentServiceTests
{
    [Fact]
    public void Open_and_Save_delegate_to_core_bundle_io()
    {
        var dir = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "doc.cpres");

        var state = new BundleSaveState
        {
            ManifestJson = """{"formatVersion":"1.0.0","presentationId":"pid","title":"T","createdAt":"2025-01-01T00:00:00.000Z","updatedAt":"2025-01-01T00:00:00.000Z","media":[],"fonts":[]}""",
            SlidesJson = """[{"id":"a"}]""",
            ArrangementJson = """{"order":["a"],"sections":[]}""",
            Themes = Array.Empty<ThemeFileEntry>(),
            Media = Array.Empty<MediaFileRef>(),
            Fonts = Array.Empty<FontFileRef>(),
        };

        var svc = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        svc.Save(path, state);

        var parsed = svc.Open(path);
        parsed.ManifestJson.Should().Be(state.ManifestJson);
        parsed.SlidesJson.Should().Be(state.SlidesJson);
        parsed.ArrangementJson.Should().Be(state.ArrangementJson);
    }

    [Fact]
    public void Save_throws_when_state_null()
    {
        var svc = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var act = () => svc.Save(Path.GetTempFileName(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_rewrites_bundle_mode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sourcePath = Path.Combine(dir, "source.cpres");
        var destinationPath = Path.Combine(dir, "document.cpres");
        var mediaSourcePath = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(mediaSourcePath, "video");

        var state = new BundleSaveState
        {
            ManifestJson =
                """
                {"formatVersion":"1.0.0","presentationId":"pid","title":"T","createdAt":"2025-01-01T00:00:00.000Z","updatedAt":"2025-01-01T00:00:00.000Z","bundleMode":"bundle","media":[{"id":"media-1","path":"media/clip.mp4"}],"fonts":[]}
                """,
            SlidesJson = """[]""",
            ArrangementJson = """{"order":[],"sections":[]}""",
            Themes = Array.Empty<ThemeFileEntry>(),
            Media = [new MediaFileRef { Id = "media-1", SourcePath = mediaSourcePath, BundlePath = "media/clip.mp4" }],
            Fonts = Array.Empty<FontFileRef>(),
        };

        var svc = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        svc.Save(sourcePath, state);
        svc.Export(sourcePath, destinationPath, new CpresBundleExportOptions { IncludeMedia = false, IncludeFonts = false });

        CpresBundleReader.Open(destinationPath).ManifestJson.Should().Contain("\"bundleMode\":\"document\"");
    }
}