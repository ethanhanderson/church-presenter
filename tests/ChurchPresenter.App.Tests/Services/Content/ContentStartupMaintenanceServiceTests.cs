
using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentStartupMaintenanceServiceTests
{
    [Fact]
    public async Task StartAsync_runs_startup_pipeline_and_prunes_stale_caches()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Settings).Returns(new AppSettingsDto());
        var bootstrap = new Mock<IContentBootstrapService>();
        var catalog = new Mock<ICatalogService>();
        var diagnostics = new Mock<IContentDiagnosticsQueryService>();
        diagnostics
            .Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentDiagnosticsSnapshot
            {
                Diagnostics =
                [
                    new ContentDiagnosticItem
                    {
                        Id = "catalog-empty",
                        Title = "Catalog is empty",
                        Message = "No libraries or playlists are currently loaded.",
                        Severity = "info",
                    },
                ],
            });
        var sessionCache = new Mock<IShowSessionCache>();
        sessionCache.Setup(cache => cache.PruneMissingFiles()).Returns(["missing.cpres"]);
        var cuePreparation = new Mock<ICuePreparationService>();
        var contentChanges = new Mock<IContentChangeBus>();

        var service = new ContentStartupMaintenanceService(
            settings.Object,
            bootstrap.Object,
            catalog.Object,
            diagnostics.Object,
            sessionCache.Object,
            cuePreparation.Object,
            contentChanges.Object,
            NullLogger<ContentStartupMaintenanceService>.Instance);

        var snapshots = new List<ContentStartupMaintenanceSnapshot>();
        service.Changed += (_, args) => snapshots.Add(args.Snapshot);

        await service.StartAsync();

        settings.Verify(service => service.LoadAsync(), Times.Once);
        bootstrap.Verify(service => service.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
        catalog.Verify(service => service.LoadAsync(ContentMaintenanceTrigger.Startup), Times.Once);
        sessionCache.Verify(cache => cache.PruneMissingFiles(), Times.AtLeastOnce);
        cuePreparation.Verify(service => service.InvalidatePresentationCues("missing.cpres"), Times.Once);
        contentChanges.Verify(
            bus => bus.Publish(It.Is<ContentChangeEvent>(change => change.Kind == ContentChangeKind.RepairCompleted)),
            Times.Once);

        service.Current.Phase.Should().Be(ContentStartupMaintenancePhase.Completed);
        service.Current.Diagnostics.Should().NotBeNull();
        service.Current.PrunedCachePaths.Should().ContainSingle("missing.cpres");
        snapshots.Should().Contain(snapshot => snapshot.Phase == ContentStartupMaintenancePhase.Completed);
    }

    [Fact]
    public async Task StartAsync_records_failure_without_throwing()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Settings).Returns(new AppSettingsDto());
        var bootstrap = new Mock<IContentBootstrapService>();
        var catalog = new Mock<ICatalogService>();
        catalog
            .Setup(service => service.LoadAsync(ContentMaintenanceTrigger.Startup))
            .ThrowsAsync(new InvalidOperationException("catalog unavailable"));

        var service = new ContentStartupMaintenanceService(
            settings.Object,
            bootstrap.Object,
            catalog.Object,
            Mock.Of<IContentDiagnosticsQueryService>(),
            Mock.Of<IShowSessionCache>(),
            Mock.Of<ICuePreparationService>(),
            Mock.Of<IContentChangeBus>(),
            NullLogger<ContentStartupMaintenanceService>.Instance);

        await service.StartAsync();

        service.Current.Phase.Should().Be(ContentStartupMaintenancePhase.Failed);
        service.Current.IsRunning.Should().BeFalse();
        service.Current.ErrorMessage.Should().Be("catalog unavailable");
    }
}
