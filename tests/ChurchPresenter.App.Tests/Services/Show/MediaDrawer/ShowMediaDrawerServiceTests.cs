
using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Show.MediaDrawer;

[ReferenceArea(ReferenceAreas.ShowWorkflow)]
public sealed class ShowMediaDrawerServiceTests
{
    [Fact]
    public async Task LoadAsync_projects_root_media_availability()
    {
        string file = Path.GetTempFileName();
        try
        {
            Mock<IMediaLibraryService> media = new();
            media.Setup(service => service.GetRootItemsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new MediaLibraryItem { Id = "media-1", Name = "Walk In", Path = file, Type = "video" },
                ]);
            media.Setup(service => service.ResolveStoredMediaPath(file)).Returns(file);

            ShowMediaDrawerService service = new(media.Object, Mock.Of<ICuePreparationService>(), Mock.Of<ICueDispatcher>());

            ShowMediaDrawerSnapshot snapshot = await service.LoadAsync();

            snapshot.Items.Should().ContainSingle(item =>
                item.Id == "media-1"
                && item.Name == "Walk In"
                && item.IsAvailable);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task TakeMediaLiveAsync_prepares_and_enters_media_cue()
    {
        MediaLibraryItem item = new() { Id = "media-1", Name = "Walk In", Path = "walkin.mp4", Type = "video" };
        PreparedMediaCue cue = new()
        {
            Media = new OutputLayerMedia { MediaId = "walkin.mp4", DisplayName = "Walk In" },
        };
        Mock<IMediaLibraryService> media = new();
        media.Setup(service => service.GetRootItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        Mock<ICuePreparationService> cuePreparation = new();
        cuePreparation.Setup(service => service.PrepareMediaCue(item)).Returns(cue);
        FakeCueDispatcher cueDispatcher = new();
        ShowMediaDrawerService service = new(media.Object, cuePreparation.Object, cueDispatcher);

        bool activated = await service.TakeMediaLiveAsync("media-1");

        activated.Should().BeTrue();
        cueDispatcher.DispatchMediaCueCount.Should().Be(1);
        cueDispatcher.LastMediaCueTarget.Should().Be(MediaPlaybackLayerTargetNames.MediaUnderlay);
    }
}
