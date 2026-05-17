
using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

/// <summary>Parity: content root and layout folders (legacy <c>get_documents_data_dir</c> / <c>ensure_*</c>).</summary>
public sealed class ContentDirectoryServiceTests
{
    [Fact]
    public void GetAppDataDirectory_ends_with_ChurchPresenter_under_LocalAppData()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);
        var dir = svc.GetAppDataDirectory();

        dir.Should().Contain("ChurchPresenter");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dir.Should().StartWith(local);
    }

    [Fact]
    public async Task EnsureDocumentsLayoutAsync_creates_library_playlist_and_presentation_trees()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);
        var root = svc.GetDocumentsDataDirectory();

        await svc.EnsureDocumentsLayoutAsync();

        Directory.Exists(Path.Combine(root, "Libraries")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "Playlists")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "Presentations")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "Presentations", "songs")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "Themes")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "Media", "Files")).Should().BeTrue();
    }

    [Fact]
    public void ResolvePresentationPath_combines_relative_paths_with_documents_root()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);

        var resolved = svc.ResolvePresentationPath(Path.Combine("Presentations", "songs", "demo.cpres"));

        resolved.Should().Be(Path.Combine(svc.GetDocumentsDataDirectory(), "Presentations", "songs", "demo.cpres"));
    }

    [Fact]
    public void ResolvePresentationPath_preserves_absolute_paths()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);
        var absolute = Path.Combine(Path.GetTempPath(), "church-presenter-tests", "demo.cpres");

        var resolved = svc.ResolvePresentationPath(absolute);

        resolved.Should().Be(Path.GetFullPath(absolute));
    }

    [Fact]
    public void ToContentRelativePath_returns_relative_path_for_local_content_file()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);
        var localFile = Path.Combine(svc.GetDocumentsDataDirectory(), "Presentations", "demo.cpres");

        var relative = svc.ToContentRelativePath(localFile);

        relative.Should().Be("Presentations/demo.cpres");
    }

    [Fact]
    public void GetLibraryMetadataPath_builds_per_library_file_path()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);

        var path = svc.GetLibraryMetadataPath("main");

        path.Should().Be(Path.Combine(svc.GetDocumentsDataDirectory(), "libraries", "main", "library.json"));
    }

    [Fact]
    public void GetThemeFilePath_builds_canonical_theme_manifest_path()
    {
        var svc = new ContentDirectoryService(NullLogger<ContentDirectoryService>.Instance);

        var path = svc.GetThemeFilePath("theme-1");

        path.Should().Be(Path.Combine(svc.GetDocumentsDataDirectory(), "Themes", "theme-1.json"));
    }
}