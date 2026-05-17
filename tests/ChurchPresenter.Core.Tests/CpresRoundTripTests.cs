using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Xunit;

namespace ChurchPresenter.Core.Tests;

/// <summary>
/// Validates bundle I/O against shared fixtures and round-trip integrity.
/// </summary>
public class CpresRoundTripTests
{
    [Fact]
    public void Open_minimal_fixture_json_fragments_match_reader_expectations()
    {
        var root = AppContext.BaseDirectory;
        var dir = Path.Combine(root, "fixtures", "minimal-presentation");
        var manifest = File.ReadAllText(Path.Combine(dir, "manifest.json"));
        var slides = File.ReadAllText(Path.Combine(dir, "slides.json"));
        var arrangement = File.ReadAllText(Path.Combine(dir, "arrangement.json"));

        manifest.Should().Contain("formatVersion");
        slides.Should().Contain("slide-1");
        arrangement.Should().Contain("order");
    }

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
}