using System.Text.Json;
using System.Text.Json.Serialization;

using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class AppDataInitializerTests
{
    /// <summary>Matches <see cref="ContentBootstrapService"/> JSON options so migration reads the same shape tests write.</summary>
    private static JsonSerializerOptions MigrationJsonOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

    [Fact]
    public async Task InitializeAsync_migrates_legacy_aggregate_catalog_into_library_and_playlist_registries()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var jsonOptions = MigrationJsonOptions();

        await File.WriteAllTextAsync(
            paths.Object.GetLibrariesJsonPath(),
            JsonSerializer.Serialize(
                new[]
                {
                    new LibraryDto
                    {
                        Id = "library-1",
                        Name = "Songs",
                        Presentations = new List<PresentationRefDto>
                        {
                            new() { Path = "Presentations/alpha.cpres", Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                        },
                    },
                },
                jsonOptions));

        await File.WriteAllTextAsync(
            paths.Object.GetPlaylistsJsonPath(),
            JsonSerializer.Serialize(
                new[]
                {
                    new PlaylistDto
                    {
                        Id = "playlist-1",
                        Name = "Sunday",
                        Items = new List<PresentationRefDto>
                        {
                            new() { Path = "Presentations/alpha.cpres", Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                        },
                    },
                },
                jsonOptions));

        JsonSerializer.Deserialize<List<LibraryDto>>(
                await File.ReadAllTextAsync(paths.Object.GetLibrariesJsonPath()),
                MigrationJsonOptions())
            .Should()
            .NotBeNull()
            .And.Subject.Should().ContainSingle();
        JsonSerializer.Deserialize<List<PlaylistDto>>(
                await File.ReadAllTextAsync(paths.Object.GetPlaylistsJsonPath()),
                MigrationJsonOptions())
            .Should()
            .NotBeNull()
            .And.Subject.Should().ContainSingle();

        var initializer = TestServiceFactory.CreateAppDataInitializer(paths.Object);

        await initializer.InitializeAsync();

        File.Exists(paths.Object.GetMigrationLastRunPath()).Should().BeTrue();
        File.ReadAllText(paths.Object.GetMigrationLastRunPath()).Should().Contain("\"succeeded\": true");

        File.Exists(paths.Object.GetLibrariesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetPlaylistsIndexPath()).Should().BeTrue();
        File.ReadAllText(paths.Object.GetLibraryManifestPath("library-1")).Should().Contain("Songs");
        File.ReadAllText(paths.Object.GetPlaylistManifestPath("playlist-1")).Should().Contain("Sunday");
        File.Exists(paths.Object.GetLibrariesJsonPath()).Should().BeFalse();
        File.Exists(paths.Object.GetPlaylistsJsonPath()).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_migrates_legacy_appdata_catalog_when_aggregate_files_are_missing()
    {
        var docRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var appData = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(docRoot, appData);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var jsonOptions = MigrationJsonOptions();
        var legacy = new CatalogDto
        {
            Libraries =
            [
                new LibraryDto { Id = "legacy-lib", Name = "From Legacy", Presentations = [] },
            ],
            Playlists = [],
        };
        var catalogPath = Path.Combine(appData, "catalog.json");
        await File.WriteAllTextAsync(
            catalogPath,
            JsonSerializer.Serialize(legacy, jsonOptions));

        JsonSerializer.Deserialize<CatalogDto>(await File.ReadAllTextAsync(catalogPath), jsonOptions)
            .Should().NotBeNull();

        var initializer = TestServiceFactory.CreateAppDataInitializer(paths.Object);

        await initializer.InitializeAsync();

        File.Exists(paths.Object.GetLibrariesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetLibraryManifestPath("legacy-lib")).Should().BeTrue();
        File.ReadAllText(paths.Object.GetLibraryManifestPath("legacy-lib")).Should().Contain("From Legacy");
    }

    [Fact]
    public async Task InitializeAsync_leaves_empty_content_directory_without_catalog_files()
    {
        var docRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(docRoot);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var initializer = TestServiceFactory.CreateAppDataInitializer(paths.Object);

        await initializer.InitializeAsync();

        File.Exists(paths.Object.GetLibrariesJsonPath()).Should().BeFalse();
        File.Exists(paths.Object.GetPlaylistsJsonPath()).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_updates_root_manifest_schema_and_last_migrated_timestamp()
    {
        var docRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(docRoot);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var initializer = TestServiceFactory.CreateAppDataInitializer(paths.Object);

        await initializer.InitializeAsync();

        var manifest = JsonSerializer.Deserialize<ContentRootManifest>(
            await File.ReadAllTextAsync(paths.Object.GetContentRootManifestPath()),
            MigrationJsonOptions());

        manifest.Should().NotBeNull();
        manifest!.SchemaVersion.Should().Be(3);
        manifest.LastMigratedAt.Should().NotBeNullOrWhiteSpace();
        manifest.PortableRootKind.Should().Be("onedrive-documents");
        manifest.ResetWorkflow.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_imports_legacy_theme_file_into_canonical_theme_storage()
    {
        var docRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(docRoot);
        await paths.Object.EnsureDocumentsLayoutAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(paths.Object.GetThemesJsonPath())!);
        await File.WriteAllTextAsync(
            paths.Object.GetThemesJsonPath(),
            JsonSerializer.Serialize(
                new ThemeLibraryFile
                {
                    Themes =
                    [
                        new ThemeTemplate
                        {
                            Id = "theme-legacy",
                            Name = "Legacy Theme",
                            Slides = [],
                        },
                    ],
                },
                MigrationJsonOptions()));

        var initializer = TestServiceFactory.CreateAppDataInitializer(paths.Object);

        await initializer.InitializeAsync();

        File.Exists(paths.Object.GetThemesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetThemeFilePath("theme-legacy")).Should().BeTrue();
        File.Exists(paths.Object.GetThemesJsonPath()).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_creates_missing_content_directory_and_leaves_it_empty_when_no_catalog_exists()
    {
        var docRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(docRoot);
        Directory.Delete(docRoot, recursive: true);

        var initializer = TestServiceFactory.CreateAppDataInitializer(paths.Object);

        await initializer.InitializeAsync();

        Directory.Exists(paths.Object.GetLibrariesRootDirectory()).Should().BeTrue();
        Directory.Exists(paths.Object.GetPlaylistsRootDirectory()).Should().BeTrue();
        Directory.Exists(paths.Object.GetPresentationsRootDirectory()).Should().BeTrue();
        File.Exists(paths.Object.GetLibrariesJsonPath()).Should().BeFalse();
        File.Exists(paths.Object.GetPlaylistsJsonPath()).Should().BeFalse();
    }
}