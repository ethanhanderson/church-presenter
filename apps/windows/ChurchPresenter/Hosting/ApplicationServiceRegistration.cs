using Microsoft.Extensions.DependencyInjection;

namespace ChurchPresenter.Hosting;

internal static class ApplicationServiceRegistration
{
    internal static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IContentStore, ContentStore>();
        services.AddSingleton<IContentChangeBus, ContentChangeBus>();
        services.AddSingleton<IContentDirectoryService, ContentDirectoryService>();
        services.AddSingleton<ICpresDocumentService, CpresDocumentService>();
        services.AddSingleton<ILibraryRegistryService, LibraryRegistryService>();
        services.AddSingleton<IPlaylistRegistryService, PlaylistRegistryService>();
        services.AddSingleton<IContentMaintenanceLogService, ContentMaintenanceLogService>();
        services.AddSingleton<IMachineStateService, MachineStateService>();
        services.AddSingleton<ISharedConfigService, SharedConfigService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IShowTransitionDefaults, ShowTransitionDefaults>();
        services.AddSingleton<IStageLayoutRegistryService, StageLayoutRegistryService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IPresentationProjectService, PresentationProjectService>();
        services.AddSingleton<IPresentationDocumentService, PresentationDocumentService>();
        services.AddSingleton<IMediaLibraryService, MediaLibraryService>();
        services.AddSingleton<IThemeLibraryService, ThemeLibraryService>();
        services.AddSingleton<IShowSessionCache, ShowSessionCache>();
        services.AddSingleton<ICuePreparationService, CuePreparationService>();
        services.AddSingleton<IContentCacheInvalidator>(static sp => (IContentCacheInvalidator)sp.GetRequiredService<IShowSessionCache>());
        services.AddSingleton<IContentCacheInvalidator>(static sp => (IContentCacheInvalidator)sp.GetRequiredService<ICuePreparationService>());
        services.AddSingleton<ILocalCollectionService, LocalCollectionService>();
        services.AddSingleton<IContentAuditService, ContentAuditService>();
        services.AddSingleton<ISupportPackageService, SupportPackageService>();
        services.AddSingleton<IContentSupportQueryService, ContentSupportQueryService>();
        services.AddSingleton<IContentDiagnosticsQueryService, ContentDiagnosticsQueryService>();
        services.AddSingleton<IContentBootstrapService, ContentBootstrapService>();
        services.AddSingleton<IContentStartupMaintenanceService, ContentStartupMaintenanceService>();
        services.AddSingleton<IContentRootMediaMigrationService, ContentRootMediaMigrationService>();
        services.AddSingleton<IAppDataInitializer, AppDataInitializer>();
        services.AddSingleton<IAppActivationService, AppActivationService>();
        services.AddSingleton<IPresentationTextWorkflowService, PresentationTextWorkflowService>();
        services.AddSingleton<ICollectionPackageService, CollectionPackageService>();
        services.AddSingleton<ISettingsHealthService, SettingsHealthService>();
        services.AddSingleton<IThemeApplicationService, ThemeApplicationService>();
        services.AddSingleton<IThemeResolutionService, ThemeResolutionService>();
        services.AddSingleton<IQuickEditTextLayerService, QuickEditTextLayerService>();
        services.AddSingleton<IActivePresentationService, ActivePresentationService>();
        services.AddSingleton<IPresentationItemActionService, PresentationItemActionService>();
        services.AddSingleton<ISlideItemActionService, SlideItemActionService>();
        services.AddSingleton<ISidebarPresentationClipboardService, SidebarPresentationClipboardService>();
        services.AddSingleton<ISlideClipboardService, SlideClipboardService>();
        services.AddSingleton<ISlideTextStyleClipboardService, SlideTextStyleClipboardService>();

        return services;
    }
}
