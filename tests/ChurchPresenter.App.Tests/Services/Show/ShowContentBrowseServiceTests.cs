
using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Show;

/// <summary>
/// Covers the Application service that backs the active Show browse/go-live shell slice.
/// </summary>
public sealed class ShowContentBrowseServiceTests
{
    [Fact]
    public async Task InitializeAsync_loads_workspace_catalog_and_projects_selected_playlist()
    {
        CatalogDto catalog = new()
        {
            Libraries =
            [
                new LibraryDto
                {
                    Id = "library-1",
                    Name = "Library",
                    Presentations = [new PresentationRefDto { Path = "Presentations/song.cpres", Title = "Song" }],
                },
            ],
            Playlists =
            [
                new PlaylistDto
                {
                    Id = "playlist-1",
                    Name = "Sunday",
                    Items = [new PresentationRefDto { Path = "Presentations/service.cpres", Title = "Service Song" }],
                },
            ],
        };
        FakeWorkspaceService workspace = new()
        {
            Workspace = new WorkspaceDto
            {
                SelectedPlaylistId = "playlist-1",
                SelectedPresentationPath = "Presentations/service.cpres",
            },
        };
        PresentationDocument document = CreateDocument("C:/content/Presentations/service.cpres", "Service Song");
        Mock<IShowSessionCache> sessionCache = new();
        sessionCache.Setup(cache => cache.GetOrLoad("Presentations/service.cpres")).Returns(document);

        ShowContentBrowseService service = CreateService(
            catalog,
            workspace,
            sessionCache: sessionCache.Object);

        ShowContentBrowseSnapshot snapshot = await service.InitializeAsync();

        snapshot.SelectedSourceKey.Should().Be("playlist:playlist-1");
        snapshot.SelectedPresentationPath.Should().Be("C:/content/Presentations/service.cpres");
        snapshot.Presentations.Should().ContainSingle(item => item.Title == "Service Song" && item.IsSelected);
        snapshot.Slides.Should().HaveCount(2);
        snapshot.Slides[0].Title.Should().Be("1. Verse");
        sessionCache.Verify(cache => cache.SetSessionOrder(
            It.Is<IReadOnlyList<PresentationRefDto>>(items => items.Single().Path == "Presentations/service.cpres")), Times.Once);
    }

