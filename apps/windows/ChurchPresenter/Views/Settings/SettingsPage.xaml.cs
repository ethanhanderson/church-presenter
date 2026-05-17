using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

/// <summary>Root settings shell: navigates to the category hub (Windows 11–style list).</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SettingsNavFrame.Navigated += OnNavigated;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (SettingsNavFrame.Content == null)
            _ = SettingsNavFrame.Navigate(typeof(SettingsCategoryListPage));
    }

    private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _ = SettingsScrollViewer.ChangeView(null, 0, null, true);
    }
}