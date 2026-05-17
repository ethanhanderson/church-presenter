using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class SettingsShowDetailPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsShowDetailPage()
        : this(App.Services)
    {
    }

    private SettingsShowDetailPage(IServiceProvider services)
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