    [Fact]
    public async Task TakeSlideLiveAsync_enters_prepared_cue_and_executes_slide_actions()
    {
        CatalogDto catalog = new()
        {
            Playlists =
            [
                new PlaylistDto
                {
                    Id = "playlist-1",
                    Name = "Sunday",
                    Items = [new PresentationRefDto { Path = "Presentations/service.cpres", Title = "Service Song" }],
                },
            ],
        };
        FakeWorkspaceService workspace = new();
        PresentationDocument document = CreateDocument("C:/content/Presentations/service.cpres", "Service Song");
        PreparedSlideCue cue = new()
        {
            Presentation = document,
            PresentationPath = document.SourcePath,
            SlideId = "slide-2",
            InstanceKey = "slide-2",
            SlideIndex = 1,
            Slide = document.Project!.Slides[1],
        };
        Mock<IShowSessionCache> sessionCache = new();
        sessionCache.Setup(cache => cache.GetOrLoad("Presentations/service.cpres")).Returns(document);
        Mock<ICuePreparationService> cuePreparation = new();
        cuePreparation
            .Setup(service => service.GetPreparedSlideCue("Presentations/service.cpres", "slide-2", null))
            .Returns((PreparedSlideCue?)null);
        cuePreparation
            .Setup(service => service.PrepareSlideCueAsync(
                "Presentations/service.cpres",
                "slide-2",
                null,
                document,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cue);
        Mock<IPlaybackEngine> playback = new();
        Mock<ISlideActionExecutionService> slideActions = new();

        ShowContentBrowseService service = CreateService(
            catalog,
            workspace,
            sessionCache: sessionCache.Object,
            cuePreparation: cuePreparation.Object,
            playback: playback.Object,
            slideActions: slideActions.Object);

        bool result = await service.TakeSlideLiveAsync("Presentations/service.cpres", "slide-2");

        result.Should().BeTrue();
        playback.Verify(engine => engine.EnterPreparedSlideCue(cue), Times.Once);
        slideActions.Verify(actions => actions.ExecuteForSlide(document.Project!.Slides[1]), Times.Once);
        sessionCache.Verify(cache => cache.SchedulePrefetch(document.SourcePath, 2), Times.Once);
        workspace.Workspace.SelectedPresentationPath.Should().Be(document.SourcePath);
        workspace.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_scans_catalog_presentations_and_slide_text()
    {
        CatalogDto catalog = new()
        {
            Libraries =
            [
                new LibraryDto
                {
                    Id = "library-1",
                    Name = "Library",
                    Presentations =
                    [
                        new PresentationRefDto { Path = "Presentations/amazing.cpres", Title = "Hymn" },
                        new PresentationRefDto { Path = "Presentations/other.cpres", Title = "Other" },
                    ],
                },
            ],
        };
        PresentationDocument match = CreateDocument("C:/content/Presentations/amazing.cpres", "Hymn");
        match.Project!.Slides[0].Layers.Add(new TextLayer { Id = "text-1", Content = "Amazing grace" });
        PresentationDocument other = CreateDocument("C:/content/Presentations/other.cpres", "Other");
        Mock<IPresentationDocumentService> presentationDocuments = new();
        presentationDocuments.Setup(service => service.Open("Presentations/amazing.cpres")).Returns(match);
        presentationDocuments.Setup(service => service.Open("Presentations/other.cpres")).Returns(other);

        ShowContentBrowseService service = CreateService(
            catalog,
            new FakeWorkspaceService(),
            sessionCache: Mock.Of<IShowSessionCache>(),
            presentationDocuments: presentationDocuments.Object);

        ShowContentBrowseSnapshot snapshot = await service.SearchAsync("grace");

        snapshot.Presentations.Should().ContainSingle(item => item.Path == "Presentations/amazing.cpres");
        snapshot.SelectedPresentationPath.Should().Be("C:/content/Presentations/amazing.cpres");
        snapshot.Slides.Should().HaveCount(2);
        snapshot.StatusMessage.Should().Contain("Search found 1 presentation");
    }

    private static ShowContentBrowseService CreateService(
        CatalogDto catalog,
        IWorkspaceService workspace,
        IShowSessionCache? sessionCache = null,
        ICuePreparationService? cuePreparation = null,
        IPlaybackEngine? playback = null,
        ISlideActionExecutionService? slideActions = null,
        IPresentationDocumentService? presentationDocuments = null)
    {
        Mock<ICatalogService> catalogService = new();
        catalogService.SetupGet(service => service.Catalog).Returns(catalog);
        catalogService.Setup(service => service.LoadAsync(It.IsAny<ContentMaintenanceTrigger>())).Returns(Task.CompletedTask);

        return new ShowContentBrowseService(
            catalogService.Object,
            workspace,
            sessionCache ?? Mock.Of<IShowSessionCache>(),
            presentationDocuments ?? Mock.Of<IPresentationDocumentService>(),
            cuePreparation ?? Mock.Of<ICuePreparationService>(),
            playback ?? Mock.Of<IPlaybackEngine>(),
            slideActions ?? Mock.Of<ISlideActionExecutionService>(),
            NullLogger<ShowContentBrowseService>.Instance);
    }

    private static PresentationDocument CreateDocument(string sourcePath, string title)
    {
        PresentationSlide firstSlide = new() { Id = "slide-1", SectionLabel = "Verse" };
        PresentationSlide secondSlide = new() { Id = "slide-2", SectionLabel = "Chorus" };

        return new PresentationDocument
        {
            SourcePath = sourcePath,
            Manifest = new PresentationManifestDto { Title = title },
            Slides =
            [
                new SlideDto { Id = "slide-1", SectionLabel = "Verse" },
                new SlideDto { Id = "slide-2", SectionLabel = "Chorus" },
            ],
            Project = new PresentationProject
            {
                Manifest = new PresentationManifest { Title = title },
                Slides = [firstSlide, secondSlide],
                SourcePath = sourcePath,
            },
        };
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        public WorkspaceDto Workspace { get; set; } = new();

        public int SaveCount { get; private set; }

        public Task LoadAsync() => Task.CompletedTask;

        public Task SaveAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public void Update(Action<WorkspaceDto> mutator)
        {
            mutator(Workspace);
        }
    }
}