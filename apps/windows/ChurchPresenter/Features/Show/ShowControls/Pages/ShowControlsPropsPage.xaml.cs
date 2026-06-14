using ChurchPresenter.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChurchPresenter.Features.Show.ShowControls.Pages;

public sealed partial class ShowControlsPropsPage : Page, IShowControlsPage
{
    public ShowViewModel? ViewModel { get; private set; }

    public ShowControlsPropsPage() => InitializeComponent();

    public void SetViewModel(ShowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ShowControlsPageNavigation.ApplyViewModel(this, e);
    }
}
