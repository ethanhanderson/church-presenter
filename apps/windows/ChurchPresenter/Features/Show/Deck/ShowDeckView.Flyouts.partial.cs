using System;
using System.Threading.Tasks;

using ChurchPresenter.Models.Show;
using ChurchPresenter.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Features.Show.Deck;

public sealed partial class ShowDeckView
{
    private enum TransitionFlyoutTarget
    {
        None,
        GlobalSlideCustom,
        GlobalMediaCustom,
        PresentationDefaultSlide,
    }

    private TransitionFlyoutTarget _transitionFlyoutTarget;
    private string? _presentationDefaultTransitionPath;

    private void GlobalSlideCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        _transitionFlyoutTarget = TransitionFlyoutTarget.GlobalSlideCustom;
        _presentationDefaultTransitionPath = null;
        EnsureTransitionFlyout();
        var existing = Vm.GetGlobalSlideCustomTransitionForPicker();
        _transitionFlyoutContent!.Initialize(Vm, existing);
        if (_transitionFlyoutRemoveButton != null)
        {
            _transitionFlyoutRemoveButton.Content = "Clear custom transition";
            _transitionFlyoutRemoveButton.Visibility =
                Vm.GlobalSlideTransitionIsCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        _transitionFlyout!.ShowAt(btn);
    }

    private void GlobalMediaCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        _transitionFlyoutTarget = TransitionFlyoutTarget.GlobalMediaCustom;
        _presentationDefaultTransitionPath = null;
        EnsureTransitionFlyout();
        var existing = Vm.GetGlobalMediaCustomTransitionForPicker();
        _transitionFlyoutContent!.Initialize(Vm, existing);
        if (_transitionFlyoutRemoveButton != null)
        {
            _transitionFlyoutRemoveButton.Content = "Clear custom transition";
            _transitionFlyoutRemoveButton.Visibility =
                Vm.GlobalMediaTransitionIsCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        _transitionFlyout!.ShowAt(btn);
    }

    private void PresentationHeaderTransition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb)
            return;

        string? path = tb.Tag is ShowPresentationDeckSection section
            ? section.PresentationPath
            : Vm.ActivePresentationFilePath;

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (tb.Tag is ShowPresentationDeckSection sec)
            tb.IsChecked = sec.HasDefaultSlideTransition;
        else
            tb.IsChecked = Vm.HasPresentationDefaultSlideTransition;

        _transitionFlyoutTarget = TransitionFlyoutTarget.PresentationDefaultSlide;
        _presentationDefaultTransitionPath = path;

        EnsureTransitionFlyout();
        var existing = Vm.GetDefaultTransitionForPath(path);
        _transitionFlyoutContent!.Initialize(Vm, existing);

        var hasExplicit = tb.Tag is ShowPresentationDeckSection s
            ? s.HasDefaultSlideTransition
            : Vm.HasPresentationDefaultSlideTransition;

        if (_transitionFlyoutRemoveButton != null)
        {
            _transitionFlyoutRemoveButton.Content = "Clear presentation default";
            _transitionFlyoutRemoveButton.Visibility = hasExplicit ? Visibility.Visible : Visibility.Collapsed;
        }

        _transitionFlyout!.ShowAt(tb);
    }

    private void TransitionFlyoutApply_Click(object sender, RoutedEventArgs e)
    {
        if (_transitionFlyoutContent == null)
            return;

        var transition = _transitionFlyoutContent.BuildTransition();
        if (transition == null)
            return;

        _ = Vm.RecordRecentTransitionAsync(transition.Type);

        _ = _transitionFlyoutTarget switch
        {
            TransitionFlyoutTarget.GlobalSlideCustom => Vm.ApplyGlobalSlideCustomTransitionAsync(transition),
            TransitionFlyoutTarget.GlobalMediaCustom => Vm.ApplyGlobalMediaCustomTransitionAsync(transition),
            TransitionFlyoutTarget.PresentationDefaultSlide when !string.IsNullOrWhiteSpace(_presentationDefaultTransitionPath) =>
                Vm.SetDefaultTransitionForPathAsync(_presentationDefaultTransitionPath, transition),
            _ => Task.CompletedTask,
        };
        _transitionFlyout?.Hide();
    }

    private void TransitionFlyoutCancel_Click(object sender, RoutedEventArgs e) =>
        _transitionFlyout?.Hide();

    private void TransitionFlyoutRemove_Click(object sender, RoutedEventArgs e)
    {
        _ = _transitionFlyoutTarget switch
        {
            TransitionFlyoutTarget.GlobalSlideCustom => Vm.ResetGlobalSlideCustomTransitionAsync(),
            TransitionFlyoutTarget.GlobalMediaCustom => Vm.ResetGlobalMediaCustomTransitionAsync(),
            TransitionFlyoutTarget.PresentationDefaultSlide when !string.IsNullOrWhiteSpace(_presentationDefaultTransitionPath) =>
                Vm.SetDefaultTransitionForPathAsync(_presentationDefaultTransitionPath, null),
            _ => Task.CompletedTask,
        };
        _transitionFlyout?.Hide();
    }

    private void EnsureTransitionFlyout()
    {
        if (_transitionFlyout != null)
            return;

        _transitionFlyoutContent = new TransitionPickerDialogContent();

        _transitionFlyoutRemoveButton = new Button
        {
            Content = "Clear custom transition",
            Visibility = Visibility.Collapsed,
        };
        _transitionFlyoutRemoveButton.Click += TransitionFlyoutRemove_Click;

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += TransitionFlyoutCancel_Click;

        var applyBtn = new Button { Content = "Apply" };
        applyBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        applyBtn.Click += TransitionFlyoutApply_Click;

        var btnRow = new Grid { Margin = new Thickness(0, 12, 0, 0), ColumnSpacing = 8 };
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_transitionFlyoutRemoveButton, 0);
        Grid.SetColumn(cancelBtn, 1);
        Grid.SetColumn(applyBtn, 2);
        btnRow.Children.Add(_transitionFlyoutRemoveButton);
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(applyBtn);

        var container = new StackPanel();
        container.Children.Add(_transitionFlyoutContent);
        container.Children.Add(btnRow);

        _transitionFlyout = new Flyout
        {
            Content = container,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = BuildFlyoutPresenterStyle(minWidth: 620, maxWidth: 800, padding: 20),
        };
    }

    private void ArrangementsToggle_CheckStateChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: ShowPresentationDeckSection section } tb)
            Vm.SetBrowseStackArrangementExpanded(section.PresentationPath, tb.IsChecked == true);
    }

    private void ArrangementsEditButton_Click(object sender, RoutedEventArgs e)
    {
        var presentationPath = (sender as Button)?.Tag is ShowPresentationDeckSection section
            ? section.PresentationPath
            : null;

        var content = new ArrangementsDialogContent();
        content.Initialize(Vm, presentationPath);
        var flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = BuildFlyoutPresenterStyle(minWidth: 340, maxWidth: 420, padding: 12, cornerRadius: 8),
        };
        flyout.ShowAt((FrameworkElement)sender);
    }

    private static Style BuildFlyoutPresenterStyle(
        double minWidth,
        double maxWidth,
        double padding,
        double? cornerRadius = null)
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, minWidth));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, maxWidth));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(padding)));
        if (cornerRadius.HasValue)
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(cornerRadius.Value)));

        return style;
    }
}
