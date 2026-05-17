using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class SettingsIntegrationsDetailPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsIntegrationsDetailPage()
        : this(App.Services)
    {
    }

    private SettingsIntegrationsDetailPage(IServiceProvider services)
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

}