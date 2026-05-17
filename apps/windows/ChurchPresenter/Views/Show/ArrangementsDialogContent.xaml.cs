using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace ChurchPresenter.Views;

/// <summary>
/// UserControl displayed inside a flyout for managing named group arrangements.
/// </summary>
public sealed partial class ArrangementsDialogContent : UserControl
{
    private const string PaletteDragFormat = "application/x-churchpresenter-section-group-id";

    private ShowViewModel? _viewModel;
    private string? _presentationPath;
    private readonly ObservableCollection<ArrangementGroupEntry> _groups = new();
    private readonly ObservableCollection<ArrangementPaletteEntry> _paletteGroups = new();
    private bool _suppressComboChange;
    private bool _paletteCanDrag;

    public ArrangementsDialogContent()
    {
        InitializeComponent();
        GroupsListView.ItemsSource = _groups;
        PaletteItemsControl.ItemsSource = _paletteGroups;
        GroupsListView.DragOver += GroupsListView_DragOver;
        GroupsListView.Drop += GroupsListView_Drop;
    }

    /// <summary>Bind the dialog to the active ViewModel (called before showing the dialog).</summary>
    public void Initialize(ShowViewModel viewModel, string? presentationPath = null)
    {
        _viewModel = viewModel;
        _presentationPath = presentationPath;
        PopulateArrangementCombo();
    }

    private NamedArrangement? SelectedArrangement =>
        (ArrangementCombo.SelectedItem as ArrangementComboEntry)?.Arrangement;

    private string? CurrentPresentationPath =>
        !string.IsNullOrWhiteSpace(_presentationPath)
            ? _presentationPath
            : _viewModel?.SelectedPresentationPath ?? _viewModel?.OpenDocument?.SourcePath;

    private void PopulateArrangementCombo()
    {
        _suppressComboChange = true;
        ArrangementCombo.ItemsSource = null;
        if (_viewModel == null)
        {
            _suppressComboChange = false;
            return;
        }

        var project = _viewModel.GetProjectForPath(CurrentPresentationPath);
        List<NamedArrangement> arrangements = project?.Arrangement?.Arrangements?.ToList() ?? new List<NamedArrangement>();
        var active = ResolveActiveArrangement(project);
        var hasCustom = arrangements.Any(a => !a.IsNatural);

        var masterEntry = arrangements
            .Where(a => a.IsNatural)
            .Select(a => new ArrangementComboEntry(a))
            .FirstOrDefault();

        var entries = arrangements
            .Where(a => !a.IsNatural)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new ArrangementComboEntry(a))
            .ToList();

        if (masterEntry != null)
            entries.Insert(0, masterEntry);

