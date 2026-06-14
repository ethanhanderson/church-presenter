using System;
using ChurchPresenter.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace ChurchPresenter.Features.Show.Output;

public sealed partial class ShowOutputColumnView
{
    private void PrimaryOutputClearCommandBar_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        ShowOutputClearConfigurationMenu(sender, e);

    private void SecondaryOutputClearCommandBar_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        ShowOutputClearConfigurationMenu(sender, e);

    private void ShowOutputClearConfigurationMenu(object sender, RightTappedRoutedEventArgs e)
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
