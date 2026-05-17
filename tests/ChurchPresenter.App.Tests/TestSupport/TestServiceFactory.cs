using ChurchPresenter.Core.Cpres;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.TestSupport;

/// <summary>
/// Convenience factory methods that wire up concrete service instances for tests,
/// insulating test code from constructor-signature changes in production services.
/// </summary>
public static class TestServiceFactory
{
    private static IContentStore CreateContentStore() => new ContentStore(NullLogger<ContentStore>.Instance);

    public static CatalogService CreateCatalogService(IContentDirectoryService paths) =>
        new(paths,
            new LibraryRegistryService(paths, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

    public static SettingsService CreateSettingsService(IContentDirectoryService paths) =>
        new(paths,
            new SharedConfigService(paths, CreateContentStore(), NullLogger<SharedConfigService>.Instance),
            new MachineStateService(paths, NullLogger<MachineStateService>.Instance),
            NullLogger<SettingsService>.Instance);

    public static AppDataInitializer CreateAppDataInitializer(IContentDirectoryService paths) =>
        new(new ContentBootstrapService(
            paths,
            CreateContentStore(),
            new LibraryRegistryService(paths, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths, NullLogger<PlaylistRegistryService>.Instance),
            new ThemeLibraryService(paths, CreateContentStore(), NullLogger<ThemeLibraryService>.Instance),
            new ContentRootMediaMigrationService(
                paths,
                new LibraryRegistryService(paths, NullLogger<LibraryRegistryService>.Instance),
                new PlaylistRegistryService(paths, NullLogger<PlaylistRegistryService>.Instance),
                new PresentationProjectService(
                    paths,
                    new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
                    NullLogger<PresentationProjectService>.Instance),
                new ThemeLibraryService(paths, CreateContentStore(), NullLogger<ThemeLibraryService>.Instance),
                NullLogger<ContentRootMediaMigrationService>.Instance),
            new ContentMaintenanceLogService(paths, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<ContentBootstrapService>.Instance));
}