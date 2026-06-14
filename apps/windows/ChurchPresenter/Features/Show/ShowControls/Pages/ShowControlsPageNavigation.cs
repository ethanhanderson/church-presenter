using ChurchPresenter.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChurchPresenter.Features.Show.ShowControls.Pages;

internal static class ShowControlsPageNavigation
{
    internal static void ApplyViewModel(Page page, NavigationEventArgs e)
    {
        if (e.Parameter is not ShowViewModel vm)
            return;

        if (page is IShowControlsPage controlsPage)
            controlsPage.SetViewModel(vm);
        else
            page.DataContext = vm;
    }
}
