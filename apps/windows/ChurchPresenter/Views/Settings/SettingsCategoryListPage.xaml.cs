using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class SettingsCategoryListPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsCategoryListPage()
        : this(App.Services)
    {
    }

    private SettingsCategoryListPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ViewModel = services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        ViewModel.LoadAllFromSettings();
        SettingsPageLayout.BindSettingsColumnWidth(this, ContentRoot);
        ApplyAboutTexts();
    }

    private void ApplyAboutTexts()
    {
        string title = AppMetadata.GetDisplayName();
        if (string.IsNullOrWhiteSpace(title))
            title = "Church Presenter";

        string publisher = AppMetadata.GetPublisherDisplayName();
        int year = DateTime.UtcNow.Year;
        string subtitle = string.IsNullOrWhiteSpace(publisher)
            ? $"© {year}. All rights reserved."
            : $"© {year} {publisher}. All rights reserved.";

        string version = AppMetadata.GetVersionMajorMinor();

        AboutStaticTitleText.Text = title;
        AboutStaticSubtitleText.Text = subtitle;
        AboutStaticVersionText.Text = version;

        AboutExpanderTitleText.Text = title;
        AboutExpanderSubtitleText.Text = subtitle;
        AboutExpanderVersionText.Text = version;

        bool hasDetails = AboutExpandableDetails.HasExpandableDetails;
        AboutExpanderCard.Visibility = hasDetails ? Visibility.Visible : Visibility.Collapsed;
        AboutStaticCard.Visibility = hasDetails ? Visibility.Collapsed : Visibility.Visible;
        if (hasDetails)
            AboutDependencyItems.ItemsSource = AboutExpandableDetails.DependencyLines;
    }

    private void Output_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsOutputPage));

    private void Show_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsShowDetailPage));

    private void Editor_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsEditorDetailPage));

    private void Reflow_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsReflowDetailPage));

    private void LibraryLocation_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsLibraryLocationPage));

    private void Integrations_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsIntegrationsDetailPage));

    private void Appearance_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SettingsNavigation.NavigateToDetail(Frame, typeof(SettingsAppearanceDetailPage));
}