        ArrangementCombo.ItemsSource = entries;
        if (active != null)
        {
            ArrangementCombo.SelectedItem = entries.FirstOrDefault(e =>
                string.Equals(e.Arrangement.Id, active.Id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            ArrangementCombo.SelectedItem = null;
        }

        UpdateVisibleState(hasCustom, active, entries.Count != 0);

        _suppressComboChange = false;
        LoadGroupsForSelected();
    }

    private void LoadGroupsForSelected()
    {
        _groups.Clear();
        var arr = SelectedArrangement;
        var canReorder = arr != null && !arr.IsNatural;
        // ListViewBase.CanReorderItems remarks: AllowDrop and CanReorderItems must both be true; CanDragItems for DragItemsCompleted;
        // IsSwipeEnabled false => mouse reorder only (no touch).
        GroupsListView.AllowDrop = canReorder;
        GroupsListView.CanReorderItems = canReorder;
        GroupsListView.CanDragItems = canReorder;
        GroupsListView.IsSwipeEnabled = canReorder;
        _paletteCanDrag = canReorder;

        var project = _viewModel?.GetProjectForPath(CurrentPresentationPath);
        if (project?.Arrangement?.Sections == null)
        {
            _paletteGroups.Clear();
            NotifyBoolState();
            return;
        }

        if (arr == null)
        {
            _paletteGroups.Clear();
            _groups.Clear();
            NotifyBoolState();
            return;
        }

        var sectionMap = project.Arrangement.Sections
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

        _paletteGroups.Clear();
        foreach (var section in project.Arrangement.Sections)
        {
            var hex = SlideGroupThumbnailColors.GetHexColorForSectionGroup(section, project);
            var (bg, fg) = canReorder
                ? SectionGroupChipDisplay.CreateBrushesFromHex(hex)
                : SectionGroupChipDisplay.CreateBrushesForFixedOrder(hex);
            _paletteGroups.Add(new ArrangementPaletteEntry
            {
                SectionGroupId = section.Id,
                Label = section.Label,
                BackgroundBrush = bg,
                ForegroundBrush = fg,
                CanDrag = canReorder,
            });
        }

        foreach (var groupRef in arr.Groups)
        {
            if (sectionMap.TryGetValue(groupRef.SectionGroupId, out var section))
            {
                var hex = SlideGroupThumbnailColors.GetHexColorForSectionGroup(section, project);
                var (bg, fg) = canReorder
                    ? SectionGroupChipDisplay.CreateBrushesFromHex(hex)
                    : SectionGroupChipDisplay.CreateBrushesForFixedOrder(hex);
                _groups.Add(new ArrangementGroupEntry
                {
                    SectionGroupId = groupRef.SectionGroupId,
                    Label = section.Label,
                    BackgroundBrush = bg,
                    ForegroundBrush = fg,
                    IsReorderLocked = !canReorder,
                });
            }
        }

        NotifyBoolState();
    }

    private async Task DeleteSelectedArrangementAsync()
    {
        var entry = ArrangementCombo.SelectedItem as ArrangementComboEntry;
        if (entry == null || !entry.IsDeletable)
            return;

        var path = CurrentPresentationPath;
        if (string.IsNullOrWhiteSpace(path) || _viewModel == null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Delete arrangement",
            Content = $"Delete “{entry.DisplayName}”? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await _viewModel.DeleteArrangementForPathAsync(path, entry.Arrangement.Id).ConfigureAwait(true);
        PopulateArrangementCombo();
    }

    private async void RemoveArrangementButton_Click(object sender, RoutedEventArgs e) =>
        await DeleteSelectedArrangementAsync().ConfigureAwait(true);

    private void ArrangementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboChange) return;
        UpdateVisibleState(
            hasCustom: ArrangementCombo.Items.OfType<ArrangementComboEntry>().Any(entry => !entry.Arrangement.IsNatural),
            selectedArrangement: SelectedArrangement,
            hasEntries: ArrangementCombo.Items.Count > 0);
        LoadGroupsForSelected();
    }

