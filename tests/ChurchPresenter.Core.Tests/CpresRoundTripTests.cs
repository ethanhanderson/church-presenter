using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Xunit;

namespace ChurchPresenter.Core.Tests;

/// <summary>
/// Validates <c>.cpres</c> bundle round-trip and error handling (inline/temp paths only).
/// </summary>
public class CpresRoundTripTests
{
    [Fact]
    public void Save_and_open_round_trip_preserves_json_entries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.cpres");

        var state = new BundleSaveState
        {
            ManifestJson = """{"formatVersion":"1.0.0","presentationId":"pid","title":"T","createdAt":"2025-01-01T00:00:00.000Z","updatedAt":"2025-01-01T00:00:00.000Z","media":[],"fonts":[]}""",
            SlidesJson = """[]""",
            ArrangementJson = """{"order":[],"sections":[]}""",
            Themes = Array.Empty<ThemeFileEntry>(),
            Media = Array.Empty<MediaFileRef>(),
            Fonts = Array.Empty<FontFileRef>(),
        };

        CpresBundleWriter.Save(path, state);
        var parsed = CpresBundleReader.Open(path);

        parsed.ManifestJson.Should().Be(state.ManifestJson);
        parsed.SlidesJson.Should().Be(state.SlidesJson);
        parsed.ArrangementJson.Should().Be(state.ArrangementJson);
    }

    [Fact]
    public void Open_throws_CpresException_when_path_empty()
    {
        var act = () => CpresBundleReader.Open("   ");
        act.Should().Throw<CpresException>();
    }

    [Fact]
    public void Export_document_mode_strips_media_and_sets_bundleMode()
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

        CpresBundleWriter.Save(sourcePath, state);
        CpresBundleExporter.Export(
            sourcePath,
            destinationPath,
            new CpresBundleExportOptions { IncludeMedia = false, IncludeFonts = false });

        using var archive = System.IO.Compression.ZipFile.OpenRead(destinationPath);
        archive.GetEntry("media/clip.mp4").Should().BeNull();
        var parsed = CpresBundleReader.Open(destinationPath);
        parsed.ManifestJson.Should().Contain("\"bundleMode\":\"document\"");
        parsed.ManifestJson.Should().Contain("\"path\":\"media/clip.mp4\"");
    }
}
