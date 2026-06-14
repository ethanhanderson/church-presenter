using ChurchPresenter.ViewModels;

namespace ChurchPresenter.Features.Show.ShowControls.Pages;

internal interface IShowControlsPage
{
    ShowViewModel? ViewModel { get; }

    void SetViewModel(ShowViewModel viewModel);
}
