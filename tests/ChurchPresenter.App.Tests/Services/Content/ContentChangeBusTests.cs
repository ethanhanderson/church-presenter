using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentChangeBusTests
{
    [Fact]
    public void Publish_notifies_subscribers_with_typed_event()
    {
        var bus = new ContentChangeBus(NullLogger<ContentChangeBus>.Instance);
        ContentChangeEvent? observed = null;
        bus.Changed += (_, change) => observed = change;
        var change = new ContentChangeEvent
        {
            Kind = ContentChangeKind.PresentationDeleted,
            SubjectId = "Presentations/song.cpres",
            Source = "test",
        };

        bus.Publish(change);

        observed.Should().BeSameAs(change);
    }

    [Fact]
    public async Task SharedConfigService_SaveAsync_publishes_shared_config_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var bus = new ContentChangeBus(NullLogger<ContentChangeBus>.Instance);
        var changes = new List<ContentChangeEvent>();
        bus.Changed += (_, change) => changes.Add(change);

        var service = new SharedConfigService(
            paths.Object,
            new ContentStore(NullLogger<ContentStore>.Instance),
            NullLogger<SharedConfigService>.Instance,
            bus);

        await service.SaveAsync();

        changes.Should().ContainSingle(change =>
            change.Kind == ContentChangeKind.SharedConfigChanged &&
            string.Equals(change.SubjectId, paths.Object.GetConfigurationsDirectory(), StringComparison.OrdinalIgnoreCase));
    }
}
