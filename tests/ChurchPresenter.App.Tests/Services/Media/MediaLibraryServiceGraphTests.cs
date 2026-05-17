using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Media;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Media;

public sealed class MediaLibraryServiceGraphTests
{
    [Fact]
    public async Task GetAssetsAsync_projects_library_items_as_stable_media_assets()
    {
        string root = CreateTestRoot();
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        string source = WriteMediaFile(root, "walk-in.mp4");
        MediaLibraryService service = CreateService(paths);

        MediaLibraryItem item = await service.AddRootItemAsync(source);

        IReadOnlyList<MediaAsset> assets = await service.GetAssetsAsync();

        MediaAsset asset = assets.Should().ContainSingle().Subject;
        asset.AssetId.Should().Be(item.Id);
        asset.DisplayName.Should().Be("walk-in");
        asset.Kind.Should().Be(MediaAssetKind.Video);
        asset.StoragePolicy.Should().Be(MediaStoragePolicy.Managed);
        asset.Availability.Status.Should().Be(MediaAvailabilityStatus.Available);
        asset.DefaultCue.PlaybackMode.Should().Be(MediaPlaybackMode.Loop);
        asset.DefaultCue.Muted.Should().BeTrue();
        asset.ResolvedPath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ResolvePlaybackRequestAsync_resolves_slide_cue_by_asset_identity_and_applies_overrides()
    {
        string root = CreateTestRoot();
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        string source = WriteMediaFile(root, "sermon-background.mp4");
        MediaLibraryService service = CreateService(paths);
        MediaLibraryItem item = await service.AddRootItemAsync(source);

        SlideMediaCue cue = new()
        {
            Id = "cue-1",
            MediaId = item.Id,
            DisplayName = "Sermon Background",
            Target = MediaPlaybackLayerTargetNames.MediaOverlay,
            Fit = "contain",
            Loop = false,
            Muted = false,
            Autoplay = true,
            Transition = new SlideTransition { Type = "fade", Duration = 400 },
        };

        MediaPlaybackRequest? request = await service.ResolvePlaybackRequestAsync(cue, "slide:welcome");

        request.Should().NotBeNull();
        request!.AssetId.Should().Be(item.Id);
        request.CueId.Should().Be("cue-1");
        request.LayerTarget.Should().Be(MediaPlaybackLayerTarget.MediaOverlay);
        request.DisplayName.Should().Be("Sermon Background");
        request.IsPlayable.Should().BeTrue();
        request.EffectiveCue.Role.Should().Be(MediaCueRole.Foreground);
        request.EffectiveCue.Scaling.Should().Be(MediaScalingMode.ScaleToFit);
        request.EffectiveCue.PlaybackMode.Should().Be(MediaPlaybackMode.Stop);
        request.EffectiveCue.Muted.Should().BeFalse();
        request.EffectiveCue.Transition?.TransitionId.Should().Be("fade");
        request.EffectiveCue.Transition?.Duration.Should().Be(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task BuildCleanupReferenceGraphAsync_includes_playlists_and_presentation_media_references()
    {
        string root = CreateTestRoot();
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        string source = WriteMediaFile(root, "walk-in.png");
        MediaLibraryService service = CreateService(paths);
        MediaLibraryItem item = await service.AddRootItemAsync(source);
        MediaPlaylistManifest playlist = await service.CreatePlaylistAsync("Walk In");
        await service.AddItemAsync(playlist.Id, source);

        PresentationProject presentation = new()
        {
            Manifest = new PresentationManifest
            {
                PresentationId = "presentation-1",
                Title = "Sunday Service",
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
                            MediaId = item.Path,
                            Target = MediaPlaybackLayerTargetNames.MediaUnderlay,
                        },
                    ],
                },
            ],
        };

        MediaCleanupReferenceGraph graph = await service.BuildCleanupReferenceGraphAsync([presentation]);

        graph.Nodes.Should().Contain(node =>
            node.Surface == MediaReferenceSurface.MediaPlaylist
            && node.DisplayName == "Walk In");
        graph.Nodes.Should().Contain(node =>
            node.Surface == MediaReferenceSurface.Presentation
            && node.DisplayName == "Sunday Service"
            && node.AssetIds.Contains(item.Id));
    }

    private static MediaLibraryService CreateService(Moq.Mock<IContentDirectoryService> paths) =>
        new(paths.Object, NullLogger<MediaLibraryService>.Instance);

    private static string CreateTestRoot() =>
        Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));

    private static string WriteMediaFile(string root, string fileName)
    {
        string source = Path.Combine(root, "source", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "media");
        return source;
    }
}
