
namespace ChurchPresenter.Hosting;

internal sealed class AppNavigationRoute
{
    private AppNavigationRoute(string tag, Type pageType)
    {
        Tag = tag;
        PageType = pageType;
    }

    public string Tag { get; }

    public Type PageType { get; }

    public static readonly AppNavigationRoute Show = new("Show", typeof(ShowPage));
    public static readonly AppNavigationRoute Editor = new("EditorPage", typeof(EditPage));
    public static readonly AppNavigationRoute Reflow = new("Reflow", typeof(ReflowPage));
    public static readonly AppNavigationRoute ThemeLibrary = new("ThemeLibraryPage", typeof(ThemesPage));
    public static readonly AppNavigationRoute Settings = new("Settings", typeof(SettingsPage));

    public static IReadOnlyList<AppNavigationRoute> TopLevelRoutes { get; } =
    [
        Show,
        Editor,
        Reflow,
        ThemeLibrary,
        Settings,
    ];

    public static IReadOnlyList<Type> ActivePageTypes { get; } =
    [
        typeof(ShowPage),
        typeof(EditPage),
        typeof(ReflowPage),
        typeof(ThemesPage),
        typeof(SettingsPage),
        typeof(SettingsCategoryListPage),
        typeof(SettingsOutputPage),
        typeof(SettingsShowDetailPage),
        typeof(SettingsEditorDetailPage),
        typeof(SettingsReflowDetailPage),
        typeof(SettingsLibraryLocationPage),
        typeof(SettingsIntegrationsDetailPage),
        typeof(SettingsAppearanceDetailPage),
        typeof(OutputPage),
        typeof(StageOutputPage),
    ];

    public static AppNavigationRoute Resolve(string tag) =>
        TopLevelRoutes.FirstOrDefault(route => string.Equals(route.Tag, tag, StringComparison.Ordinal))
        ?? Show;
}
