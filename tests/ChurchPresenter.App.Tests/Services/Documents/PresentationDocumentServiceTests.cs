using System.Text.Json;

using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Documents;

/// <summary>Verifies the Show-facing presentation document model can open stored catalog paths and expose slide content for display.</summary>
public sealed class PresentationDocumentServiceTests
{
    [Fact]
    public void Open_resolves_relative_paths_against_documents_directory_and_returns_display_document()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var relativePath = Path.Combine("presentations", "songs", "fixture.cpres");
        var absolutePath = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        var bundleState = new BundleSaveState
        {
            ManifestJson = """{"formatVersion":"1.0.0","presentationId":"fixture-id","title":"Fixture","createdAt":"2025-01-01T00:00:00.000Z","updatedAt":"2025-01-01T00:00:00.000Z","media":[],"fonts":[]}""",
            SlidesJson = """[{"id":"slide-1","type":"content","layers":[{"id":"layer-1","type":"text","content":"Hello church"}]}]""",
            ArrangementJson = """{"order":["slide-1"],"sections":[]}""",
            Themes = Array.Empty<ThemeFileEntry>(),
            Media = Array.Empty<MediaFileRef>(),
            Fonts = Array.Empty<FontFileRef>(),
        };

        var bundleService = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        bundleService.Save(absolutePath, bundleState);

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.ResolvePresentationPath(It.IsAny<string>()))
            .Returns<string>(path => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path)));

        var projects = new PresentationProjectService(
            paths.Object,
            bundleService,
            NullLogger<PresentationProjectService>.Instance);

        var svc = new PresentationDocumentService(
            paths.Object,
            bundleService,
            projects,
            NullLogger<PresentationDocumentService>.Instance);

        var doc = svc.Open(relativePath);

        doc.SourcePath.Should().Be(absolutePath);
        doc.Manifest.Title.Should().Be("Fixture");
        doc.Slides.Should().ContainSingle();
        doc.Slides[0].Type.Should().Be("content");
        doc.Slides[0].Layers.ValueKind.Should().Be(JsonValueKind.Array);
        doc.Slides[0].Layers.EnumerateArray().Single().GetProperty("content").GetString().Should().Be("Hello church");
    }

    [Fact]
    public void Open_defaults_missing_layers_to_empty_array_for_display_bindings()
    {
        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.ResolvePresentationPath(It.IsAny<string>()))
            .Returns<string>(path => Path.GetFullPath(path));

        var cpres = new Mock<ICpresDocumentService>();
        cpres.Setup(s => s.Open(@"C:\fixture.cpres"))
            .Returns(new ParsedBundle
            {
                ManifestJson = """{"formatVersion":"1.0.0","presentationId":"fixture-id","title":"Fixture","media":[],"fonts":[]}""",
                SlidesJson = """[{"id":"slide-1","type":"blank"}]""",
                ArrangementJson = """{"order":["slide-1"],"sections":[]}""",
                Themes = Array.Empty<ThemeFileEntry>(),
            });

        var projects = new PresentationProjectService(
            paths.Object,
            cpres.Object,
            NullLogger<PresentationProjectService>.Instance);

        var svc = new PresentationDocumentService(
            paths.Object,
            cpres.Object,
            projects,
            NullLogger<PresentationDocumentService>.Instance);

        var doc = svc.Open(@"C:\fixture.cpres");

        doc.Slides.Should().ContainSingle();
        doc.Slides[0].Layers.ValueKind.Should().Be(JsonValueKind.Array);
        doc.Slides[0].Layers.GetArrayLength().Should().Be(0);
    }
}