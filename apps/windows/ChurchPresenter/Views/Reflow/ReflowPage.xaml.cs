using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class ReflowPage : Page
{
    public ReflowViewModel ViewModel { get; }

    public ReflowPage()
        : this(App.Services)
    {
    }

    private ReflowPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ViewModel = services.GetRequiredService<ReflowViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += ReflowPage_Loaded;
    }

    private async void ReflowPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        SlidesList.SelectedItem = ViewModel.SelectedSlideItem;
    }

    private void SlidesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ReflowSlideItem item)
        {
            SlidesList.SelectedItem = item;
            ViewModel.SelectSlideById(item.Slide.Id);
        }
    }

    private void PresentationThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresentationThemePicker.SelectedItem is ThemeTemplate theme)
            ViewModel.SelectPresentationTheme(theme.Id);
    }

    private void SlideStylePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlideStylePicker.SelectedItem is ThemeTemplateSlide slide)
            ViewModel.SelectSlideStyle(slide.Id);
    }
}