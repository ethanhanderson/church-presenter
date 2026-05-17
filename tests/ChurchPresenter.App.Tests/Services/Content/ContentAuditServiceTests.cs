using System.Text.Json;

using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentAuditServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public async Task RunAuditAsync_builds_cleanup_graph_and_preserves_broken_references()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        Directory.CreateDirectory(paths.Object.GetManagedMediaFilesDirectory());
        await File.WriteAllTextAsync(Path.Combine(paths.Object.GetManagedMediaFilesDirectory(), "live.mp4"), "live");
        await File.WriteAllTextAsync(Path.Combine(paths.Object.GetManagedMediaFilesDirectory(), "orphan.mp4"), "orphan");

        await File.WriteAllTextAsync(
            paths.Object.GetMediaIndexPath(),
            JsonSerializer.Serialize(
                new MediaLibraryIndex
                {
                    Items =
                    [
                        new MediaLibraryItem
                        {
                            Id = "asset-live",
                            Name = "Live Asset",
                            Path = "Media/Files/live.mp4",
                            Type = "video",
                            CueDefaults = new MediaCueDefaults(),
                        },
                        new MediaLibraryItem
                        {
                            Id = "asset-orphan",
                            Name = "Orphan Asset",
                            Path = "Media/Files/orphan.mp4",
                            Type = "video",
                            CueDefaults = new MediaCueDefaults(),
                        },
                    ],
                },
                JsonOptions));

        var store = new ContentStore(NullLogger<ContentStore>.Instance);
        var themeLibrary = new ThemeLibraryService(paths.Object, store, NullLogger<ThemeLibraryService>.Instance);
        await themeLibrary.SaveAsync(
            [
                new ThemeTemplate
                {
                    Id = "theme-1",
                    Name = "Main Theme",
                    Slides =
                    [
                        new ThemeTemplateSlide
                        {
                            Id = "slide-1",
                            Name = "Theme Slide",
                            MediaCues =
                            [
                                new SlideMediaCue
                                {
                                    Id = "cue-1",
                                    MediaId = "asset-live",
                                    MediaType = "video",
                                    Target = "mediaUnderlay",
                                },
                            ],
                        },
                    ],
                },
            ]);

        var libraryRegistry = new LibraryRegistryService(paths.Object, store, NullLogger<LibraryRegistryService>.Instance);
        await libraryRegistry.SaveAsync(new LibraryManifest
        {
            Id = "library-1",
            Name = "Songs",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            Presentations =
            [
                new PresentationRefDto
                {
                    Path = "Presentations/missing.cpres",
                    Title = "Missing Song",
                    UpdatedAt = "2026-01-01T00:00:00.000Z",
                },
            ],
        });

        var audit = new ContentAuditService(
            paths.Object,
            store,
            libraryRegistry,
            new PlaylistRegistryService(paths.Object, store, NullLogger<PlaylistRegistryService>.Instance),
            new MediaLibraryService(paths.Object, NullLogger<MediaLibraryService>.Instance),
            themeLibrary,
            new PresentationProjectService(
                paths.Object,
                new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
                NullLogger<PresentationProjectService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<ContentAuditService>.Instance);

        var result = await audit.RunAuditAsync();

        result.ThemeCount.Should().Be(1);
        result.ReferenceGraphNodes.Should().ContainSingle(node => node.NodeId == "theme:theme-1:slide-1");
        result.BrokenReferences.Should().ContainSingle(reference =>
            reference.ReferenceKind == "presentation" &&
            reference.OwnerId == "library-1");
        result.CleanupCandidates.Should().ContainSingle(candidate =>
            candidate.AssetId == "asset-live" &&
            !candidate.EligibleForCleanup &&
            candidate.IsReferenced);
        result.CleanupCandidates.Should().ContainSingle(candidate =>
            candidate.AssetId == "asset-orphan" &&
            candidate.EligibleForCleanup);
        result.CleanupPreview.EligibleForCleanupCount.Should().Be(1);
        result.CleanupPreview.RequiresDestructiveConfirmation.Should().BeTrue();
        File.Exists(paths.Object.GetContentAuditPath()).Should().BeTrue();
    }
}