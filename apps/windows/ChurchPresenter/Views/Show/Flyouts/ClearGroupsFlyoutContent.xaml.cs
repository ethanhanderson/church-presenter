using System;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Views;

/// <summary>
/// Flyout content for creating and editing custom clear groups from the Show output panel.
/// </summary>
public sealed partial class ClearGroupsFlyoutContent : UserControl
{
    private bool _loading;

    /// <summary>Editor model bound to the flyout content.</summary>
    public ShowClearGroupsFlyoutViewModel? ViewModel { get; private set; }

    /// <summary>Creates the flyout content.</summary>
    public ClearGroupsFlyoutContent()
    {
        InitializeComponent();
    }

    /// <summary>Initializes the flyout with the active-Look clear groups.</summary>
    public void Initialize(ShowClearGroupsFlyoutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _loading = true;
        ViewModel = viewModel;
        DataContext = viewModel;
        ClearGroupsListView.SelectedItem = viewModel.SelectedClearGroup;
        SyncTintSelection();
        _loading = false;
    }

    private async void AddClearGroup_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (ViewModel == null)
            return;

        await ViewModel.AddClearGroupAsync();
        ClearGroupsListView.ScrollIntoView(ViewModel.SelectedClearGroup);
    }

    private void ClearGroupsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        SyncTintSelection();
    }

    private void ClearGroupsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        _ = sender;

        if (args.ItemContainer is not ListViewItem itemContainer)
            return;

        itemContainer.PointerEntered -= ClearGroupItemContainer_PointerEntered;
        itemContainer.PointerExited -= ClearGroupItemContainer_PointerExited;
        itemContainer.GotFocus -= ClearGroupItemContainer_GotFocus;
        itemContainer.LostFocus -= ClearGroupItemContainer_LostFocus;

        if (args.InRecycleQueue)
        {
            SetDeleteButtonVisible(itemContainer, false);
            return;
        }

        itemContainer.PointerEntered += ClearGroupItemContainer_PointerEntered;
        itemContainer.PointerExited += ClearGroupItemContainer_PointerExited;
        itemContainer.GotFocus += ClearGroupItemContainer_GotFocus;
        itemContainer.LostFocus += ClearGroupItemContainer_LostFocus;
        SetDeleteButtonVisible(itemContainer, false);
    }

    private async void DeleteClearGroup_Click(object sender, RoutedEventArgs e)
    {
        _ = e;

        if (ViewModel == null || sender is not Button { Tag: ClearGroupSettingsItem item })
            return;

        await ViewModel.DeleteClearGroupAsync(item);
    }

    private void ClearGroupItemContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = e;
        SetDeleteButtonVisible(sender, true);
    }

    private void ClearGroupItemContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _ = e;
        SetDeleteButtonVisible(sender, false);
    }

    private void ClearGroupItemContainer_GotFocus(object sender, RoutedEventArgs e)
    {
        _ = e;
        SetDeleteButtonVisible(sender, true);
    }

    private void ClearGroupItemContainer_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = e;
        SetDeleteButtonVisible(sender, false);
    }

    private static void SetDeleteButtonVisible(object sender, bool visible)
    {
        if (sender is DependencyObject root
            && FindDescendantByName<Button>(root, "DeleteClearGroupButton") is Button deleteButton)
        {
            deleteButton.Opacity = visible ? 1 : 0;
            deleteButton.IsHitTestVisible = visible;
        }
    }

    private static T? FindDescendantByName<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
                return element;

            T? descendant = FindDescendantByName<T>(child, name);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private void PersistClearGroups_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueuePersist();
    }

    private void PersistClearGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueuePersist();
    }

    private void TintComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_loading || ViewModel?.SelectedClearGroup == null)
            return;

        string tintColor = TintComboBox.SelectedValue as string ?? string.Empty;
        ViewModel.SelectedClearGroup.TintEnabled = !string.IsNullOrWhiteSpace(tintColor);
        ViewModel.SelectedClearGroup.TintColor = tintColor;
        QueuePersist();
    }

    private void PersistClearGroups_CheckChanged(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueuePersist();
    }

    private void QueuePersist()
    {
        if (_loading || ViewModel == null)
            return;

        DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _ = dispatcherQueue.TryEnqueue(async () =>
        {
            if (ViewModel != null)
                await ViewModel.PersistClearGroupsAsync();
        });
    }

    private void SyncTintSelection()
    {
        if (ViewModel?.SelectedClearGroup == null)
        {
            TintComboBox.SelectedValue = string.Empty;
            return;
        }

        TintComboBox.SelectedValue = ViewModel.SelectedClearGroup.TintEnabled
            ? ViewModel.SelectedClearGroup.TintColor
            : string.Empty;
    }
}
