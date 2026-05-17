using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace ChurchPresenter.Views;

public sealed partial class ShowPage
{
    // ── Transition picker flyout (global Show toolbar: slide or media custom) ─

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
        if (sender is not Button btn) return;
        _transitionFlyoutTarget = TransitionFlyoutTarget.GlobalSlideCustom;
        _presentationDefaultTransitionPath = null;
        EnsureTransitionFlyout();
        var existing = ViewModel.GetGlobalSlideCustomTransitionForPicker();
        _transitionFlyoutContent!.Initialize(ViewModel, existing);
        if (_transitionFlyoutRemoveButton != null)
        {
            _transitionFlyoutRemoveButton.Content = "Clear custom transition";
            _transitionFlyoutRemoveButton.Visibility =
                ViewModel.GlobalSlideTransitionIsCustom ? Visibility.Visible : Visibility.Collapsed;
        }
        _transitionFlyout!.ShowAt(btn);
    }

    private void GlobalMediaCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _transitionFlyoutTarget = TransitionFlyoutTarget.GlobalMediaCustom;
        _presentationDefaultTransitionPath = null;
        EnsureTransitionFlyout();
        var existing = ViewModel.GetGlobalMediaCustomTransitionForPicker();
        _transitionFlyoutContent!.Initialize(ViewModel, existing);
        if (_transitionFlyoutRemoveButton != null)
        {
            _transitionFlyoutRemoveButton.Content = "Clear custom transition";
            _transitionFlyoutRemoveButton.Visibility =
                ViewModel.GlobalMediaTransitionIsCustom ? Visibility.Visible : Visibility.Collapsed;
        }
        _transitionFlyout!.ShowAt(btn);
    }

    private void PresentationHeaderTransition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb) return;

        string? path = tb.Tag is ShowPresentationDeckSection section
            ? section.PresentationPath
            : ViewModel.ActivePresentationFilePath;

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (tb.Tag is ShowPresentationDeckSection sec)
            tb.IsChecked = sec.HasDefaultSlideTransition;
        else
            tb.IsChecked = ViewModel.HasPresentationDefaultSlideTransition;

        _transitionFlyoutTarget = TransitionFlyoutTarget.PresentationDefaultSlide;
        _presentationDefaultTransitionPath = path;

        EnsureTransitionFlyout();
        var existing = ViewModel.GetDefaultTransitionForPath(path);
        _transitionFlyoutContent!.Initialize(ViewModel, existing);

        var hasExplicit = tb.Tag is ShowPresentationDeckSection s
            ? s.HasDefaultSlideTransition
            : ViewModel.HasPresentationDefaultSlideTransition;

        if (_transitionFlyoutRemoveButton != null)
        {
            _transitionFlyoutRemoveButton.Content = "Clear presentation default";
            _transitionFlyoutRemoveButton.Visibility = hasExplicit ? Visibility.Visible : Visibility.Collapsed;
        }

        _transitionFlyout!.ShowAt(tb);
    }

    private void TransitionFlyoutApply_Click(object sender, RoutedEventArgs e)
    {
        if (_transitionFlyoutContent == null) return;
        var transition = _transitionFlyoutContent.BuildTransition();
        if (transition == null)
            return;

        _ = ViewModel.RecordRecentTransitionAsync(transition.Type);

        _ = _transitionFlyoutTarget switch
        {
            TransitionFlyoutTarget.GlobalSlideCustom => ViewModel.ApplyGlobalSlideCustomTransitionAsync(transition),
            TransitionFlyoutTarget.GlobalMediaCustom => ViewModel.ApplyGlobalMediaCustomTransitionAsync(transition),
            TransitionFlyoutTarget.PresentationDefaultSlide when !string.IsNullOrWhiteSpace(_presentationDefaultTransitionPath) =>
                ViewModel.SetDefaultTransitionForPathAsync(_presentationDefaultTransitionPath, transition),
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
            TransitionFlyoutTarget.GlobalSlideCustom => ViewModel.ResetGlobalSlideCustomTransitionAsync(),
            TransitionFlyoutTarget.GlobalMediaCustom => ViewModel.ResetGlobalMediaCustomTransitionAsync(),
            TransitionFlyoutTarget.PresentationDefaultSlide when !string.IsNullOrWhiteSpace(_presentationDefaultTransitionPath) =>
                ViewModel.SetDefaultTransitionForPathAsync(_presentationDefaultTransitionPath, null),
            _ => Task.CompletedTask,
        };
        _transitionFlyout?.Hide();
    }

    private void EnsureTransitionFlyout()
    {
        if (_transitionFlyout != null) return;

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

    // ── Arrangements flyout ──────────────────────────────────────────────────

    /// <summary>Persists browse-stack header strip visibility when the section-groups toggle changes.</summary>
    private void ArrangementsToggle_CheckStateChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: ShowPresentationDeckSection section } tb)
            ViewModel.SetBrowseStackArrangementExpanded(section.PresentationPath, tb.IsChecked == true);
    }

    /// <summary>Opens the arrangements editor flyout (named arrangements + group order).</summary>
    private void ArrangementsEditButton_Click(object sender, RoutedEventArgs e)
    {
        var presentationPath = (sender as Button)?.Tag is ShowPresentationDeckSection section
            ? section.PresentationPath
            : null;

        // New flyout per open so multiple presentation headers can keep editor flyouts open at once.
        var content = new ArrangementsDialogContent();
        content.Initialize(ViewModel, presentationPath);
        var flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = BuildFlyoutPresenterStyle(minWidth: 340, maxWidth: 420, padding: 12, cornerRadius: 8),
        };
        flyout.ShowAt((FrameworkElement)sender);
    }

    // ── Output clear configuration ───────────────────────────────────────────

    private void PrimaryOutputClearCommandBar_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement target)
            return;

        var menu = new MenuFlyout();
        AddConfigureClearGroupsItem(menu, target);
        menu.ShowAt(target);
        e.Handled = true;
    }

    private void SecondaryOutputClearCommandBar_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement target)
            return;

        var menu = new MenuFlyout();
        AddConfigureClearGroupsItem(menu, target);
        menu.ShowAt(target);
        e.Handled = true;
    }

    private void AddConfigureClearGroupsItem(MenuFlyout menu, FrameworkElement target)
    {
        var configureItem = new MenuFlyoutItem
        {
            Text = "Custom clear groups...",
            Icon = new FontIcon { Glyph = "\uE713" },
        };
        configureItem.Click += (_, _) => ShowClearGroupsFlyout(target);
        menu.Items.Add(configureItem);
    }

    private void ShowClearGroupsFlyout(FrameworkElement target)
    {
        EnsureClearGroupsFlyout();
        var editor = new ShowClearGroupsFlyoutViewModel(_outputRouting);
        editor.Load();
        _clearGroupsFlyoutContent!.Initialize(editor);
        _clearGroupsFlyout!.ShowAt(target);
    }

    private void EnsureClearGroupsFlyout()
    {
        if (_clearGroupsFlyout != null)
            return;

        _clearGroupsFlyoutContent = new ClearGroupsFlyoutContent();
        _clearGroupsFlyout = new Flyout
        {
            Content = _clearGroupsFlyoutContent,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = BuildFlyoutPresenterStyle(minWidth: 700, maxWidth: 840, padding: 0, cornerRadius: 8),
        };
        _clearGroupsFlyout.Closed += ClearGroupsFlyout_Closed;
    }

    private async void ClearGroupsFlyout_Closed(object? sender, object e)
    {
        _ = sender;
        _ = e;

        if (_clearGroupsFlyoutContent?.ViewModel != null)
            await _clearGroupsFlyoutContent.ViewModel.PersistClearGroupsAsync();
    }

    // ── Flyout helper ────────────────────────────────────────────────────────

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
