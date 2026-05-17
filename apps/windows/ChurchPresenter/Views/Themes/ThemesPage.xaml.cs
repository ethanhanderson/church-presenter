using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class ThemesPage : Page
{
    public ThemesViewModel ViewModel { get; }

    public ThemesPage()
        : this(App.Services)
    {
    }

    private ThemesPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ViewModel = services.GetRequiredService<ThemesViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += ThemesPage_Loaded;
    }

    private async void ThemesPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        ThemesList.SelectedItem = ViewModel.SelectedTheme;
        ThemeSlidesList.SelectedItem = ViewModel.SelectedThemeSlide;
        LayersList.SelectedItem = ViewModel.SelectedLayer;
        SourceSlidesCombo.SelectedItem = ViewModel.SelectedSourceSlide;
    }

    private void ThemesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ThemeTemplate theme)
        {
            ThemesList.SelectedItem = theme;
            ViewModel.SelectThemeById(theme.Id);
            ThemeSlidesList.SelectedItem = ViewModel.SelectedThemeSlide;
            LayersList.SelectedItem = ViewModel.SelectedLayer;
        }
    }

    private void ThemeSlidesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ThemeTemplateSlide slide)
        {
            ThemeSlidesList.SelectedItem = slide;
            ViewModel.SelectThemeSlideById(slide.Id);
            LayersList.SelectedItem = ViewModel.SelectedLayer;
        }
    }

    private void LayersList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SlideLayer layer)
        {
            LayersList.SelectedItem = layer;
            ViewModel.SelectLayerById(layer.Id);
        }
    }

    private void SourceSlidesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceSlidesCombo.SelectedItem is PresentationSlide slide)
            ViewModel.SelectSourceSlideById(slide.Id);
    }
}