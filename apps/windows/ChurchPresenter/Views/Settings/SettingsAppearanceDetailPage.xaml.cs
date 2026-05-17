using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class SettingsAppearanceDetailPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsAppearanceDetailPage()
        : this(App.Services)
    {
    }

    private SettingsAppearanceDetailPage(IServiceProvider services)
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
        SettingsPageLayout.BindSettingsColumnWidth(this, SettingsColumnRoot);
    }

    private async void ClearRecent_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ViewModel.ClearRecentFilesAsync();
}