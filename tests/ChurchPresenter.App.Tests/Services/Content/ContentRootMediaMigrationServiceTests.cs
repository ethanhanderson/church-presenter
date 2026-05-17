using System.Text.Json;

using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentRootMediaMigrationServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public async Task RunAsync_bulk_rewrites_media_index_themes_and_referenced_presentations()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var mediaFilePath = Path.Combine(paths.Object.GetManagedMediaFilesDirectory(), "walkin.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaFilePath)!);
        await File.WriteAllTextAsync(mediaFilePath, "media");

        var mediaIndex = new MediaLibraryIndex
        {
            Items =
            [
                new MediaLibraryItem
                {
                    Id = "media-1",
                    Name = "Walk In",
                    Path = "Media/Files/walkin.mp4",
                    Type = "video",
                    CueDefaults = new MediaCueDefaults
                    {
                        Target = "slideBackgroundMedia",
                        Transition = new SlideTransition { Type = "Fade", Duration = 0 },
                    },
                },
            ],
        };

        await WriteJsonAsync(paths.Object.GetMediaIndexPath(), mediaIndex);

        var themeLibrary = new ThemeLibraryService(paths.Object, NullLogger<ThemeLibraryService>.Instance);
        await themeLibrary.SaveAsync(
            [
                new ThemeTemplate
                {
                    Id = "theme-1",
                    Name = "Song Theme",
                    Slides =
                    [
                        new ThemeTemplateSlide
                        {
                            Id = "theme-slide-1",
                            Name = "Verse",
                            MediaCues =
                            [
                                new SlideMediaCue
                                {
                                    Id = "theme-cue-1",
                                    MediaId = "Media/Files/walkin.mp4",
                                    MediaType = "video",
                                    Target = "slideBackgroundMedia",
                                },
                            ],
                        },
                    ],
                },
            ]);

        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        await libraryRegistry.SaveAsync(new LibraryManifest
        {
            Id = "library-1",
            Name = "Songs",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            Presentations =
            [
                new PresentationRefDto
                {
                    Path = @"Presentations\song-a.cpres",
                    Title = "Song A",
                    UpdatedAt = "2026-01-01T00:00:00.000Z",
                },
            ],
        });

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var projectService = new PresentationProjectService(
            paths.Object,
            cpres,
            NullLogger<PresentationProjectService>.Instance);

        projectService.Save(
            new PresentationProject
            {
                Manifest = new PresentationManifest
                {
                    PresentationId = "song-a",
                    Title = "Song A",
                    CreatedAt = "2026-01-01T00:00:00.000Z",
                    UpdatedAt = "2026-01-01T00:00:00.000Z",
                },
                Slides =
                [
                    new PresentationSlide
                    {
                        Id = "slide-1",
                        MediaCues =
                        [
                            new SlideMediaCue
                            {
                                Id = "cue-1",
                                MediaId = "Media/Files/walkin.mp4",
                                MediaType = "video",
                                Target = "slideBackgroundMedia",
                            },
                        ],
                    },
                ],
                Arrangement = new PresentationArrangement
                {
                    Order = ["slide-1"],
                },
            },
            "Presentations/song-a.cpres");

        var service = CreateService(paths.Object);

        var result = await service.RunAsync();

        result.Issues.Should().BeEmpty();
        result.RewrittenPaths.Should().Contain(path => path.EndsWith("Media/Index.json", StringComparison.OrdinalIgnoreCase));
        result.RewrittenPaths.Should().Contain(path => path.EndsWith("Themes/Index.json", StringComparison.OrdinalIgnoreCase));
        result.RewrittenPaths.Should().Contain(path => path.EndsWith("Themes/theme-1.json", StringComparison.OrdinalIgnoreCase));
        result.RewrittenPaths.Should().Contain(path => path.EndsWith("Presentations/song-a.cpres", StringComparison.OrdinalIgnoreCase));

        var rewrittenIndex = await ReadJsonAsync<MediaLibraryIndex>(paths.Object.GetMediaIndexPath());
        rewrittenIndex.Items.Should().ContainSingle();
        rewrittenIndex.Items[0].CueDefaults.Target.Should().Be("mediaUnderlay");
        rewrittenIndex.Items[0].CueDefaults.Transition.Should().NotBeNull();
        rewrittenIndex.Items[0].CueDefaults.Transition!.Type.Should().Be("fade");

        var rewrittenTheme = (await themeLibrary.LoadAsync()).Single();
        rewrittenTheme.Slides.Single().MediaCues!.Single().DisplayName.Should().Be("Walk In");
        rewrittenTheme.Slides.Single().MediaCues!.Single().Target.Should().Be("mediaUnderlay");

        var reopenedProject = projectService.Open("Presentations/song-a.cpres");
        reopenedProject.Slides.Single().MediaCues.Single().DisplayName.Should().Be("Walk In");
        reopenedProject.Slides.Single().MediaCues.Single().Target.Should().Be("mediaUnderlay");

        var rewrittenLibrary = await libraryRegistry.LoadAsync("library-1");
        rewrittenLibrary.Should().NotBeNull();
        rewrittenLibrary!.Presentations.Single().Path.Should().Be("Presentations/song-a.cpres");
    }

    [Fact]
    public async Task RunAsync_reports_missing_presentations_and_unresolved_cue_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        await libraryRegistry.SaveAsync(new LibraryManifest
        {
            Id = "library-1",
            Name = "Songs",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            Presentations =
            [
                new PresentationRefDto
                {
                    Path = "Presentations/existing.cpres",
                    Title = "Existing",
                    UpdatedAt = "2026-01-01T00:00:00.000Z",
                },
                new PresentationRefDto
                {
                    Path = "Presentations/missing.cpres",
                    Title = "Missing",
                    UpdatedAt = "2026-01-01T00:00:00.000Z",
                },
            ],
        });

        var projectService = new PresentationProjectService(
            paths.Object,
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            NullLogger<PresentationProjectService>.Instance);

        projectService.Save(
            new PresentationProject
            {
                Manifest = new PresentationManifest
                {
                    PresentationId = "existing",
                    Title = "Existing",
                    CreatedAt = "2026-01-01T00:00:00.000Z",
                    UpdatedAt = "2026-01-01T00:00:00.000Z",
                },
                Slides =
                [
                    new PresentationSlide
                    {
                        Id = "slide-1",
                        MediaCues =
                        [
                            new SlideMediaCue
                            {
                                Id = "cue-1",
                                MediaId = "Media/Files/unknown.mp4",
                                MediaType = "video",
                                Target = "mediaUnderlay",
                            },
                        ],
                    },
                ],
                Arrangement = new PresentationArrangement
                {
                    Order = ["slide-1"],
                },
            },
            "Presentations/existing.cpres");

        var service = CreateService(paths.Object);

        var result = await service.RunAsync();

        result.Issues.Select(issue => issue.Code).Should().Contain("presentation-missing");
        result.Issues.Select(issue => issue.Code).Should().Contain("cue-display-name-unresolved");
        result.Issues.Select(issue => issue.Code).Should().Contain("cue-media-reference-missing");
    }

    private static ContentRootMediaMigrationService CreateService(IContentDirectoryService paths)
    {
        var libraryRegistry = new LibraryRegistryService(paths, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths, NullLogger<PlaylistRegistryService>.Instance);
        var projectService = new PresentationProjectService(
            paths,
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            NullLogger<PresentationProjectService>.Instance);
        var themeLibrary = new ThemeLibraryService(paths, NullLogger<ThemeLibraryService>.Instance);

        return new ContentRootMediaMigrationService(
            paths,
            libraryRegistry,
            playlistRegistry,
            projectService,
            themeLibrary,
            NullLogger<ContentRootMediaMigrationService>.Instance);
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }
}