    private void UpdateVisibleState(bool hasCustom, NamedArrangement? selectedArrangement, bool hasEntries)
    {
        var showEmptyOnly = !hasCustom;
        var showEditor = hasCustom && selectedArrangement is { IsNatural: false };
        var canDelete = showEditor && selectedArrangement is { IsNatural: false };

        PickerRow.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = showEmptyOnly ? Visibility.Visible : Visibility.Collapsed;
        PaletteBorder.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        OrderListBorder.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        RemoveArrangementButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PaletteChip_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (!_paletteCanDrag || sender is not FrameworkElement { Tag: string id } || string.IsNullOrEmpty(id))
            return;
        args.Data.SetData(PaletteDragFormat, id);
        args.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void GroupsListView_DragOver(object sender, DragEventArgs e)
    {
        if (!_paletteCanDrag)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (e.DataView.Contains(PaletteDragFormat))
            e.AcceptedOperation = DataPackageOperation.Copy;
        else
            e.AcceptedOperation = DataPackageOperation.None;
    }

    private async void GroupsListView_Drop(object sender, DragEventArgs e)
    {
        if (!_paletteCanDrag || sender is not ListView lv)
            return;

        if (!e.DataView.Contains(PaletteDragFormat))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var id = await e.DataView.GetDataAsync(PaletteDragFormat) as string;
            if (string.IsNullOrEmpty(id))
                return;

            var insertIndex = GetInsertIndexForDrop(lv, e);
            var project = _viewModel?.GetProjectForPath(CurrentPresentationPath);
            var sectionMap = project?.Arrangement?.Sections
                .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
            if (sectionMap == null || project == null || !sectionMap.TryGetValue(id, out var section))
                return;

            var hex = SlideGroupThumbnailColors.GetHexColorForSectionGroup(section, project);
            var (bg, fg) = SectionGroupChipDisplay.CreateBrushesFromHex(hex);
            var entry = new ArrangementGroupEntry
            {
                SectionGroupId = section.Id,
                Label = section.Label,
                BackgroundBrush = bg,
                ForegroundBrush = fg,
                IsReorderLocked = false,
            };

            insertIndex = Math.Clamp(insertIndex, 0, _groups.Count);
            _groups.Insert(insertIndex, entry);
            CommitGroupOrder();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private int GetInsertIndexForDrop(ListView listView, DragEventArgs e)
    {
        var posInList = e.GetPosition(listView);
        for (var i = 0; i < _groups.Count; i++)
        {
            var container = listView.ContainerFromIndex(i) as ListViewItem;
            if (container == null)
                continue;
            var topLeft = container.TransformToVisual(listView).TransformPoint(new Point(0, 0));
            var midY = topLeft.Y + container.ActualHeight * 0.5;
            if (posInList.Y < midY)
                return i;
        }

        return _groups.Count;
    }

    private void NotifyBoolState()
    {
        // Bindings is null until the control is in the tree (Loaded); Initialize runs before Flyout.ShowAt.
        Bindings?.Update();
    }

    private void GroupsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // ListView has already applied the new order to _groups when the drag completes.
        CommitGroupOrder();
    }

    private void RemoveGroupRow_Click(object sender, RoutedEventArgs e)
    {
        if (!_paletteCanDrag)
            return;
        if (sender is not FrameworkElement { DataContext: ArrangementGroupEntry entry })
            return;
        if (!_groups.Remove(entry))
            return;
        CommitGroupOrder();
    }

    private async void NewArrangementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var nameBox = new TextBox { PlaceholderText = "Name", MinWidth = 280 };
        var hint = new TextBlock
        {
            Text = "The new arrangement copies the section order from Master. Drag groups vertically to reorder; changes save when you drop.",
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(hint);
        panel.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            Title = "New arrangement",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
            return;

        var project = _viewModel.GetProjectForPath(CurrentPresentationPath);
        var naturalGroups = project?.Arrangement?.Sections
            .Select(s => new ArrangementGroupRef { SectionGroupId = s.Id })
            .ToList() ?? new List<ArrangementGroupRef>();

        var newArr = new NamedArrangement
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            IsNatural = false,
            Groups = naturalGroups,
        };

        var path = CurrentPresentationPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        await _viewModel.SaveArrangementForPathAsync(path, newArr).ConfigureAwait(true);
        PopulateArrangementCombo();
    }

    private void EmptyStateCreateButton_Click(object sender, RoutedEventArgs e) =>
        NewArrangementButton_Click(sender, e);

    private void CommitGroupOrder()
    {
        var arr = SelectedArrangement;
        var path = CurrentPresentationPath;
        if (arr == null || arr.IsNatural || _viewModel == null || string.IsNullOrWhiteSpace(path))
            return;

        arr.Groups = _groups.Select(g => new ArrangementGroupRef { SectionGroupId = g.SectionGroupId }).ToList();
        _ = _viewModel.SaveArrangementForPathAsync(path, arr);
    }

    private static NamedArrangement? ResolveActiveArrangement(PresentationProject? project)
    {
        var arrangements = project?.Arrangement?.Arrangements;
        if (arrangements == null || arrangements.Count == 0)
            return null;

        var activeId = project?.Arrangement?.ActiveArrangementId;
        return arrangements.FirstOrDefault(a => string.Equals(a.Id, activeId, StringComparison.OrdinalIgnoreCase))
            ?? arrangements.FirstOrDefault(a => a.IsNatural);
    }
}