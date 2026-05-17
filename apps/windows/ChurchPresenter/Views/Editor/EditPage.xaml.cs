using System;
using System.Threading.Tasks;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ChurchPresenter.Views;

public sealed partial class EditPage : Page
{
    public EditViewModel ViewModel { get; }

    public EditPage()
        : this(App.Services)
    {
    }

    private EditPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        InitializeComponent();
        ViewModel = services.GetRequiredService<EditViewModel>();
        DataContext = ViewModel;
        Loaded += EditPage_Loaded;
    }

    private async void EditPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        SlidesList.SelectedItem = ViewModel.SelectedSlide;
        LayersList.SelectedItem = ViewModel.SelectedLayer;
    }

    private void SlidesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PresentationSlide slide)
        {
            SlidesList.SelectedItem = slide;
            ViewModel.SelectSlideById(slide.Id);
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

    private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemePicker.SelectedItem is ThemeTemplate theme)
            ViewModel.SelectThemeForSlide(theme.Id);
    }

    private void ThemeSlidePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSlidePicker.SelectedItem is ThemeTemplateSlide slide)
            ViewModel.SelectThemeSlideForSlide(slide.Id);
    }

    private void SaveKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SaveCommand.CanExecute(null))
            ViewModel.SaveCommand.Execute(null);
        args.Handled = true;
    }

    private void SaveAsKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SaveAsCommand.CanExecute(null))
            ViewModel.SaveAsCommand.Execute(null);
        args.Handled = true;
    }

    private void DuplicateKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.DuplicateSlideCommand.CanExecute(null))
            ViewModel.DuplicateSlideCommand.Execute(null);
        args.Handled = true;
    }

    private void DeleteKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.HasSelectedLayer && ViewModel.DeleteLayerCommand.CanExecute(null))
            ViewModel.DeleteLayerCommand.Execute(null);
        else if (ViewModel.DeleteSlideCommand.CanExecute(null))
            ViewModel.DeleteSlideCommand.Execute(null);

        args.Handled = true;
    }

    private void DuplicateSlideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PresentationSlide slide)
        {
            ViewModel.SelectSlideById(slide.Id);
            ViewModel.DuplicateSlideCommand.Execute(null);
            SlidesList.SelectedItem = ViewModel.SelectedSlide;
        }
    }

    private void DeleteSlideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PresentationSlide slide)
        {
            ViewModel.SelectSlideById(slide.Id);
            ViewModel.DeleteSlideCommand.Execute(null);
            SlidesList.SelectedItem = ViewModel.SelectedSlide;
        }
    }

    private void MoveSlideUpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PresentationSlide slide)
        {
            ViewModel.SelectSlideById(slide.Id);
            ViewModel.MoveSlideUpCommand.Execute(null);
            SlidesList.SelectedItem = ViewModel.SelectedSlide;
        }
    }

    private void MoveSlideDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PresentationSlide slide)
        {
            ViewModel.SelectSlideById(slide.Id);
            ViewModel.MoveSlideDownCommand.Execute(null);
            SlidesList.SelectedItem = ViewModel.SelectedSlide;
        }
    }

    private async void EditSlideTransitionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PresentationSlide slide)
            return;

        ViewModel.SelectSlideById(slide.Id);
        SlidesList.SelectedItem = ViewModel.SelectedSlide;
        await EditSelectedSlideTransitionAsync();
    }

    private async void EditSelectedSlideTransitionButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedSlideTransitionAsync();
    }

    private void ClearSelectedSlideTransitionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasSelectedSlideTransition)
            return;

        ViewModel.SetSelectedSlideTransition(null);
    }

    private void DeleteLayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SlideLayer layer)
        {
            ViewModel.SelectLayerById(layer.Id);
            LayersList.SelectedItem = ViewModel.SelectedLayer;
            ViewModel.DeleteLayerCommand.Execute(null);
        }
    }

    private async Task EditSelectedSlideTransitionAsync()
    {
        if (!ViewModel.HasSelectedSlide || XamlRoot == null)
            return;

        var result = await PromptForTransitionAsync(ViewModel.GetSelectedSlideTransitionForPicker());
        if (!result.Submitted)
            return;

        ViewModel.SetSelectedSlideTransition(result.ClearRequested ? null : result.Transition);
    }

    private async Task<TransitionDialogResult> PromptForTransitionAsync(SlideTransition? currentTransition)
    {
        var picker = new TransitionPickerDialogContent();
        picker.Initialize(null, currentTransition);

        var dialog = new ContentDialog
        {
            Title = "Slide Transition Override",
            PrimaryButtonText = "Apply",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = picker,
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => new TransitionDialogResult(true, false, picker.BuildTransition()),
            ContentDialogResult.Secondary => new TransitionDialogResult(true, true, null),
            _ => new TransitionDialogResult(false, false, null),
        };
    }

    private sealed record TransitionDialogResult(bool Submitted, bool ClearRequested, SlideTransition? Transition);
}