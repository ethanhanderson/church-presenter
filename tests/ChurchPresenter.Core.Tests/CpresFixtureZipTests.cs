using System.IO.Compression;
using System.Text;

using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Xunit;

namespace ChurchPresenter.Core.Tests;

/// <summary>
/// Builds a <c>.cpres</c> ZIP from the shared loose JSON fixture (portable bundle layout).
/// </summary>
public sealed class CpresFixtureZipTests
{
    [Fact]
    public void Open_zip_built_from_minimal_fixture_matches_Tauri_bundle_layout()
    {
        var root = AppContext.BaseDirectory;
        var fixtureDir = Path.Combine(root, "fixtures", "minimal-presentation");
        var manifest = File.ReadAllText(Path.Combine(fixtureDir, "manifest.json"));
        var slides = File.ReadAllText(Path.Combine(fixtureDir, "slides.json"));
        var arrangement = File.ReadAllText(Path.Combine(fixtureDir, "arrangement.json"));

        var tempDir = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cpresPath = Path.Combine(tempDir, "fixture.cpres");

        using (var fs = File.Create(cpresPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            AddText(zip, "manifest.json", manifest);
            AddText(zip, "slides.json", slides);
            AddText(zip, "arrangement.json", arrangement);
        }

        var parsed = CpresBundleReader.Open(cpresPath);

        parsed.ManifestJson.Should().Be(manifest);
        parsed.SlidesJson.Should().Be(slides);
        parsed.ArrangementJson.Should().Be(arrangement);
        parsed.Themes.Should().BeEmpty();

        manifest.Should().Contain("formatVersion");
        manifest.Should().Contain("presentationId");
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes);
    }
}