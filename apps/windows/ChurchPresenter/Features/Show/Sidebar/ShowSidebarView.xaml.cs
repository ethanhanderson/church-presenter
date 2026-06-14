using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls.Rendering.Engine;
using ChurchPresenter.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Features.Show.Sidebar;

public sealed partial class ShowSidebarView : NavigationView
{    private readonly ILocalCollectionService _localCollection;
    private readonly ICollectionPackageService _collectionPackages;
    private readonly IPresentationProjectService _projects;
    private readonly IContentDirectoryService _content;
    private readonly IPresentationItemActionService _presentationActions;
    private readonly ISidebarPresentationClipboardService _presentationClipboard;
    private readonly IThemeLibraryService _themeLibrary;
    private readonly IThemeApplicationService _themeApplier;
    private readonly IOutputTopologyService _topology;
    private readonly ILogger<ShowSidebarView> _logger;
    private const string SourceNavigationDragFormat = "application/x-churchpresenter-source-item";
    private const string PresentationNavigationDragFormat = "application/x-churchpresenter-presentation-ref";
    private const string SidebarDropBeforeIndicatorName = "SidebarDropBeforeIndicator";
    private const string SidebarDropAfterIndicatorName = "SidebarDropAfterIndicator";
    private bool _sourcesNavigationRefreshPending;
    private bool _suppressSourcesNavigationSelectionChanged;
    private string _sourcesSearchText = string.Empty;
    private List<ShowPresentationSearchSuggestion> _activeSourcesSearchSuggestions = new();
    private readonly Thickness _sidebarItemChromeMargin = new(-32, -6, -12, -6);
    private readonly Thickness _sidebarItemChromePadding = new(32, 6, 12, 6);
    private ShowSourceNavigationTag? _selectedSourceNavigationTag;
    private FrameworkElement? _activeSidebarDropTarget;
    private Border? _activeSidebarIntoDropOverlay;
    private SourceNavigationDragPayload? _activeSourceNavigationDragPayload;
    private PresentationNavigationDragPayload? _activePresentationNavigationDragPayload;
    private ShowSourceNavigationTag? _inlineRenameSourceTag;
    private TextBox? _inlineRenameTextBox;
    private bool _committingInlineSourceRename;
    private CancellationTokenSource? _pendingSourceNavigationSelection;

    private enum ShowContentAction
    {
        CreatePresentation,
        ImportPresentation,
        CreateLibrary,
        CreatePlaylist,
    }

    private enum SidebarDropIndicator
    {
        None,
        Before,
        After,
        Into,
    }
    private void CatalogCollections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleSourcesNavigationRefresh();
    }

    private void ViewModel_SourcesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName is nameof(ShowViewModel.SelectedLibraryId)
                or nameof(ShowViewModel.SelectedPlaylistId)
                or nameof(ShowViewModel.SelectedPresentationPath)
                or nameof(ShowViewModel.HasPresentationSources))
        {
            ScheduleSourcesNavigationRefresh();
        }
    }

    private void ScheduleSourcesNavigationRefresh()
    {
        if (_sourcesNavigationRefreshPending)
            return;

        _sourcesNavigationRefreshPending = true;
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                _sourcesNavigationRefreshPending = false;
                RebuildSourcesNavigation();
                UpdateSourcesNavigationSelection();
                RefreshSourcesSearchSuggestions();
            });
    }

    private void UpdateSourcesSearchBoxHost()
    {
        if (ViewModel == null)
            return;

        if (ViewModel.HasPresentationSources)
        {
            if (AutoSuggestBox != SourcesSearchBox)
                AutoSuggestBox = SourcesSearchBox;

            SourcesSearchBox.Visibility = Visibility.Visible;
            return;
        }

        if (!string.IsNullOrEmpty(_sourcesSearchText))
            _sourcesSearchText = string.Empty;

        if (!string.IsNullOrEmpty(SourcesSearchBox.Text))
            SourcesSearchBox.Text = string.Empty;

        SourcesSearchBox.ItemsSource = null;

        if (AutoSuggestBox == SourcesSearchBox)
            AutoSuggestBox = null;
    }

    private void RebuildSourcesNavigation()
    {
        if (ViewModel == null)
            return;

        _suppressSourcesNavigationSelectionChanged = true;
        try
        {
            MenuItems.Clear();
            var hasSearchFilter = !string.IsNullOrWhiteSpace(_sourcesSearchText);

            UpdateSourcesSearchBoxHost();
            UpdateContentActionButton();

            MenuItems.Add(CreateSourcesNavigationHeader("Libraries"));

            if (ViewModel.Libraries.Count > 0)
            {
                foreach (var library in ViewModel.Libraries)
                {
                    var matchingPresentations = GetMatchingPresentations(library.Presentations, _sourcesSearchText);
                    if (hasSearchFilter && matchingPresentations.Count == 0)
                        continue;

                    MenuItems.Add(CreateLibraryNavigationItem(library, matchingPresentations));
                }
            }
            else
            {
                MenuItems.Add(CreateEmptySourceNavigationItem(
                    "\uE8B7",
                    "No libraries yet"));
            }

            MenuItems.Add(CreateSourcesNavigationDivider());

            MenuItems.Add(CreateSourcesNavigationHeader("Playlists"));

            if (ViewModel.Playlists.Count > 0)
            {
                foreach (var playlist in ViewModel.Playlists)
                {
                    var matchingPresentations = GetMatchingPresentations(playlist.Items, _sourcesSearchText);
                    if (hasSearchFilter && matchingPresentations.Count == 0)
                        continue;

                    MenuItems.Add(CreatePlaylistNavigationItem(playlist, matchingPresentations));
                }
            }
            else
            {
                MenuItems.Add(CreateEmptySourceNavigationItem(
                    "\uE142",
                    "No playlists yet"));
            }
        }
        finally
        {
            _suppressSourcesNavigationSelectionChanged = false;
        }
    }

    private List<PresentationRefDto> GetMatchingPresentations(IEnumerable<PresentationRefDto> presentations, string? searchText)
    {
        var list = presentations.ToList();
        var query = searchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
            return list;

        return list
            .Where(p =>
                (!string.IsNullOrWhiteSpace(p.Title) && p.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(p.Path) && p.Path.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void UpdateContentActionButton()
    {
        var primaryAction = ResolveRecommendedContentAction();
        ShowContentSplitButton.Content = GetContentActionLabel(primaryAction);
        AutomationProperties.SetName(ShowContentSplitButton, GetContentActionAutomationName(primaryAction));
    }

    private ShowContentAction ResolveRecommendedContentAction()
    {
        if (ViewModel.Libraries.Count == 0)
            return ShowContentAction.CreateLibrary;

        if (!string.IsNullOrWhiteSpace(ViewModel.SelectedLibraryId)
            || !string.IsNullOrWhiteSpace(ViewModel.SelectedPlaylistId))
        {
            return ShowContentAction.CreatePresentation;
        }

        var presentationCount = ViewModel.Libraries.Sum(library => library.Presentations.Count);
        if (presentationCount == 0)
            return ShowContentAction.CreatePresentation;

        if (ViewModel.Playlists.Count == 0)
            return ShowContentAction.CreatePlaylist;

        return ShowContentAction.CreatePresentation;
    }

    private static string GetContentActionLabel(ShowContentAction action) =>
        action switch
        {
            ShowContentAction.CreateLibrary => "Create library",
            ShowContentAction.CreatePlaylist => "Create playlist",
            ShowContentAction.ImportPresentation => "Import presentation",
            _ => "Create presentation",
        };

    private static string GetContentActionAutomationName(ShowContentAction action) =>
        action switch
        {
            ShowContentAction.CreateLibrary => "Create library",
            ShowContentAction.CreatePlaylist => "Create playlist",
            ShowContentAction.ImportPresentation => "Import presentation",
            _ => "Create presentation",
        };

    private static NavigationViewItemHeader CreateSourcesNavigationHeader(string title) =>
        new()
        {
            Content = title,
        };

    private static NavigationViewItem CreateEmptySourceNavigationItem(string glyph, string label)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(2, 3, 0, 3),
        };
        content.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph = glyph,
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new NavigationViewItem
        {
            Content = content,
            IsHitTestVisible = false,
            SelectsOnInvoked = false,
        };
    }

    private NavigationViewItemSeparator CreateSourcesNavigationDivider()
    {
        return new NavigationViewItemSeparator
        {
            Opacity = 0,
            Height = 8,
        };
    }

    private NavigationViewItem CreateLibraryNavigationItem(LibraryDto library, IReadOnlyList<PresentationRefDto> presentations)
    {
        var tag = ShowSourceNavigationTag.ForLibrary(library.Id);
        var item = new NavigationViewItem
        {
            Content = CreateSourceNavigationItemContent(library.Name, tag),
            Tag = tag,
            InfoBadge = new InfoBadge { Value = presentations.Count },
            IsExpanded = string.Equals(ViewModel.SelectedLibraryId, library.Id, StringComparison.OrdinalIgnoreCase),
        };

        if (!string.IsNullOrWhiteSpace(library.Description))
            ToolTipService.SetToolTip(item, library.Description);

        AttachSourceNavigationCommands(item, tag);
        AttachSourceNavigationDropTarget(item);
        foreach (var presentation in presentations)
        {
            item.MenuItems.Add(CreatePresentationNavigationItem(
                presentation,
                libraryId: library.Id,
                playlistId: null,
                playlistIndex: -1));
        }

        return item;
    }

    private NavigationViewItem CreatePlaylistNavigationItem(PlaylistDto playlist, IReadOnlyList<PresentationRefDto> presentations)
    {
        var tag = ShowSourceNavigationTag.ForPlaylist(playlist.Id);
        var item = new NavigationViewItem
        {
            Content = CreateSourceNavigationItemContent(playlist.Name, tag),
            Tag = tag,
            InfoBadge = new InfoBadge { Value = presentations.Count },
            IsExpanded = string.Equals(ViewModel.SelectedPlaylistId, playlist.Id, StringComparison.OrdinalIgnoreCase),
        };

        if (!string.IsNullOrWhiteSpace(playlist.Description))
            ToolTipService.SetToolTip(item, playlist.Description);

        AttachSourceNavigationCommands(item, tag);
        AttachSourceNavigationDropTarget(item);
        foreach (var presentation in presentations)
        {
            item.MenuItems.Add(CreatePresentationNavigationItem(
                presentation,
                libraryId: null,
                playlistId: playlist.Id,
                playlistIndex: playlist.Items.IndexOf(presentation)));
        }

        return item;
    }

    private object CreateSourceNavigationItemContent(string displayName, ShowSourceNavigationTag tag)
    {
        if (!Equals(_inlineRenameSourceTag, tag))
        {
            var row = CreateSidebarNavigationRow(tag);
            var content = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = false,
            };
            content.Children.Add(new TextBlock
            {
                Text = displayName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            AddSidebarDropTargetAdorners(content);
            row.Child = content;
            AttachSourceNavigationDragSource(row);
            AttachSourceNavigationDropTarget(row);
            return row;
        }

        var textBox = new TextBox
        {
            Text = displayName,
            Tag = tag,
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            SelectionStart = 0,
            SelectionLength = displayName.Length,
        };

        textBox.KeyDown += SourceInlineRenameTextBox_KeyDown;
        textBox.LostFocus += SourceInlineRenameTextBox_LostFocus;
        textBox.Loaded += SourceInlineRenameTextBox_Loaded;
        _inlineRenameTextBox = textBox;
        return textBox;
    }

    private void AttachSourceNavigationCommands(NavigationViewItem item, ShowSourceNavigationTag tag)
    {
        item.ContextFlyout = CreateSourceNavigationContextFlyout(tag);
        item.DoubleTapped += SourceNavigationItem_DoubleTapped;

        item.KeyboardAccelerators.Add(CreateSourceKeyboardAccelerator(
            VirtualKey.F2,
            VirtualKeyModifiers.None,
            () => BeginSourceInlineRename(tag)));

        item.KeyboardAccelerators.Add(CreateSourceKeyboardAccelerator(
            VirtualKey.Delete,
            VirtualKeyModifiers.None,
            () => _ = DeleteSourceAsync(tag)));

        if (tag.Kind == ShowSourceNavigationKind.Playlist)
        {
            item.KeyboardAccelerators.Add(CreateSourceKeyboardAccelerator(
                VirtualKey.D,
                VirtualKeyModifiers.Control,
                () => _ = DuplicatePlaylistSourceAsync(tag)));
        }
    }

    private MenuFlyout CreateSourceNavigationContextFlyout(ShowSourceNavigationTag tag)
    {
        var flyout = new MenuFlyout();

        flyout.Items.Add(CreateSourceMenuItem(
            "Rename",
            Symbol.Edit,
            VirtualKey.F2,
            VirtualKeyModifiers.None,
            () => BeginSourceInlineRename(tag)));

        if (tag.Kind == ShowSourceNavigationKind.Playlist)
        {
            flyout.Items.Add(CreateSourceMenuItem(
                "Duplicate",
                Symbol.Copy,
                VirtualKey.D,
                VirtualKeyModifiers.Control,
                () => _ = DuplicatePlaylistSourceAsync(tag)));
        }

        flyout.Items.Add(CreateSourceMenuItem(
            "Delete",
            Symbol.Delete,
            VirtualKey.Delete,
            VirtualKeyModifiers.None,
            () => _ = DeleteSourceAsync(tag)));

        return flyout;
    }

    private static MenuFlyoutItem CreateSourceMenuItem(
        string text,
        Symbol icon,
        VirtualKey acceleratorKey,
        VirtualKeyModifiers acceleratorModifiers,
        Action execute)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new SymbolIcon(icon),
        };

        var accelerator = new KeyboardAccelerator
        {
            Key = acceleratorKey,
            Modifiers = acceleratorModifiers,
        };

        accelerator.Invoked += (_, args) =>
        {
            execute();
            args.Handled = true;
        };

        item.KeyboardAccelerators.Add(accelerator);
        item.Click += (_, _) => execute();
        return item;
    }

    private static KeyboardAccelerator CreateSourceKeyboardAccelerator(
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        Action execute)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
        };

        accelerator.Invoked += (_, args) =>
        {
            execute();
            args.Handled = true;
        };
        return accelerator;
    }

    private void AttachPresentationNavigationCommands(NavigationViewItem item, ShowSourceNavigationTag tag)
    {
        item.ContextFlyout = CreatePresentationNavigationContextFlyout(tag);
        item.DoubleTapped += PresentationNavigationItem_DoubleTapped;

        item.KeyboardAccelerators.Add(CreatePresentationKeyboardAccelerator(
            VirtualKey.C,
            VirtualKeyModifiers.Control,
            tag,
            item =>
            {
                CopyPresentation(item);
                return Task.CompletedTask;
            }));
        item.KeyboardAccelerators.Add(CreatePresentationKeyboardAccelerator(
            VirtualKey.V,
            VirtualKeyModifiers.Control,
            tag,
            item => PastePresentationAsync(item)));
        item.KeyboardAccelerators.Add(CreatePresentationKeyboardAccelerator(
            VirtualKey.Delete,
            VirtualKeyModifiers.None,
            tag,
            item => RemovePresentationFromSourceAsync(item)));
        item.KeyboardAccelerators.Add(CreatePresentationKeyboardAccelerator(
            VirtualKey.D,
            VirtualKeyModifiers.Control,
            tag,
            item => DuplicatePresentationAsync(item)));
        item.KeyboardAccelerators.Add(CreatePresentationKeyboardAccelerator(
            VirtualKey.F2,
            VirtualKeyModifiers.None,
            tag,
            _ =>
            {
                BeginSourceInlineRename(tag);
                return Task.CompletedTask;
            }));
    }

    private void AttachSourceNavigationDragSource(UIElement item)
    {
        item.CanDrag = true;
        item.DragStarting += SourceNavigationItem_DragStarting;
        item.DropCompleted += SidebarNavigationItem_DropCompleted;
    }

    private void AttachSourceNavigationDropTarget(UIElement item)
    {
        item.AllowDrop = true;
        item.DragOver += SourceNavigationItem_DragOver;
        item.DragLeave += SidebarNavigationItem_DragLeave;
        item.Drop += SourceNavigationItem_Drop;
    }

    private void AttachPresentationNavigationDragSource(UIElement item)
    {
        item.CanDrag = true;
        item.DragStarting += PresentationNavigationItem_DragStarting;
        item.DropCompleted += SidebarNavigationItem_DropCompleted;
    }

    private void AttachPresentationNavigationDropTarget(UIElement item)
    {
        item.AllowDrop = true;
        item.DragOver += PresentationNavigationItem_DragOver;
        item.DragLeave += SidebarNavigationItem_DragLeave;
        item.Drop += PresentationNavigationItem_Drop;
    }

    private void SourceNavigationItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        ClearActiveSidebarDropIndicator();
        if (!string.IsNullOrWhiteSpace(_sourcesSearchText)
            || TryGetSourceNavigationTag(sender, out var tag) is false
            || tag.Kind is not (ShowSourceNavigationKind.Library or ShowSourceNavigationKind.Playlist))
        {
            args.Cancel = true;
            return;
        }

        var sourceId = tag.LibraryId ?? tag.PlaylistId;
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            args.Cancel = true;
            return;
        }

        var payload = new SourceNavigationDragPayload(tag.Kind, sourceId);
        _activeSourceNavigationDragPayload = payload;
        args.Data.SetData(SourceNavigationDragFormat, JsonSerializer.Serialize(payload));
        args.AllowedOperations = DataPackageOperation.Move;
    }

    private async void PresentationNavigationItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        ClearActiveSidebarDropIndicator();
        if (TryGetSourceNavigationTag(sender, out var tag) is false
            || tag.Kind != ShowSourceNavigationKind.Presentation
            || ResolvePresentationTreeItem(tag) is not { } item)
        {
            args.Cancel = true;
            return;
        }

        var payload = new PresentationNavigationDragPayload(
            item.Presentation.Path,
            item.Presentation.Title,
            item.LibraryId,
            item.PlaylistId,
            item.PlaylistIndex);
        _activePresentationNavigationDragPayload = payload;
        args.Data.SetData(PresentationNavigationDragFormat, JsonSerializer.Serialize(payload));
        args.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move;

        var deferral = args.GetDeferral();
        try
        {
            var bitmap = await TryCreatePresentationDragCardBitmapAsync(payload).ConfigureAwait(true);
            if (bitmap != null)
                args.DragUI.SetContentFromSoftwareBitmap(bitmap, args.GetPosition(sender));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not build presentation drag preview.");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void SourceNavigationItem_DragOver(object sender, DragEventArgs e)
    {
        if (TryGetSourceNavigationTag(sender, out var targetTag) is false)
        {
            ClearActiveSidebarDropIndicator();
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        var targetElement = ResolveSidebarDropTargetElement(sender as FrameworkElement);

        if (e.DataView.Contains(SourceNavigationDragFormat))
        {
            var sourcePayload = _activeSourceNavigationDragPayload;
            e.AcceptedOperation = CanReorderSourceDrop(targetTag)
                && sourcePayload is { } activeSourcePayload
                && activeSourcePayload.Kind == targetTag.Kind
                && !IsSameSourceNavigationDragTarget(activeSourcePayload, targetTag)
                ? DataPackageOperation.Move
                : DataPackageOperation.None;
            if (e.AcceptedOperation == DataPackageOperation.Move)
            {
                SetSidebarDropIndicator(targetElement, ResolveDropIndicator(targetElement, e));
                e.DragUIOverride.Caption = targetTag.Kind == ShowSourceNavigationKind.Library
                    ? "Reorder library"
                    : "Reorder playlist";
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                ClearActiveSidebarDropIndicator();
            }

            e.Handled = true;
            return;
        }

        if (!e.DataView.Contains(PresentationNavigationDragFormat))
        {
            ClearActiveSidebarDropIndicator();
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        var canDropPresentationOnSource = _activePresentationNavigationDragPayload != null
            && CanDropPresentationOnSource(_activePresentationNavigationDragPayload, targetTag);

        e.AcceptedOperation = canDropPresentationOnSource
            ? targetTag.Kind switch
            {
                ShowSourceNavigationKind.Library => DataPackageOperation.Move,
                ShowSourceNavigationKind.Playlist => DataPackageOperation.Copy,
                _ => DataPackageOperation.None,
            }
            : DataPackageOperation.None;

        if (e.AcceptedOperation != DataPackageOperation.None)
        {
            SetSidebarDropIndicator(targetElement, SidebarDropIndicator.Into);
            e.DragUIOverride.Caption = targetTag.Kind == ShowSourceNavigationKind.Library
                ? "Move to library"
                : "Add to playlist";
            e.DragUIOverride.IsCaptionVisible = true;
        }
        else
        {
            ClearActiveSidebarDropIndicator();
        }

        e.Handled = true;
    }

    private async void SourceNavigationItem_Drop(object sender, DragEventArgs e)
    {
        var targetElement = ResolveSidebarDropTargetElement(sender as FrameworkElement);
        if (targetElement == null || TryGetSourceNavigationTag(sender, out var targetTag) is false)
            return;

        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(SourceNavigationDragFormat))
            {
                var payload = await ReadDragPayloadAsync<SourceNavigationDragPayload>(e, SourceNavigationDragFormat).ConfigureAwait(true);
                if (payload != null)
                    await DropSourceOnSourceAsync(payload, targetTag, targetElement, e).ConfigureAwait(true);
                return;
            }

            if (e.DataView.Contains(PresentationNavigationDragFormat))
            {
                var payload = await ReadDragPayloadAsync<PresentationNavigationDragPayload>(e, PresentationNavigationDragFormat).ConfigureAwait(true);
                if (payload != null)
                    await DropPresentationOnSourceAsync(payload, targetTag).ConfigureAwait(true);
            }
        }
        finally
        {
            ClearActiveSidebarDropIndicator();
            ClearSidebarDragPayloads();
            e.Handled = true;
            deferral.Complete();
        }
    }

    private void PresentationNavigationItem_DragOver(object sender, DragEventArgs e)
    {
        var targetElement = ResolveSidebarDropTargetElement(sender as FrameworkElement);
        if (targetElement == null || TryGetSourceNavigationTag(sender, out var targetTag) is false)
        {
            ClearActiveSidebarDropIndicator();
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (!e.DataView.Contains(PresentationNavigationDragFormat))
        {
            ClearActiveSidebarDropIndicator();
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = targetTag.Kind == ShowSourceNavigationKind.Presentation
            && _activePresentationNavigationDragPayload != null
            && !IsSamePresentationNavigationDragTarget(_activePresentationNavigationDragPayload, targetTag)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
        if (e.AcceptedOperation == DataPackageOperation.Move)
        {
            SetSidebarDropIndicator(targetElement, ResolveDropIndicator(targetElement, e));
            e.DragUIOverride.Caption = targetTag.PlaylistId == null
                ? "Place in library"
                : "Place in playlist";
            e.DragUIOverride.IsCaptionVisible = true;
        }
        else
        {
            ClearActiveSidebarDropIndicator();
        }

        e.Handled = true;
    }

    private async void PresentationNavigationItem_Drop(object sender, DragEventArgs e)
    {
        var targetElement = ResolveSidebarDropTargetElement(sender as FrameworkElement);
        if (targetElement == null || TryGetSourceNavigationTag(sender, out var targetTag) is false)
            return;

        if (!e.DataView.Contains(PresentationNavigationDragFormat))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var payload = await ReadDragPayloadAsync<PresentationNavigationDragPayload>(e, PresentationNavigationDragFormat).ConfigureAwait(true);
            if (payload != null)
                await DropPresentationOnPresentationAsync(payload, targetTag, targetElement, e).ConfigureAwait(true);
        }
        finally
        {
            ClearActiveSidebarDropIndicator();
            ClearSidebarDragPayloads();
            e.Handled = true;
            deferral.Complete();
        }
    }

    private void SidebarNavigationItem_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(ResolveSidebarDropTargetElement(sender as FrameworkElement), _activeSidebarDropTarget))
            ClearActiveSidebarDropIndicator();
    }

    private void SidebarNavigationItem_DropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        ClearActiveSidebarDropIndicator();
        ClearSidebarDragPayloads();
    }

    private void ClearSidebarDragPayloads()
    {
        _activeSourceNavigationDragPayload = null;
        _activePresentationNavigationDragPayload = null;
    }

    private Border CreateSidebarNavigationRow(ShowSourceNavigationTag tag)
    {
        var row = new Border
        {
            Tag = tag,
            Margin = _sidebarItemChromeMargin,
            Padding = _sidebarItemChromePadding,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        row.Tapped += SidebarNavigationRow_Tapped;
        return row;
    }

    private void SidebarNavigationRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowSourceNavigationTag tag })
            ScheduleSourceNavigationSelection(tag);
    }

    private static FrameworkElement? ResolveSidebarDropTargetElement(FrameworkElement? element)
    {
        if (element == null)
            return null;

        return FindOwningNavigationViewItem(element) ?? element;
    }

    private static NavigationViewItem? FindOwningNavigationViewItem(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is NavigationViewItem item)
                return item;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void AddSidebarDropTargetAdorners(Grid row)
    {
        var accentBrush = LookupThemeBrush("AccentFillColorDefaultBrush")
            ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);

        row.Children.Add(new Border
        {
            Name = SidebarDropBeforeIndicatorName,
            Background = accentBrush,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(-_sidebarItemChromePadding.Left, -8, -_sidebarItemChromePadding.Right, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        });

        row.Children.Add(new Border
        {
            Name = SidebarDropAfterIndicatorName,
            Background = accentBrush,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(-_sidebarItemChromePadding.Left, 0, -_sidebarItemChromePadding.Right, -8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        });
    }

    private void SetSidebarDropIndicator(FrameworkElement? target, SidebarDropIndicator indicator)
    {
        if (target == null || indicator == SidebarDropIndicator.None)
        {
            ClearActiveSidebarDropIndicator();
            return;
        }

        if (!ReferenceEquals(_activeSidebarDropTarget, target))
            ClearActiveSidebarDropIndicator();

        ClearSidebarDropIndicator(target);
        if (indicator == SidebarDropIndicator.Into)
        {
            ShowSidebarIntoDropOverlay(target);
            _activeSidebarDropTarget = target;
            return;
        }

        var indicatorName = indicator switch
        {
            SidebarDropIndicator.Before => SidebarDropBeforeIndicatorName,
            SidebarDropIndicator.After => SidebarDropAfterIndicatorName,
            _ => null,
        };

        if (indicatorName != null && FindSidebarDropIndicator(target, indicatorName) is { } border)
        {
            border.Visibility = Visibility.Visible;
            _activeSidebarDropTarget = target;
        }
    }

    private void ClearActiveSidebarDropIndicator()
    {
        if (_activeSidebarDropTarget != null)
            ClearSidebarDropIndicator(_activeSidebarDropTarget);
        _activeSidebarDropTarget = null;
    }

    private void ClearSidebarDropIndicator(FrameworkElement target)
    {
        ClearSidebarIntoDropOverlay();

        if (GetSidebarDropIndicatorPanel(target) is not { } panel)
            return;

        foreach (var border in panel.Children.OfType<Border>())
        {
            if (border.Name is SidebarDropBeforeIndicatorName
                or SidebarDropAfterIndicatorName)
            {
                border.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowSidebarIntoDropOverlay(FrameworkElement target)
    {
        if (DropOverlayRoot is null)
            return;

        if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return;

        var position = target.TransformToVisual(DropOverlayRoot).TransformPoint(new Point(0, 0));
        var inset = 2d;
        var overlay = _activeSidebarIntoDropOverlay ??= CreateSidebarIntoDropOverlay();
        overlay.Width = Math.Max(0, target.ActualWidth - inset * 2);
        overlay.Height = Math.Max(0, target.ActualHeight - inset * 2);
        overlay.Margin = new Thickness(position.X + inset, position.Y + inset, 0, 0);
        overlay.Visibility = Visibility.Visible;

        if (!DropOverlayRoot.Children.Contains(overlay))
            DropOverlayRoot.Children.Add(overlay);
    }

    private Border CreateSidebarIntoDropOverlay()
    {
        var accentBrush = LookupThemeBrush("AccentFillColorDefaultBrush")
            ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        var overlay = new Border
        {
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        Grid.SetColumn(overlay, 0);
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        Canvas.SetZIndex(overlay, 100);
        return overlay;
    }

    private void ClearSidebarIntoDropOverlay()
    {
        if (_activeSidebarIntoDropOverlay == null || DropOverlayRoot is null)
            return;

        DropOverlayRoot.Children.Remove(_activeSidebarIntoDropOverlay);
        _activeSidebarIntoDropOverlay.Visibility = Visibility.Collapsed;
    }

    private static Border? FindSidebarDropIndicator(FrameworkElement target, string name)
    {
        return GetSidebarDropIndicatorPanel(target) is { } panel
            ? panel.Children.OfType<Border>().FirstOrDefault(border => string.Equals(border.Name, name, StringComparison.Ordinal))
            : null;
    }

    private static Panel? GetSidebarDropIndicatorPanel(FrameworkElement target) =>
        target switch
        {
            Panel panel => panel,
            Border { Child: Panel panel } => panel,
            NavigationViewItem { Content: Border { Child: Panel panel } } => panel,
            _ => null,
        };

    private static SidebarDropIndicator ResolveDropIndicator(FrameworkElement? targetElement, DragEventArgs e)
    {
        if (targetElement == null || targetElement.ActualHeight <= 0)
            return SidebarDropIndicator.Before;

        var position = e.GetPosition(targetElement);
        return position.Y > targetElement.ActualHeight * 0.5
            ? SidebarDropIndicator.After
            : SidebarDropIndicator.Before;
    }

    private bool CanReorderSourceDrop(ShowSourceNavigationTag targetTag) =>
        string.IsNullOrWhiteSpace(_sourcesSearchText)
        && targetTag.Kind is ShowSourceNavigationKind.Library or ShowSourceNavigationKind.Playlist;

    private bool CanDropPresentationOnSource(PresentationNavigationDragPayload payload, ShowSourceNavigationTag targetTag)
    {
        return targetTag.Kind switch
        {
            ShowSourceNavigationKind.Library => !string.IsNullOrWhiteSpace(targetTag.LibraryId)
                && !string.Equals(payload.SourceLibraryId, targetTag.LibraryId, StringComparison.OrdinalIgnoreCase),
            ShowSourceNavigationKind.Playlist => !string.IsNullOrWhiteSpace(targetTag.PlaylistId)
                && !string.Equals(payload.SourcePlaylistId, targetTag.PlaylistId, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool IsSameSourceNavigationDragTarget(SourceNavigationDragPayload payload, ShowSourceNavigationTag targetTag)
    {
        var targetId = targetTag.LibraryId ?? targetTag.PlaylistId;
        return string.Equals(payload.SourceId, targetId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSamePresentationNavigationDragTarget(PresentationNavigationDragPayload payload, ShowSourceNavigationTag targetTag)
    {
        return targetTag.Kind == ShowSourceNavigationKind.Presentation
            && ViewModel.PresentationPathsMatch(payload.PresentationPath, targetTag.PresentationPath)
            && string.Equals(payload.SourceLibraryId, targetTag.LibraryId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(payload.SourcePlaylistId, targetTag.PlaylistId, StringComparison.OrdinalIgnoreCase)
            && payload.SourcePlaylistIndex == targetTag.PlaylistIndex;
    }

    private static bool TryGetSourceNavigationTag(object? sender, out ShowSourceNavigationTag tag)
    {
        tag = null!;
        if (sender is FrameworkElement { Tag: ShowSourceNavigationTag directTag })
        {
            tag = directTag;
            return true;
        }

        return false;
    }

    private static async Task<T?> ReadDragPayloadAsync<T>(DragEventArgs e, string format)
    {
        var data = await e.DataView.GetDataAsync(format);
        var json = data as string;
        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json);
    }

    private async Task DropSourceOnSourceAsync(
        SourceNavigationDragPayload payload,
        ShowSourceNavigationTag targetTag,
        FrameworkElement targetItem,
        DragEventArgs e)
    {
        if (!CanReorderSourceDrop(targetTag) || payload.Kind != targetTag.Kind)
            return;

        var targetId = targetTag.LibraryId ?? targetTag.PlaylistId;
        if (string.IsNullOrWhiteSpace(targetId) || string.Equals(payload.SourceId, targetId, StringComparison.OrdinalIgnoreCase))
            return;

        var targetIndex = GetSourceDropTargetIndex(payload.Kind, targetId, targetItem, e);
        await ExecuteSidebarActionAsync(async () =>
        {
            if (payload.Kind == ShowSourceNavigationKind.Library)
            {
                await _localCollection.MoveLibraryAsync(payload.SourceId, targetIndex);
                await ViewModel.RefreshCatalogAsync();
                ViewModel.SelectLibraryCommand.Execute(payload.SourceId);
                ViewModel.StatusMessage = "Reordered library.";
            }
            else
            {
                await _localCollection.MovePlaylistAsync(payload.SourceId, targetIndex);
                await ViewModel.RefreshCatalogAsync();
                ViewModel.SelectPlaylistCommand.Execute(payload.SourceId);
                ViewModel.StatusMessage = "Reordered playlist.";
            }
        }, payload.Kind == ShowSourceNavigationKind.Library ? "Could not reorder library" : "Could not reorder playlist");
    }

    private int GetSourceDropTargetIndex(
        ShowSourceNavigationKind kind,
        string targetId,
        FrameworkElement targetItem,
        DragEventArgs e)
    {
        var targetIndex = kind == ShowSourceNavigationKind.Library
            ? ViewModel.Libraries.ToList().FindIndex(item => string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase))
            : ViewModel.Playlists.ToList().FindIndex(item => string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            return 0;

        var position = e.GetPosition(targetItem);
        return position.Y > targetItem.ActualHeight * 0.5
            ? targetIndex + 1
            : targetIndex;
    }

    private async Task DropPresentationOnSourceAsync(PresentationNavigationDragPayload payload, ShowSourceNavigationTag targetTag)
    {
        if (string.IsNullOrWhiteSpace(payload.PresentationPath))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            switch (targetTag.Kind)
            {
                case ShowSourceNavigationKind.Library:
                    await DropPresentationOnLibraryAsync(payload, targetTag.LibraryId).ConfigureAwait(true);
                    break;

                case ShowSourceNavigationKind.Playlist:
                    await DropPresentationOnPlaylistAsync(payload, targetTag.PlaylistId).ConfigureAwait(true);
                    break;
            }
        }, "Could not move presentation");
    }

    private async Task DropPresentationOnPresentationAsync(
        PresentationNavigationDragPayload payload,
        ShowSourceNavigationTag targetTag,
        FrameworkElement targetElement,
        DragEventArgs e)
    {
        if (targetTag.Kind != ShowSourceNavigationKind.Presentation || string.IsNullOrWhiteSpace(targetTag.PresentationPath))
            return;

        var insertIndex = GetPresentationDropTargetIndex(targetTag, targetElement, e);
        await ExecuteSidebarActionAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(targetTag.LibraryId))
                await DropPresentationOnLibraryAsync(payload, targetTag.LibraryId, insertIndex).ConfigureAwait(true);
            else if (!string.IsNullOrWhiteSpace(targetTag.PlaylistId))
                await DropPresentationOnPlaylistAsync(payload, targetTag.PlaylistId, insertIndex).ConfigureAwait(true);
        }, "Could not move presentation");
    }

    private int GetPresentationDropTargetIndex(ShowSourceNavigationTag targetTag, FrameworkElement targetElement, DragEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(targetTag.PresentationPath))
            return 0;

        var targetIndex = -1;
        if (!string.IsNullOrWhiteSpace(targetTag.LibraryId))
        {
            targetIndex = ViewModel.Libraries
                .FirstOrDefault(item => string.Equals(item.Id, targetTag.LibraryId, StringComparison.OrdinalIgnoreCase))
                ?.Presentations
                .FindIndex(item => ViewModel.PresentationPathsMatch(item.Path, targetTag.PresentationPath)) ?? -1;
        }
        else if (!string.IsNullOrWhiteSpace(targetTag.PlaylistId))
        {
            var items = ViewModel.Playlists
                .FirstOrDefault(item => string.Equals(item.Id, targetTag.PlaylistId, StringComparison.OrdinalIgnoreCase))
                ?.Items;
            targetIndex = items == null
                ? -1
                : ResolvePlaylistPresentationIndex(items, targetTag.PresentationPath, targetTag.PlaylistIndex);
        }

        if (targetIndex < 0)
            return 0;

        var position = e.GetPosition(targetElement);
        return position.Y > targetElement.ActualHeight * 0.5
            ? targetIndex + 1
            : targetIndex;
    }

    private async Task DropPresentationOnLibraryAsync(PresentationNavigationDragPayload payload, string? targetLibraryId, int? insertIndex = null)
    {
        if (string.IsNullOrWhiteSpace(targetLibraryId)
            || (string.Equals(payload.SourceLibraryId, targetLibraryId, StringComparison.OrdinalIgnoreCase) && insertIndex == null))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(payload.SourceLibraryId))
        {
            await _presentationActions.MovePresentationToLibraryAsync(
                payload.SourceLibraryId,
                targetLibraryId,
                payload.PresentationPath,
                insertIndex).ConfigureAwait(true);
            await RefreshSidebarAfterMutationAsync(targetLibraryId, null, payload.PresentationPath).ConfigureAwait(true);
            ViewModel.StatusMessage = $"Moved \"{payload.Title}\" to library.";
        }
        else
        {
            await _presentationActions.AddPresentationToLibraryAsync(targetLibraryId, payload.PresentationPath, insertIndex).ConfigureAwait(true);
            await RefreshSidebarAfterMutationAsync(targetLibraryId, null, payload.PresentationPath).ConfigureAwait(true);
            ViewModel.StatusMessage = $"Added \"{payload.Title}\" to library.";
        }
    }

    private async Task DropPresentationOnPlaylistAsync(PresentationNavigationDragPayload payload, string? targetPlaylistId, int? insertIndex = null)
    {
        if (string.IsNullOrWhiteSpace(targetPlaylistId)
            || (string.Equals(payload.SourcePlaylistId, targetPlaylistId, StringComparison.OrdinalIgnoreCase) && insertIndex == null))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(payload.SourcePlaylistId))
        {
            await _presentationActions.MovePresentationToPlaylistAsync(
                payload.SourcePlaylistId,
                targetPlaylistId,
                payload.PresentationPath,
                insertIndex,
                payload.SourcePlaylistIndex).ConfigureAwait(true);
            await RefreshSidebarAfterMutationAsync(null, targetPlaylistId, payload.PresentationPath).ConfigureAwait(true);
            ViewModel.StatusMessage = $"Moved \"{payload.Title}\" to playlist.";
        }
        else
        {
            await _presentationActions.AddPresentationToPlaylistAsync(targetPlaylistId, payload.PresentationPath, insertIndex).ConfigureAwait(true);
            await RefreshSidebarAfterMutationAsync(null, targetPlaylistId, payload.PresentationPath).ConfigureAwait(true);
            ViewModel.StatusMessage = $"Added \"{payload.Title}\" to playlist.";
        }
    }

    private async Task<SoftwareBitmap?> TryCreatePresentationDragCardBitmapAsync(PresentationNavigationDragPayload payload)
    {
        const double cardWidth = 220;

        var bg = FlattenBrushForBitmapRender(LookupThemeBrush("CardBackgroundFillColorDefaultBrush"), new SolidColorBrush(Microsoft.UI.Colors.DimGray));
        var edge = FlattenBrushForBitmapRender(LookupThemeBrush("CardStrokeColorDefaultBrush"), new SolidColorBrush(Microsoft.UI.Colors.Gray));
        var textPrimary = FlattenBrushForBitmapRender(LookupThemeBrush("TextFillColorPrimaryBrush"), new SolidColorBrush(Microsoft.UI.Colors.White));
        var textSecondary = FlattenBrushForBitmapRender(LookupThemeBrush("TextFillColorSecondaryBrush"), new SolidColorBrush(Microsoft.UI.Colors.LightGray));
        var iconBrush = FlattenBrushForBitmapRender(LookupThemeBrush("AccentTextFillColorPrimaryBrush"), textPrimary);

        var sourceLabel = ResolvePresentationDragSourceLabel(payload);
        var rootBorder = new Border
        {
            Width = cardWidth,
            Padding = new Thickness(12, 10, 12, 10),
            Background = bg,
            BorderBrush = edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        var row = new Grid
        {
            ColumnSpacing = 10,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Width = 28,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = LookupSymbolFont(),
            FontSize = 18,
            Foreground = iconBrush,
            Glyph = "\uE8B7",
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(textStack, 1);
        textStack.Children.Add(new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = textPrimary,
            Text = payload.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });
        textStack.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = textSecondary,
            Text = sourceLabel,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });
        row.Children.Add(textStack);
        rootBorder.Child = row;

        var dragHost = PresentationDragPreviewHost;
        if (dragHost == null)
            return null;

        dragHost.Children.Clear();
        dragHost.Children.Add(rootBorder);
        dragHost.UpdateLayout();

        rootBorder.Measure(new Size(cardWidth, 160));
        var height = Math.Max(52, rootBorder.DesiredSize.Height);
        rootBorder.Arrange(new Rect(0, 0, cardWidth, height));

        var width = (int)Math.Max(1, Math.Ceiling(rootBorder.ActualWidth));
        var renderHeight = (int)Math.Max(1, Math.Ceiling(rootBorder.ActualHeight));
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(rootBorder, width, renderHeight);
        var pixels = await rtb.GetPixelsAsync();

        dragHost.Children.Clear();

        var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            rtb.PixelWidth,
            rtb.PixelHeight,
            BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(pixels);

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            bitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        return bitmap;
    }

    private string ResolvePresentationDragSourceLabel(PresentationNavigationDragPayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.SourceLibraryId)
            && ViewModel.Libraries.FirstOrDefault(library => string.Equals(library.Id, payload.SourceLibraryId, StringComparison.OrdinalIgnoreCase)) is { } library)
        {
            return library.Name;
        }

        if (!string.IsNullOrWhiteSpace(payload.SourcePlaylistId)
            && ViewModel.Playlists.FirstOrDefault(playlist => string.Equals(playlist.Id, payload.SourcePlaylistId, StringComparison.OrdinalIgnoreCase)) is { } playlist)
        {
            return playlist.Name;
        }

        return "Presentation";
    }

    private MenuFlyout CreatePresentationNavigationContextFlyout(ShowSourceNavigationTag tag)
    {
        var flyout = new MenuFlyout();

        if (ResolvePresentationTreeItem(tag) is not { } item)
            return flyout;

        flyout.Items.Add(BuildAddToPresentationMenu(item));
        flyout.Items.Add(BuildMoveToPresentationMenu(item));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildArrangementPresentationMenu(item));
        flyout.Items.Add(BuildDestinationPresentationMenu(item));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreatePresentationMenuItem("Edit", "\uE70F", null, VirtualKey.None, _ => EditPresentationAsync(item)));
        flyout.Items.Add(BuildResizePresentationMenu(item));
        flyout.Items.Add(CreatePresentationMenuItem("Reflow", "\uE149", null, VirtualKey.None, _ => ReflowPresentationAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreatePresentationMenuItem("Copy", "\uE8C8", VirtualKeyModifiers.Control, VirtualKey.C, _ =>
        {
            CopyPresentation(item);
            return Task.CompletedTask;
        }));
        var pasteItem = CreatePresentationMenuItem("Paste", "\uE77F", VirtualKeyModifiers.Control, VirtualKey.V, _ => PastePresentationAsync(item));
        pasteItem.IsEnabled = IsPresentationClipboardValid();
        flyout.Items.Add(pasteItem);

        var removeItem = CreatePresentationMenuItem(
            GetPresentationRemoveMenuText(item),
            "\uE74D",
            null,
            VirtualKey.Delete,
            _ => RemovePresentationFromSourceAsync(item));
        removeItem.IsEnabled = IsPresentationInScopedSource(item);
        flyout.Items.Add(removeItem);
        flyout.Items.Add(CreatePresentationMenuItem("Duplicate", "\uE8C8", VirtualKeyModifiers.Control, VirtualKey.D, _ => DuplicatePresentationAsync(item)));
        flyout.Items.Add(CreatePresentationMenuItem("Rename", "\uE8AC", null, VirtualKey.F2, _ =>
        {
            BeginSourceInlineRename(tag);
            return Task.CompletedTask;
        }));
        flyout.Items.Add(new MenuFlyoutSeparator());
        var openFileLocationItem = CreatePresentationMenuItem("Open File Location", "\uE8DA", VirtualKeyModifiers.Control, VirtualKey.I, _ =>
        {
            OpenPresentationFileLocation(item.Presentation.Path);
            return Task.CompletedTask;
        });
        openFileLocationItem.IsEnabled = CanResolvePresentationPath(item.Presentation.Path);
        flyout.Items.Add(openFileLocationItem);

        var exportMenu = new MenuFlyoutSubItem { Text = "Export Presentation..." };
        var exportDocumentItem = CreatePresentationMenuItem("Document (slides only)...", "\uE8A5", null, VirtualKey.None, _ => ExportPresentationDocumentAsync(item));
        exportDocumentItem.IsEnabled = CanResolvePresentationPath(item.Presentation.Path);
        exportMenu.Items.Add(exportDocumentItem);

        var exportBundleItem = CreatePresentationMenuItem("Bundle (with media)...", "\uE159", null, VirtualKey.None, _ => ExportPresentationBundleAsync(item));
        exportBundleItem.IsEnabled = CanResolvePresentationPath(item.Presentation.Path);
        exportMenu.Items.Add(exportBundleItem);
        flyout.Items.Add(exportMenu);

        var exportImagesItem = CreatePresentationMenuItem("Export as Images...", "\uEB9F", null, VirtualKey.None, _ => ExportPresentationImagesAsync(item));
        exportImagesItem.IsEnabled = CanResolvePresentationPath(item.Presentation.Path);
        flyout.Items.Add(exportImagesItem);

        return flyout;
    }

    private MenuFlyoutSubItem BuildAddToPresentationMenu(ShowPresentationTreeItem item)
    {
        var menu = new MenuFlyoutSubItem { Text = "Add to" };
        foreach (var library in ViewModel.Libraries
                     .Where(library => !library.Presentations.Any(presentation => ViewModel.PresentationPathsMatch(presentation.Path, item.Presentation.Path)))
                     .OrderBy(library => library.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(CreatePresentationMenuItem(library.Name, "\uE8B7", null, VirtualKey.None, _ =>
                AddPresentationToLibraryAsync(item, library.Id)));
        }

        if (ViewModel.Libraries.Count > 0 && ViewModel.Playlists.Count > 0 && menu.Items.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var playlist in ViewModel.Playlists
                     .OrderBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(CreatePresentationMenuItem(playlist.Name, "\uE142", null, VirtualKey.None, _ =>
                AddPresentationToPlaylistAsync(item, playlist.Id)));
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(CreateDisabledPresentationMenuItem("No libraries or playlists available."));

        return menu;
    }

    private MenuFlyoutSubItem BuildMoveToPresentationMenu(ShowPresentationTreeItem item)
    {
        var menu = new MenuFlyoutSubItem { Text = "Move to" };
        var sourceLibraryId = string.IsNullOrWhiteSpace(item.LibraryId)
            ? ResolvePreferredLibraryIdForPresentation(item.Presentation.Path)
            : item.LibraryId;

        foreach (var library in ViewModel.Libraries
                     .Where(library => !string.Equals(library.Id, sourceLibraryId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(library => library.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(CreatePresentationMenuItem(library.Name, "\uE8B7", null, VirtualKey.None, _ =>
                MovePresentationToLibraryAsync(item, sourceLibraryId, library.Id)));
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(CreateDisabledPresentationMenuItem("No other libraries available."));

        return menu;
    }

    private MenuFlyoutSubItem BuildArrangementPresentationMenu(ShowPresentationTreeItem item)
    {
        var menu = new MenuFlyoutSubItem { Text = "Arrangement" };
        try
        {
            var project = _projects.Open(item.Presentation.Path);
            var currentArrangementId = item.Presentation.ArrangementId ?? project.Arrangement?.ActiveArrangementId;
            foreach (var arrangement in project.Arrangement?.Arrangements ?? [])
            {
                var icon = string.Equals(arrangement.Id, currentArrangementId, StringComparison.OrdinalIgnoreCase)
                    ? "\uE73E"
                    : "\uE8A5";
                menu.Items.Add(CreatePresentationMenuItem(arrangement.Name, icon, null, VirtualKey.None, _ =>
                    SetPresentationArrangementAsync(item, arrangement.Id)));
            }
        }
        catch
        {
            menu.Items.Add(CreateDisabledPresentationMenuItem("Could not read arrangements."));
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(CreateDisabledPresentationMenuItem("No arrangements available."));

        return menu;
    }

    private MenuFlyoutSubItem BuildDestinationPresentationMenu(ShowPresentationTreeItem item)
    {
        var menu = new MenuFlyoutSubItem { Text = "Destination" };
        var currentLayer = OutputRoutingDefaults.LayerIdEquals(item.Presentation.DestinationLayerId, BackendOutputLayerKind.Announcements)
            ? BackendOutputLayerKind.Announcements
            : BackendOutputLayerKind.Slide;

        AddDestinationMenuItem(menu, item, BackendOutputLayerKind.Slide, currentLayer, "Presentation");
        AddDestinationMenuItem(menu, item, BackendOutputLayerKind.Announcements, currentLayer, "Announcements");
        return menu;
    }

    private void AddDestinationMenuItem(
        MenuFlyoutSubItem menu,
        ShowPresentationTreeItem item,
        BackendOutputLayerKind layerKind,
        BackendOutputLayerKind currentLayer,
        string label)
    {
        var icon = layerKind == currentLayer ? "\uE73E" : "\uE8A5";
        menu.Items.Add(CreatePresentationMenuItem(label, icon, null, VirtualKey.None, _ =>
            SetPresentationDestinationAsync(item, layerKind)));
    }

    private MenuFlyoutSubItem BuildResizePresentationMenu(ShowPresentationTreeItem item)
    {
        var menu = new MenuFlyoutSubItem { Text = "Resize" };
        foreach (var choice in BuildConfiguredScreenSizeChoices())
        {
            menu.Items.Add(CreatePresentationMenuItem(
                FormatPresentationSizeMenuLabel(choice, includeSourceLabel: true),
                "\uE7F4",
                null,
                VirtualKey.None,
                _ => ResizePresentationAsync(item, choice)));
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var choice in BuildCommonPresentationSizeChoices())
        {
            menu.Items.Add(CreatePresentationMenuItem(
                FormatPresentationSizeMenuLabel(choice, includeSourceLabel: false),
                "\uE7F4",
                null,
                VirtualKey.None,
                _ => ResizePresentationAsync(item, choice)));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreatePresentationMenuItem("Custom size...", "\uE70F", null, VirtualKey.None, _ =>
            ResizePresentationWithCustomSizeAsync(item)));
        return menu;
    }

    private MenuFlyoutItem CreatePresentationMenuItem(
        string text,
        string iconGlyph,
        VirtualKeyModifiers? acceleratorModifiers,
        VirtualKey acceleratorKey,
        Func<ShowPresentationTreeItem, Task> execute)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon
            {
                Glyph = iconGlyph,
            },
        };

        if (acceleratorKey != VirtualKey.None)
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = acceleratorKey,
                Modifiers = acceleratorModifiers ?? VirtualKeyModifiers.None,
            };
            accelerator.Invoked += async (_, args) =>
            {
                await execute(null!);
                args.Handled = true;
            };
            item.KeyboardAccelerators.Add(accelerator);
        }

        item.Click += async (_, _) =>
        {
            await execute(null!);
        };
        return item;
    }

    private static MenuFlyoutItem CreateDisabledPresentationMenuItem(string text) =>
        new()
        {
            Text = text,
            IsEnabled = false,
        };

    private static string GetPresentationRemoveMenuText(ShowPresentationTreeItem item) =>
        string.IsNullOrWhiteSpace(item.PlaylistId) ? "Delete" : "Remove from Playlist";

    private static bool IsPresentationInScopedSource(ShowPresentationTreeItem item) =>
        !string.IsNullOrWhiteSpace(item.LibraryId) || !string.IsNullOrWhiteSpace(item.PlaylistId);

    private KeyboardAccelerator CreatePresentationKeyboardAccelerator(
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        ShowSourceNavigationTag tag,
        Func<ShowPresentationTreeItem, Task> execute)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
        };

        accelerator.Invoked += async (_, args) =>
        {
            if (ResolvePresentationTreeItem(tag) is { } item)
                await execute(item);
            args.Handled = true;
        };
        return accelerator;
    }

    private ShowPresentationTreeItem? ResolvePresentationTreeItem(ShowSourceNavigationTag tag)
    {
        if (tag.Kind != ShowSourceNavigationKind.Presentation || string.IsNullOrWhiteSpace(tag.PresentationPath))
            return null;

        if (!string.IsNullOrWhiteSpace(tag.LibraryId))
        {
            var library = ViewModel.Libraries.FirstOrDefault(item =>
                string.Equals(item.Id, tag.LibraryId, StringComparison.OrdinalIgnoreCase));
            var presentation = library?.Presentations.FirstOrDefault(item =>
                ViewModel.PresentationPathsMatch(item.Path, tag.PresentationPath));
            return presentation == null
                ? null
                : new ShowPresentationTreeItem(presentation, libraryId: tag.LibraryId);
        }

        if (!string.IsNullOrWhiteSpace(tag.PlaylistId))
        {
            var playlist = ViewModel.Playlists.FirstOrDefault(item =>
                string.Equals(item.Id, tag.PlaylistId, StringComparison.OrdinalIgnoreCase));
            if (playlist == null)
                return null;

            var index = ResolvePlaylistPresentationIndex(playlist.Items, tag.PresentationPath, tag.PlaylistIndex);
            if (index < 0)
                return null;

            return new ShowPresentationTreeItem(
                playlist.Items[index],
                playlistId: tag.PlaylistId,
                playlistIndex: index,
                playlistCount: playlist.Items.Count);
        }

        return null;
    }

    private int ResolvePlaylistPresentationIndex(IReadOnlyList<PresentationRefDto> items, string presentationPath, int playlistIndex)
    {
        if (playlistIndex >= 0
            && playlistIndex < items.Count
            && ViewModel.PresentationPathsMatch(items[playlistIndex].Path, presentationPath))
        {
            return playlistIndex;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (ViewModel.PresentationPathsMatch(items[index].Path, presentationPath))
                return index;
        }

        return -1;
    }

    private void SourceNavigationItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is NavigationViewItem { Tag: ShowSourceNavigationTag tag }
            && tag.Kind is ShowSourceNavigationKind.Library or ShowSourceNavigationKind.Playlist)
        {
            CancelPendingSourceNavigationSelection();
            ToggleSourceInlineRename(tag);
            e.Handled = true;
        }
    }

    private void PresentationNavigationItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is NavigationViewItem { Tag: ShowSourceNavigationTag tag }
            && tag.Kind == ShowSourceNavigationKind.Presentation)
        {
            CancelPendingSourceNavigationSelection();
            ToggleSourceInlineRename(tag);
            e.Handled = true;
        }
    }

    private void SourceInlineRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && ReferenceEquals(textBox, _inlineRenameTextBox))
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        }
    }

    private async void SourceInlineRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            await CommitSourceInlineRenameAsync(textBox);
    }

    private async void SourceInlineRenameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await CommitSourceInlineRenameAsync(textBox);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelSourceInlineRename();
        }
    }

    private void BeginSourceInlineRename(ShowSourceNavigationTag tag)
    {
        if (tag.Kind is not (ShowSourceNavigationKind.Library or ShowSourceNavigationKind.Playlist or ShowSourceNavigationKind.Presentation))
            return;

        CancelPendingSourceNavigationSelection();
        _inlineRenameSourceTag = tag;
        ScheduleSourcesNavigationRefresh();
    }

    private void ToggleSourceInlineRename(ShowSourceNavigationTag tag)
    {
        if (Equals(_inlineRenameSourceTag, tag))
            CancelSourceInlineRename();
        else
            BeginSourceInlineRename(tag);
    }

    private void CancelSourceInlineRename()
    {
        if (_inlineRenameSourceTag == null)
            return;

        _inlineRenameSourceTag = null;
        _inlineRenameTextBox = null;
        ScheduleSourcesNavigationRefresh();
        RestoreKeyboardFocus?.Invoke();
    }

    private async Task CommitSourceInlineRenameAsync(TextBox textBox)
    {
        if (_committingInlineSourceRename
            || textBox.Tag is not ShowSourceNavigationTag tag
            || !Equals(tag, _inlineRenameSourceTag))
        {
            return;
        }

        _committingInlineSourceRename = true;
        _inlineRenameSourceTag = null;
        _inlineRenameTextBox = null;

        try
        {
            var name = NormalizeDialogValue(textBox.Text);
            var currentName = GetSourceNavigationDisplayName(tag);
            if (name == null || string.Equals(name, currentName, StringComparison.Ordinal))
            {
                ScheduleSourcesNavigationRefresh();
                RestoreKeyboardFocus?.Invoke();
                return;
            }

            await RenameSourceAsync(tag, name);
        }
        finally
        {
            _committingInlineSourceRename = false;
        }
    }

    private string? GetSourceNavigationDisplayName(ShowSourceNavigationTag tag) =>
        tag.Kind switch
        {
            ShowSourceNavigationKind.Library => ViewModel.Libraries
                .FirstOrDefault(library => string.Equals(library.Id, tag.LibraryId, StringComparison.OrdinalIgnoreCase))
                ?.Name,
            ShowSourceNavigationKind.Playlist => ViewModel.Playlists
                .FirstOrDefault(playlist => string.Equals(playlist.Id, tag.PlaylistId, StringComparison.OrdinalIgnoreCase))
                ?.Name,
            ShowSourceNavigationKind.Presentation => ResolvePresentationTreeItem(tag)?.Presentation.Title,
            _ => null,
        };

    private async Task RenameSourceAsync(ShowSourceNavigationTag tag, string name)
    {
        switch (tag.Kind)
        {
            case ShowSourceNavigationKind.Library when !string.IsNullOrWhiteSpace(tag.LibraryId):
                await ExecuteSidebarActionAsync(async () =>
                {
                    await _localCollection.RenameLibraryAsync(tag.LibraryId, name);
                    await ViewModel.RefreshCatalogAsync();
                    ViewModel.SelectLibraryCommand.Execute(tag.LibraryId);
                    ViewModel.StatusMessage = $"Renamed library to \"{name}\".";
                }, "Could not rename library");
                break;

            case ShowSourceNavigationKind.Playlist when !string.IsNullOrWhiteSpace(tag.PlaylistId):
                await ExecuteSidebarActionAsync(async () =>
                {
                    await _localCollection.RenamePlaylistAsync(tag.PlaylistId, name);
                    await ViewModel.RefreshCatalogAsync();
                    ViewModel.SelectPlaylistCommand.Execute(tag.PlaylistId);
                    ViewModel.StatusMessage = $"Renamed playlist to \"{name}\".";
                }, "Could not rename playlist");
                break;

            case ShowSourceNavigationKind.Presentation when ResolvePresentationTreeItem(tag) is { } item:
                await ExecuteSidebarActionAsync(async () =>
                {
                    var renamed = await _presentationActions.RenamePresentationAsync(item.Presentation.Path, name);
                    RemapPresentationClipboard(item.Presentation.Path, renamed.NewPresentationPath);
                    await RefreshSidebarAfterMutationAsync(item.LibraryId, item.PlaylistId, renamed.NewPresentationPath);
                    ViewModel.StatusMessage = $"Renamed to \"{renamed.Title}\".";
                }, "Could not rename presentation");
                break;
        }
    }

    private async Task DeleteSourceAsync(ShowSourceNavigationTag tag)
    {
        switch (tag.Kind)
        {
            case ShowSourceNavigationKind.Library when !string.IsNullOrWhiteSpace(tag.LibraryId):
                var library = ViewModel.Libraries.FirstOrDefault(item =>
                    string.Equals(item.Id, tag.LibraryId, StringComparison.OrdinalIgnoreCase));
                if (library == null
                    || !await ConfirmAsync("Delete Library", $"Delete \"{library.Name}\" from the collection?", "Delete"))
                {
                    return;
                }

                await ExecuteSidebarActionAsync(async () =>
                {
                    await _localCollection.DeleteLibraryAsync(tag.LibraryId);
                    await ViewModel.RefreshCatalogAsync();
                    ViewModel.StatusMessage = $"Deleted {library.Name}.";
                }, "Could not delete library");
                break;

            case ShowSourceNavigationKind.Playlist when !string.IsNullOrWhiteSpace(tag.PlaylistId):
                var playlist = ViewModel.Playlists.FirstOrDefault(item =>
                    string.Equals(item.Id, tag.PlaylistId, StringComparison.OrdinalIgnoreCase));
                if (playlist == null
                    || !await ConfirmAsync("Delete Playlist", $"Delete \"{playlist.Name}\" from the collection?", "Delete"))
                {
                    return;
                }

                await ExecuteSidebarActionAsync(async () =>
                {
                    await _localCollection.DeletePlaylistAsync(tag.PlaylistId);
                    await ViewModel.RefreshCatalogAsync();
                    ViewModel.StatusMessage = $"Deleted {playlist.Name}.";
                }, "Could not delete playlist");
                break;
        }
    }

    private async Task DuplicatePlaylistSourceAsync(ShowSourceNavigationTag tag)
    {
        if (tag.Kind != ShowSourceNavigationKind.Playlist || string.IsNullOrWhiteSpace(tag.PlaylistId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var duplicate = await _localCollection.DuplicatePlaylistAsync(tag.PlaylistId);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectPlaylistCommand.Execute(duplicate.Id);
            ViewModel.StatusMessage = $"Duplicated playlist as \"{duplicate.Name}\".";
        }, "Could not duplicate playlist");
    }

    private NavigationViewItem CreatePresentationNavigationItem(
        PresentationRefDto presentation,
        string? libraryId,
        string? playlistId,
        int playlistIndex)
    {
        var tag = ShowSourceNavigationTag.ForPresentation(presentation.Path, libraryId, playlistId, playlistIndex);
        var item = new NavigationViewItem
        {
            Content = CreatePresentationNavigationItemContent(presentation.Title, tag),
            Tag = tag,
        };

        ToolTipService.SetToolTip(item, presentation.Path);
        AttachPresentationNavigationCommands(item, tag);
        AttachPresentationNavigationDropTarget(item);
        return item;
    }

    private object CreatePresentationNavigationItemContent(string displayName, ShowSourceNavigationTag tag)
    {
        if (Equals(_inlineRenameSourceTag, tag))
        {
            var textBox = new TextBox
            {
                Text = displayName,
                Tag = tag,
                MinWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                SelectionStart = 0,
                SelectionLength = displayName.Length,
            };

            textBox.KeyDown += SourceInlineRenameTextBox_KeyDown;
            textBox.LostFocus += SourceInlineRenameTextBox_LostFocus;
            textBox.Loaded += SourceInlineRenameTextBox_Loaded;
            _inlineRenameTextBox = textBox;
            return textBox;
        }

        var row = CreateSidebarNavigationRow(tag);
        var content = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        content.Children.Add(new TextBlock
        {
            Text = displayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        AddSidebarDropTargetAdorners(content);
        row.Child = content;
        AttachPresentationNavigationDragSource(row);
        AttachPresentationNavigationDropTarget(row);
        return row;
    }

    private void UpdateSourcesNavigationSelection()
    {
        NavigationViewItem? target = null;
        if (_selectedSourceNavigationTag != null)
        {
            target = FindNavigationItem(MenuItems, tag => SourceNavigationTagsEqual(tag, _selectedSourceNavigationTag));
            if (target == null)
                _selectedSourceNavigationTag = null;
        }

        if (target == null)
        {
            var selectedPresentationPath = ViewModel.SelectedPresentationPath;

            if (!string.IsNullOrWhiteSpace(selectedPresentationPath))
            {
                if (!string.IsNullOrWhiteSpace(ViewModel.SelectedLibraryId))
                {
                    target = FindNavigationItem(
                        MenuItems,
                        tag => tag.Kind == ShowSourceNavigationKind.Presentation
                            && string.Equals(tag.LibraryId, ViewModel.SelectedLibraryId, StringComparison.OrdinalIgnoreCase)
                            && ViewModel.PresentationPathsMatch(tag.PresentationPath, selectedPresentationPath));
                }
                else if (!string.IsNullOrWhiteSpace(ViewModel.SelectedPlaylistId))
                {
                    target = FindNavigationItem(
                        MenuItems,
                        tag => tag.Kind == ShowSourceNavigationKind.Presentation
                            && string.Equals(tag.PlaylistId, ViewModel.SelectedPlaylistId, StringComparison.OrdinalIgnoreCase)
                            && ViewModel.PresentationPathsMatch(tag.PresentationPath, selectedPresentationPath));
                }
            }

            target ??= !string.IsNullOrWhiteSpace(ViewModel.SelectedPlaylistId)
                ? FindNavigationItem(
                    MenuItems,
                    tag => tag.Kind == ShowSourceNavigationKind.Playlist
                        && string.Equals(tag.PlaylistId, ViewModel.SelectedPlaylistId, StringComparison.OrdinalIgnoreCase))
                : null;

            target ??= !string.IsNullOrWhiteSpace(ViewModel.SelectedLibraryId)
                ? FindNavigationItem(
                    MenuItems,
                    tag => tag.Kind == ShowSourceNavigationKind.Library
                        && string.Equals(tag.LibraryId, ViewModel.SelectedLibraryId, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        _suppressSourcesNavigationSelectionChanged = true;
        try
        {
            SelectedItem = target;
            if (target != null)
                ExpandAncestorsForTarget(MenuItems, target);
        }
        finally
        {
            _suppressSourcesNavigationSelectionChanged = false;
        }
    }

    private bool SourceNavigationTagsEqual(ShowSourceNavigationTag left, ShowSourceNavigationTag right) =>
        left.Kind == right.Kind
        && string.Equals(left.LibraryId, right.LibraryId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PlaylistId, right.PlaylistId, StringComparison.OrdinalIgnoreCase)
        && left.PlaylistIndex == right.PlaylistIndex
        && (string.Equals(left.PresentationPath, right.PresentationPath, StringComparison.OrdinalIgnoreCase)
            || ViewModel.PresentationPathsMatch(left.PresentationPath, right.PresentationPath));

    private static NavigationViewItem? FindNavigationItem(
        IList<object> items,
        Func<ShowSourceNavigationTag, bool> predicate)
    {
        foreach (var item in items.OfType<NavigationViewItem>())
        {
            if (item.Tag is ShowSourceNavigationTag tag && predicate(tag))
                return item;

            var child = FindNavigationItem(item.MenuItems, predicate);
            if (child != null)
                return child;
        }

        return null;
    }

    private static bool ExpandAncestorsForTarget(IList<object> items, NavigationViewItem target)
    {
        foreach (var item in items.OfType<NavigationViewItem>())
        {
            if (ReferenceEquals(item, target))
                return true;

            if (ExpandAncestorsForTarget(item.MenuItems, target))
            {
                item.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void ShowNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSourcesNavigationSelectionChanged)
            return;

        if (_pendingSourceNavigationSelection != null)
            return;

        if (args.SelectedItemContainer?.Tag is not ShowSourceNavigationTag tag)
            return;

        ScheduleSourceNavigationSelection(tag);
    }

    private void ShowNavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_suppressSourcesNavigationSelectionChanged)
            return;

        if (args.InvokedItemContainer?.Tag is not ShowSourceNavigationTag tag)
            return;

        ScheduleSourceNavigationSelection(tag);
    }

    private async void ScheduleSourceNavigationSelection(ShowSourceNavigationTag tag)
    {
        CancelPendingSourceNavigationSelection();
        var pending = new CancellationTokenSource();
        _pendingSourceNavigationSelection = pending;

        try
        {
            await Task.Delay(250, pending.Token);
        }
        catch (OperationCanceledException)
        {
            pending.Dispose();
            return;
        }

        if (pending.IsCancellationRequested)
        {
            pending.Dispose();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_pendingSourceNavigationSelection, pending))
            {
                _pendingSourceNavigationSelection = null;
                ExecuteSourceNavigationSelection(tag);
            }

            pending.Dispose();
        }))
        {
            pending.Dispose();
        }
    }

    private void CancelPendingSourceNavigationSelection()
    {
        if (_pendingSourceNavigationSelection == null)
            return;

        _pendingSourceNavigationSelection.Cancel();
        _pendingSourceNavigationSelection = null;
    }

    private void ExecuteSourceNavigationSelection(ShowSourceNavigationTag tag)
    {
        _selectedSourceNavigationTag = tag;
        switch (tag.Kind)
        {
            case ShowSourceNavigationKind.Library:
                ViewModel.SelectLibraryCommand.Execute(tag.LibraryId);
                break;

            case ShowSourceNavigationKind.Playlist:
                ViewModel.SelectPlaylistCommand.Execute(tag.PlaylistId);
                break;

            case ShowSourceNavigationKind.Presentation:
                if (!string.IsNullOrWhiteSpace(tag.LibraryId))
                    ViewModel.SelectLibraryCommand.Execute(tag.LibraryId);
                else if (!string.IsNullOrWhiteSpace(tag.PlaylistId))
                    ViewModel.SelectPlaylistCommand.Execute(tag.PlaylistId);

                ViewModel.SelectPresentationCommand.Execute(tag.PresentationPath);
                break;
        }

        RestoreKeyboardFocus?.Invoke();
    }

    private void SourcesSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _sourcesSearchText = sender.Text?.Trim() ?? string.Empty;
        _activeSourcesSearchSuggestions = BuildPresentationSearchSuggestions(_sourcesSearchText);
        sender.ItemsSource = _activeSourcesSearchSuggestions.Select(static suggestion => suggestion.DisplayText).ToList();
        ScheduleSourcesNavigationRefresh();
    }

    private void SourcesSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        var suggestion = ResolveSearchSuggestion(args.SelectedItem as string);
        if (suggestion is { IsPlaceholder: false })
            sender.Text = suggestion.Title;
    }

    private async void SourcesSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = ResolveSearchSuggestion(args.ChosenSuggestion as string);
        if (chosen is { IsPlaceholder: false })
        {
            await ActivateSearchSuggestionAsync(chosen);
            return;
        }

        _activeSourcesSearchSuggestions = BuildPresentationSearchSuggestions(args.QueryText);
        sender.ItemsSource = _activeSourcesSearchSuggestions.Select(static suggestion => suggestion.DisplayText).ToList();

        var matches = _activeSourcesSearchSuggestions
            .Where(static suggestion => !suggestion.IsPlaceholder)
            .ToList();

        if (matches.Count == 1)
        {
            await ActivateSearchSuggestionAsync(matches[0]);
            return;
        }

        var exactTitleMatches = matches
            .Where(match => string.Equals(match.Title, args.QueryText?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactTitleMatches.Count == 1)
        {
            await ActivateSearchSuggestionAsync(exactTitleMatches[0]);
            return;
        }

        ViewModel.StatusMessage = matches.Count == 0
            ? "No matching presentations found."
            : "Choose a presentation suggestion to open it in Show.";
        RestoreKeyboardFocus?.Invoke();
    }

    private void RefreshSourcesSearchSuggestions()
    {
        if (SourcesSearchBox == null)
            return;

        _activeSourcesSearchSuggestions = BuildPresentationSearchSuggestions(SourcesSearchBox.Text);
        SourcesSearchBox.ItemsSource = _activeSourcesSearchSuggestions.Select(static suggestion => suggestion.DisplayText).ToList();
    }

    private List<ShowPresentationSearchSuggestion> BuildPresentationSearchSuggestions(string? queryText)
    {
        var query = queryText?.Trim() ?? string.Empty;
        if (query.Length == 0)
            return new List<ShowPresentationSearchSuggestion>();

        var suggestions = new List<ShowPresentationSearchSuggestion>();

        foreach (var library in ViewModel.Libraries)
        {
            foreach (var presentation in library.Presentations)
            {
                if (!PresentationMatchesSearch(presentation, query))
                    continue;

                suggestions.Add(new ShowPresentationSearchSuggestion(
                    presentation.Title,
                    presentation.Path,
                    library.Id,
                    null,
                    $"Library: {library.Name}"));
            }
        }

        foreach (var playlist in ViewModel.Playlists)
        {
            foreach (var presentation in playlist.Items)
            {
                if (!PresentationMatchesSearch(presentation, query))
                    continue;

                suggestions.Add(new ShowPresentationSearchSuggestion(
                    presentation.Title,
                    presentation.Path,
                    null,
                    playlist.Id,
                    $"Playlist: {playlist.Name}"));
            }
        }

        var ordered = suggestions
            .OrderBy(suggestion => !suggestion.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(suggestion => suggestion.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(suggestion => suggestion.SourceLabel, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (ordered.Count > 0)
            return ordered;

        return
        [
            new ShowPresentationSearchSuggestion(
                "No presentations found",
                string.Empty,
                null,
                null,
                "Try another search term",
                IsPlaceholder: true),
        ];
    }

    private static bool PresentationMatchesSearch(PresentationRefDto presentation, string query) =>
        (!string.IsNullOrWhiteSpace(presentation.Title) && presentation.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(presentation.Path) && presentation.Path.Contains(query, StringComparison.OrdinalIgnoreCase));

    private ShowPresentationSearchSuggestion? ResolveSearchSuggestion(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
            return null;

        return _activeSourcesSearchSuggestions.FirstOrDefault(suggestion =>
            string.Equals(suggestion.DisplayText, displayText, StringComparison.Ordinal));
    }

    private async Task ActivateSearchSuggestionAsync(ShowPresentationSearchSuggestion suggestion)
    {
        if (suggestion.IsPlaceholder)
            return;

        if (!string.IsNullOrWhiteSpace(suggestion.LibraryId))
            ViewModel.SelectLibraryCommand.Execute(suggestion.LibraryId);
        else if (!string.IsNullOrWhiteSpace(suggestion.PlaylistId))
            ViewModel.SelectPlaylistCommand.Execute(suggestion.PlaylistId);

        await ViewModel.OpenPresentationFromPathAsync(suggestion.PresentationPath);
        ViewModel.StatusMessage = $"Opened \"{suggestion.Title}\" from {suggestion.SourceLabel}.";
        RestoreKeyboardFocus?.Invoke();
    }
    private async void ShowContentSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await ExecuteContentActionAsync(ResolveRecommendedContentAction());
    }

    private Task ExecuteContentActionAsync(ShowContentAction action) =>
        action switch
        {
            ShowContentAction.CreateLibrary => CreateLibraryAsync(),
            ShowContentAction.CreatePlaylist => CreatePlaylistAsync(),
            ShowContentAction.ImportPresentation => ImportPresentationAsync(),
            _ => CreatePresentationAsync(),
        };

    private async void LibraryNewLibrary_Click(object sender, RoutedEventArgs e)
    {
        await CreateLibraryAsync();
    }

    private async Task CreateLibraryAsync()
    {
        var libraryName = await PromptForCreateNameAsync(
            "Create Library",
            "Library name",
            "Library name",
            "Enter a library name.");
        if (string.IsNullOrWhiteSpace(libraryName))
            return;

        var library = await _localCollection.EnsureLibraryAsync(libraryName);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.SelectLibraryCommand.Execute(library.Id);
    }

    private async void LibraryNewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        await CreatePlaylistAsync();
    }

    private async Task CreatePlaylistAsync()
    {
        var playlistName = await PromptForCreateNameAsync(
            "Create Playlist",
            "Playlist name",
            "Playlist name",
            "Enter a playlist name.");
        if (string.IsNullOrWhiteSpace(playlistName))
            return;

        var playlist = await _localCollection.EnsurePlaylistAsync(playlistName);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.SelectPlaylistCommand.Execute(playlist.Id);
    }

    private async void ShowCreatePresentation_Click(object sender, RoutedEventArgs e)
    {
        await CreatePresentationAsync();
    }

    private async Task CreatePresentationAsync()
    {
        var request = await PromptForPresentationCreationAsync();
        if (request == null)
            return;

        try
        {
            var created = await _localCollection.CreatePresentationAsync(
                request.Name,
                request.LibraryId,
                request.PlaylistId,
                null,
                null,
                aspectRatio: request.AspectRatio,
                slideSize: request.SlideSize);

            if (request.Theme != null)
                ApplyThemeToPresentation(created.LocalPath, request.Theme);

            await ViewModel.RefreshCatalogAsync();
            await ViewModel.OpenImportedPresentationAsync(created.LocalPath, created.LibraryId, created.PlaylistId);
            ViewModel.StatusMessage = $"Created \"{created.Title}\".";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async Task<CreatePresentationDialogResult?> PromptForPresentationCreationAsync()
    {
        var libraries = ViewModel.Libraries.ToList();
        if (libraries.Count == 0)
        {
            ViewModel.StatusMessage = "Create a library first, then create presentations in it.";
            return null;
        }

        var themes = await _themeLibrary.LoadAsync();
        var themeChoices = new List<CreatePresentationThemeChoice>
        {
            new(null, "No theme"),
        };
        themeChoices.AddRange(themes.Select(theme => new CreatePresentationThemeChoice(theme, theme.Name)));

        var screenSizeChoices = BuildConfiguredScreenSizeChoices();
        var commonSizeChoices = BuildCommonPresentationSizeChoices();
        var selectedSize = screenSizeChoices.FirstOrDefault()
                           ?? commonSizeChoices.First();

        var playlistChoices = new List<CreatePresentationPlaylistChoice>
        {
            new(null, "No playlist"),
        };
        playlistChoices.AddRange(ViewModel.Playlists.Select(playlist => new CreatePresentationPlaylistChoice(playlist, playlist.Name)));

        LibraryDto selectedLibrary = libraries.FirstOrDefault(library =>
                                         string.Equals(library.Id, ViewModel.SelectedLibraryId, StringComparison.OrdinalIgnoreCase))
                                     ?? libraries.First();

        var dialog = new NewPresentationDialog(
            themeChoices,
            screenSizeChoices,
            commonSizeChoices,
            libraries,
            playlistChoices,
            selectedLibrary,
            selectedSize,
            PromptForCustomPresentationSizeAsync)
            .ConfigureForPage(this);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return dialog.Result;
    }

    private IReadOnlyList<PresentationSizeChoice> BuildConfiguredScreenSizeChoices()
    {
        var snapshot = _topology.GetSnapshot();
        var choices = new List<PresentationSizeChoice>();

        foreach (var screen in snapshot.Screens.Values.OrderBy(screen => screen.Kind).ThenBy(screen => screen.Name))
        {
            var targets = snapshot.GetLocalDisplayTargets(screen.Id)
                .Where(target => target.Monitor != null)
                .ToList();
            if (targets.Count == 0)
            {
                choices.Add(CreatePresentationSizeChoice(
                    $"{screen.Name} screen",
                    screen.RenderSize.Width,
                    screen.RenderSize.Height,
                    includeSourceLabel: true));
                continue;
            }

            foreach (var target in targets)
            {
                var monitor = target.Monitor!;
                choices.Add(CreatePresentationSizeChoice(
                    $"{screen.Name} screen - {monitor.Name}",
                    monitor.Width,
                    monitor.Height,
                    includeSourceLabel: true));
            }
        }

        return choices
            .GroupBy(choice => $"{choice.SourceLabel}|{choice.Size.Width}x{choice.Size.Height}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<PresentationSizeChoice> BuildCommonPresentationSizeChoices() =>
    [
        CreatePresentationSizeChoice("4K UHD", 3840, 2160),
        CreatePresentationSizeChoice("Full HD", 1920, 1080),
        CreatePresentationSizeChoice("HD", 1280, 720),
        CreatePresentationSizeChoice("WUXGA", 1920, 1200),
        CreatePresentationSizeChoice("SXGA+", 1400, 1050),
        CreatePresentationSizeChoice("4:3 Standard", 1440, 1080),
    ];

    private static PresentationSizeChoice CreatePresentationSizeChoice(
        string sourceLabel,
        int width,
        int height,
        bool includeSourceLabel = false)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var aspectRatio = FormatAspectRatio(width, height);
        var sizeLabel = $"{width} x {height}";
        return new PresentationSizeChoice(
            includeSourceLabel ? $"{sourceLabel} - {sizeLabel}" : sizeLabel,
            sourceLabel,
            aspectRatio,
            new SlideSizeDto { Width = width, Height = height });
    }

    private static string FormatPresentationSizeMenuLabel(PresentationSizeChoice choice, bool includeSourceLabel)
    {
        var size = $"{choice.Size.Width} x {choice.Size.Height}";
        return includeSourceLabel ? $"{choice.SourceLabel} - {size}" : size;
    }

    private async Task<PresentationSizeChoice?> PromptForCustomPresentationSizeAsync(PresentationSizeChoice currentSize)
    {
        var widthBox = new NumberBox
        {
            Header = "Width",
            Minimum = 1,
            Maximum = 16384,
            Value = currentSize.Size.Width,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var heightBox = new NumberBox
        {
            Header = "Height",
            Minimum = 1,
            Maximum = 16384,
            Value = currentSize.Size.Height,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var validationText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var dialog = new ContentDialog
        {
            Title = "Custom Slide Size",
            PrimaryButtonText = "Use Size",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                MinWidth = 320,
                Spacing = 12,
                Children =
                {
                    widthBox,
                    heightBox,
                    validationText,
                },
            },
        }.ConfigureForPage(this);

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (double.IsNaN(widthBox.Value)
                || double.IsNaN(heightBox.Value)
                || widthBox.Value < 1
                || heightBox.Value < 1)
            {
                validationText.Text = "Enter a width and height greater than zero.";
                validationText.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return CreatePresentationSizeChoice(
            "Custom",
            (int)Math.Round(widthBox.Value),
            (int)Math.Round(heightBox.Value),
            includeSourceLabel: true);
    }

    private async Task<string?> PromptForCreateNameAsync(
        string title,
        string fieldHeader,
        string placeholderText,
        string validationMessage)
    {
        var dialog = new CreateNameDialog(
            title,
            fieldHeader,
            placeholderText,
            "Create",
            validationMessage)
            .ConfigureForPage(this);

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? dialog.Result
            : null;
    }

    private static string FormatAspectRatio(int width, int height)
    {
        var divisor = GreatestCommonDivisor(Math.Abs(width), Math.Abs(height));
        return divisor == 0 ? "16:9" : $"{width / divisor}:{height / divisor}";
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            var next = left % right;
            left = right;
            right = next;
        }

        return left;
    }

    private void ApplyThemeToPresentation(string presentationPath, ThemeTemplate theme)
    {
        var project = _projects.Open(presentationPath);
        _themeApplier.ApplyLinkedTheme(project, null, theme);
        project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
        _projects.Save(project, presentationPath);
    }

    private async void ShowImportPresentation_Click(object sender, RoutedEventArgs e)
    {
        await ImportPresentationAsync();
    }

    private async Task ImportPresentationAsync()
    {
        var file = await CreatePresentationPicker().PickSingleFileAsync();
        if (file == null)
            return;

        try
        {
            var imported = await _localCollection.ImportPresentationAsync(
                file.Path,
                ViewModel.SelectedLibraryId,
                ViewModel.SelectedPlaylistId,
                null,
                null);

            await ViewModel.RefreshCatalogAsync();
            await ViewModel.OpenImportedPresentationAsync(imported.LocalPath, imported.LibraryId, imported.PlaylistId);
            ViewModel.StatusMessage = $"Imported \"{imported.Title}\".";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async void LibrariesSectionImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await CreateFolderPicker().PickSingleFolderAsync();
        if (folder == null)
            return;

        try
        {
            var result = await _localCollection.ImportLibraryAsync(folder.Path, null);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectLibraryCommand.Execute(result.LibraryId);
            ViewModel.StatusMessage = $"Imported {result.ImportedPresentationPaths.Count} presentation(s) into the library.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async void LibrariesSectionImportPackage_Click(object sender, RoutedEventArgs e)
    {
        var file = await CreateLibraryPackagePicker().PickSingleFileAsync();
        if (file == null)
            return;

        try
        {
            var imported = await _collectionPackages.ImportLibraryAsync(file.Path);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectLibraryCommand.Execute(imported.LibraryId);
            if (imported.ImportedPresentationPaths.Count > 0)
                await ViewModel.OpenPresentationFromPathAsync(imported.ImportedPresentationPaths[0]);
            ViewModel.StatusMessage = $"Imported library package with {imported.ImportedPresentationPaths.Count} presentation(s).";
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or FileNotFoundException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async void LibrariesSectionImportPresentation_Click(object sender, RoutedEventArgs e)
    {
        var file = await CreatePresentationPicker().PickSingleFileAsync();
        if (file == null)
            return;

        var libraryId = ViewModel.SelectedLibraryId;
        if (string.IsNullOrEmpty(libraryId))
            libraryId = await PromptForLibrarySelectionAsync("Import presentation");

        if (string.IsNullOrEmpty(libraryId))
            return;

        var imported = await _localCollection.ImportPresentationAsync(file.Path, libraryId, null, null, null);
        await ViewModel.RefreshCatalogAsync();
        await ViewModel.OpenImportedPresentationAsync(imported.LocalPath, imported.LibraryId, imported.PlaylistId);
    }

    private async void PlaylistsSectionImportPresentation_Click(object sender, RoutedEventArgs e)
    {
        var file = await CreatePresentationPicker().PickSingleFileAsync();
        if (file == null)
            return;

        var playlistId = ViewModel.SelectedPlaylistId;
        if (string.IsNullOrEmpty(playlistId))
            playlistId = await PromptForPlaylistSelectionAsync("Import presentation", primaryButtonText: "Import");

        if (string.IsNullOrEmpty(playlistId))
            return;

        var imported = await _localCollection.ImportPresentationAsync(
            file.Path,
            ViewModel.SelectedLibraryId,
            playlistId,
            null,
            null);

        await ViewModel.RefreshCatalogAsync();
        await ViewModel.OpenImportedPresentationAsync(imported.LocalPath, imported.LibraryId, imported.PlaylistId);
    }

    private async void PlaylistsSectionImportPackage_Click(object sender, RoutedEventArgs e)
    {
        var file = await CreatePlaylistPackagePicker().PickSingleFileAsync();
        if (file == null)
            return;

        try
        {
            var imported = await _collectionPackages.ImportPlaylistAsync(file.Path);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectPlaylistCommand.Execute(imported.PlaylistId);
            if (imported.ImportedPresentationPaths.Count > 0)
                await ViewModel.OpenPresentationFromPathAsync(imported.ImportedPresentationPaths[0]);
            ViewModel.StatusMessage = $"Imported playlist package with {imported.ImportedPresentationPaths.Count} presentation(s).";
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or FileNotFoundException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async Task<string?> PromptForNameAsync(string title, string header, string defaultValue)
    {
        var textBox = new TextBox
        {
            Header = header,
            Text = defaultValue,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = textBox,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? NormalizeDialogValue(textBox.Text)
            : null;
    }

    private static string? NormalizeDialogValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private FileOpenPicker CreatePresentationPicker()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".cpres");
        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return picker;
    }

    private FolderPicker CreateFolderPicker()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return picker;
    }

    private FileOpenPicker CreateLibraryPackagePicker() =>
        CreateFilteredOpenPicker(".cplibrary");

    private FileOpenPicker CreatePlaylistPackagePicker() =>
        CreateFilteredOpenPicker(".cpplaylist");

    private FileOpenPicker CreateFilteredOpenPicker(string extension)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(extension);
        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return picker;
    }

    private FileSavePicker CreatePackageSavePicker(string extension, string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("Church Presenter Package", new List<string> { extension });
        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return picker;
    }

    private FileSavePicker CreatePresentationSavePicker(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("Church Presenter Presentation", new List<string> { ".cpres" });
        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return picker;
    }

    private async Task<string?> PromptForPlaylistSelectionAsync(
        string title,
        string primaryButtonText = "Add",
        string? excludedPlaylistId = null)
    {
        var candidates = ViewModel.Playlists
            .Where(playlist => !string.Equals(playlist.Id, excludedPlaylistId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            ViewModel.StatusMessage = "Create a playlist first, then add presentations to it.";
            return null;
        }

        var comboBox = new ComboBox
        {
            Header = "Playlist",
            DisplayMemberPath = nameof(PlaylistDto.Name),
            ItemsSource = candidates,
            SelectedItem = candidates.FirstOrDefault(playlist =>
                               string.Equals(playlist.Id, ViewModel.SelectedPlaylist?.Id, StringComparison.OrdinalIgnoreCase))
                           ?? candidates.FirstOrDefault(),
        };

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = comboBox,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? (comboBox.SelectedItem as PlaylistDto)?.Id
            : null;
    }

    private async Task<string?> PromptForLibrarySelectionAsync(
        string title,
        string primaryButtonText = "Import",
        string? excludedLibraryId = null)
    {
        var candidates = ViewModel.Libraries
            .Where(library => !string.Equals(library.Id, excludedLibraryId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            ViewModel.StatusMessage = "Create a library first, then import presentations into it.";
            return null;
        }

        var comboBox = new ComboBox
        {
            Header = "Library",
            DisplayMemberPath = nameof(LibraryDto.Name),
            ItemsSource = candidates,
            SelectedItem = candidates.FirstOrDefault(library =>
                               string.Equals(library.Id, ViewModel.SelectedLibrary?.Id, StringComparison.OrdinalIgnoreCase))
                           ?? candidates.FirstOrDefault(),
        };

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = comboBox,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? (comboBox.SelectedItem as LibraryDto)?.Id
            : null;
    }

    private static T? GetDataContext<T>(object sender) where T : class
    {
        if (sender is not DependencyObject start)
            return null;

        for (var current = start; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not FrameworkElement fe)
                continue;

            if (fe.DataContext is T direct)
                return direct;
        }

        return null;
    }

    private void LibraryPresentationMenu_Opening(object sender, object e)
    {
        if (!TryGetFlyoutPresentationItem(sender, out var item))
            return;

        var canResolvePath = CanResolvePresentationPath(item.Presentation.Path);
        SetMenuItemEnabled(sender, "Paste", IsPresentationClipboardValid());
        SetMenuItemEnabled(sender, "Add To", ViewModel.Libraries.Count > 1 || ViewModel.Playlists.Count > 0);
        SetMenuItemEnabled(sender, "Move To", ViewModel.Libraries.Any(library =>
            !string.Equals(library.Id, item.LibraryId, StringComparison.OrdinalIgnoreCase)));
        SetMenuItemEnabled(sender, "Delete", canResolvePath);
        SetMenuItemEnabled(sender, "Open File Location", canResolvePath);
        SetMenuItemEnabled(sender, "Document (slides only)...", canResolvePath);
        SetMenuItemEnabled(sender, "Bundle (with media)...", canResolvePath);
        SetMenuItemEnabled(sender, "Export as Images...", canResolvePath);
    }

    private void PlaylistPresentationMenu_Opening(object sender, object e)
    {
        if (!TryGetFlyoutPresentationItem(sender, out var item))
            return;

        var canResolvePath = CanResolvePresentationPath(item.Presentation.Path);
        SetMenuItemEnabled(sender, "Paste", IsPresentationClipboardValid());
        SetMenuItemEnabled(sender, "Add To", ViewModel.Libraries.Count > 0 || ViewModel.Playlists.Count > 1);
        SetMenuItemEnabled(sender, "Move To", ViewModel.Playlists.Any(playlist =>
            !string.Equals(playlist.Id, item.PlaylistId, StringComparison.OrdinalIgnoreCase)));
        SetMenuItemEnabled(sender, "Move Up", item.CanMovePlaylistUp);
        SetMenuItemEnabled(sender, "Move Down", item.CanMovePlaylistDown);
        SetMenuItemEnabled(sender, "Delete", canResolvePath);
        SetMenuItemEnabled(sender, "Open File Location", canResolvePath);
        SetMenuItemEnabled(sender, "Document (slides only)...", canResolvePath);
        SetMenuItemEnabled(sender, "Bundle (with media)...", canResolvePath);
        SetMenuItemEnabled(sender, "Export as Images...", canResolvePath);
    }

    private static bool TryGetFlyoutPresentationItem(object sender, out ShowPresentationTreeItem item)
    {
        item = null!;
        if (sender is not MenuFlyout flyout || flyout.Target is not FrameworkElement target)
            return false;

        switch (target.DataContext)
        {
            case ShowPresentationTreeItem presentationItem:
                item = presentationItem;
                return true;
            default:
                return false;
        }
    }

    private static void SetMenuItemEnabled(object sender, string text, bool isEnabled)
    {
        if (sender is not MenuFlyout flyout)
            return;

        foreach (var item in EnumerateMenuItems(flyout.Items))
        {
            switch (item)
            {
                case MenuFlyoutSubItem subItem when string.Equals(subItem.Text, text, StringComparison.Ordinal):
                    subItem.IsEnabled = isEnabled;
                    break;
                case MenuFlyoutItem menuItem when string.Equals(menuItem.Text, text, StringComparison.Ordinal):
                    menuItem.IsEnabled = isEnabled;
                    break;
            }
        }
    }

    private static void SetMenuItemText(object sender, string existingText, string nextText)
    {
        if (sender is not MenuFlyout flyout)
            return;

        foreach (var item in EnumerateMenuItems(flyout.Items))
        {
            switch (item)
            {
                case MenuFlyoutSubItem subItem when string.Equals(subItem.Text, existingText, StringComparison.Ordinal):
                    subItem.Text = nextText;
                    break;
                case MenuFlyoutItem menuItem when string.Equals(menuItem.Text, existingText, StringComparison.Ordinal):
                    menuItem.Text = nextText;
                    break;
            }
        }
    }

    private static IEnumerable<MenuFlyoutItemBase> EnumerateMenuItems(IList<MenuFlyoutItemBase> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is MenuFlyoutSubItem subItem)
            {
                foreach (var child in EnumerateMenuItems(subItem.Items))
                    yield return child;
            }
        }
    }

    private bool CanResolvePresentationPath(string path)
    {
        try
        {
            return File.Exists(_content.ResolvePresentationPath(path));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when the clipboard holds a presentation whose bundle file still exists on disk.
    /// </summary>
    private bool IsPresentationClipboardValid()
    {
        if (!_presentationClipboard.HasPresentation)
            return false;

        if (CanResolvePresentationPath(_presentationClipboard.PresentationPath!))
            return true;

        _presentationClipboard.Clear();
        return false;
    }

    /// <summary>
    /// Shared error pipeline for sidebar presentation-row actions.
    /// Catches exceptions and surfaces them as a status message so the UI stays stable.
    /// </summary>
    private async Task ExecuteSidebarActionAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // user cancelled a dialog or picker - nothing to report
        }
        catch (FileNotFoundException ex)
        {
            ViewModel.StatusMessage = $"{failurePrefix}: {Path.GetFileName(ex.FileName ?? ex.Message)} not found.";
        }
        catch (InvalidOperationException ex)
        {
            ViewModel.StatusMessage = $"{failurePrefix}: {ex.Message}";
        }
        catch (IOException ex)
        {
            ViewModel.StatusMessage = $"{failurePrefix}: {ex.Message}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"{failurePrefix}: {ex.Message}";
        }
    }
    private void LibrarySidebarGroup_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShowLibraryTreeItem item)
            ViewModel.SelectLibraryCommand.Execute(item.Library.Id);
    }

    private void PlaylistSidebarGroup_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShowPlaylistTreeItem item)
            ViewModel.SelectPlaylistCommand.Execute(item.Playlist.Id);
    }

    private void PresentationSidebarRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShowPresentationTreeItem item)
            ActivateTreePresentationItem(item);
    }

    private void ActivateTreePresentationItem(ShowPresentationTreeItem item)
    {
        if (!string.IsNullOrEmpty(item.LibraryId))
            ViewModel.SelectLibraryCommand.Execute(item.LibraryId);
        else if (!string.IsNullOrEmpty(item.PlaylistId))
            ViewModel.SelectPlaylistCommand.Execute(item.PlaylistId);

        ViewModel.SelectPresentationCommand.Execute(item.Presentation.Path);
    }

    private async void LibraryImportPresentation_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowLibraryTreeItem>(sender) is not { } item)
            return;

        var file = await CreatePresentationPicker().PickSingleFileAsync();
        if (file == null)
            return;

        var imported = await _localCollection.ImportPresentationAsync(file.Path, item.Library.Id, null, null, null);
        await ViewModel.RefreshCatalogAsync();
        await ViewModel.OpenImportedPresentationAsync(imported.LocalPath, imported.LibraryId, imported.PlaylistId);
    }

    private async void LibraryExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowLibraryTreeItem>(sender) is not { } item)
            return;

        var file = await CreatePackageSavePicker(".cplibrary", item.Library.Name).PickSaveFileAsync();
        if (file == null)
            return;

        try
        {
            await _collectionPackages.ExportLibraryAsync(item.Library.Id, file.Path);
            ViewModel.StatusMessage = $"Exported {item.Library.Name} package.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async void LibraryImportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowLibraryTreeItem>(sender) is not { } item)
            return;

        var folder = await CreateFolderPicker().PickSingleFolderAsync();
        if (folder == null)
            return;

        await _localCollection.ImportLibraryAsync(folder.Path, item.Library.Name);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.SelectLibraryCommand.Execute(item.Library.Id);
        ViewModel.StatusMessage = $"Imported library content into {item.Library.Name}.";
    }

    private async void LibraryRename_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowLibraryTreeItem>(sender) is not { } item)
            return;

        var name = await PromptForNameAsync("Rename Library", "Library name", item.Library.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await _localCollection.RenameLibraryAsync(item.Library.Id, name);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.SelectLibraryCommand.Execute(item.Library.Id);
    }

    private async void LibraryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowLibraryTreeItem>(sender) is not { } item)
            return;

        if (!await ConfirmAsync("Delete Library", $"Delete \"{item.Library.Name}\" from the collection?", "Delete"))
            return;

        await _localCollection.DeleteLibraryAsync(item.Library.Id);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.StatusMessage = $"Deleted {item.Library.Name}.";
    }

    private async void PlaylistImportPresentation_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPlaylistTreeItem>(sender) is not { } item)
            return;

        var file = await CreatePresentationPicker().PickSingleFileAsync();
        if (file == null)
            return;

        var imported = await _localCollection.ImportPresentationAsync(
            file.Path,
            ViewModel.SelectedLibraryId,
            item.Playlist.Id,
            null,
            null);

        await ViewModel.RefreshCatalogAsync();
        await ViewModel.OpenImportedPresentationAsync(imported.LocalPath, imported.LibraryId, imported.PlaylistId);
    }

    private async void PlaylistExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPlaylistTreeItem>(sender) is not { } item)
            return;

        var exportOptions = await PromptForPlaylistExportOptionsAsync();
        if (exportOptions == null)
            return;

        var file = await CreatePackageSavePicker(".cpplaylist", item.Playlist.Name).PickSaveFileAsync();
        if (file == null)
            return;

        try
        {
            await _collectionPackages.ExportPlaylistAsync(item.Playlist.Id, file.Path, exportOptions);
            ViewModel.StatusMessage = $"Exported {item.Playlist.Name} package.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
    }

    private async Task<CollectionPackageExportOptions?> PromptForPlaylistExportOptionsAsync()
    {
        var includeMediaCheckBox = new CheckBox
        {
            Content = "Include media in embedded presentations",
            IsChecked = true,
        };
        var dialog = new ContentDialog
        {
            Title = "Export Playlist Package",
            Content = includeMediaCheckBox,
            PrimaryButtonText = "Export",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        ContentDialogSetup.ConfigureForPage(dialog, this);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return new CollectionPackageExportOptions
        {
            IncludeMedia = includeMediaCheckBox.IsChecked == true,
            IncludeFonts = includeMediaCheckBox.IsChecked == true,
        };
    }

    private async void PlaylistRename_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPlaylistTreeItem>(sender) is not { } item)
            return;

        var name = await PromptForNameAsync("Rename Playlist", "Playlist name", item.Playlist.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await _localCollection.RenamePlaylistAsync(item.Playlist.Id, name);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.SelectPlaylistCommand.Execute(item.Playlist.Id);
    }

    private async void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPlaylistTreeItem>(sender) is not { } item)
            return;

        if (!await ConfirmAsync("Delete Playlist", $"Delete \"{item.Playlist.Name}\" from the collection?", "Delete"))
            return;

        await _localCollection.DeletePlaylistAsync(item.Playlist.Id);
        await ViewModel.RefreshCatalogAsync();
        ViewModel.StatusMessage = $"Deleted {item.Playlist.Name}.";
    }

    private async Task AddPresentationToLibraryAsync(ShowPresentationTreeItem item, string libraryId)
    {
        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.AddPresentationToLibraryAsync(libraryId, item.Presentation.Path);
            await RestorePresentationSourceSelectionAsync(item);
            ViewModel.StatusMessage = $"Added \"{item.Presentation.Title}\" to library.";
        }, "Could not add to library");
    }

    private async Task AddPresentationToPlaylistAsync(ShowPresentationTreeItem item, string playlistId)
    {
        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.AddPresentationToPlaylistAsync(playlistId, item.Presentation.Path);
            await RestorePresentationSourceSelectionAsync(item);
            ViewModel.StatusMessage = $"Added \"{item.Presentation.Title}\" to playlist.";
        }, "Could not add to playlist");
    }

    private async Task MovePresentationToLibraryAsync(ShowPresentationTreeItem item, string? sourceLibraryId, string targetLibraryId)
    {
        if (string.IsNullOrWhiteSpace(sourceLibraryId))
        {
            ViewModel.StatusMessage = "Add this presentation to a library before moving it.";
            return;
        }

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.MovePresentationToLibraryAsync(sourceLibraryId, targetLibraryId, item.Presentation.Path);
            if (!string.IsNullOrWhiteSpace(item.PlaylistId))
                await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, item.Presentation.Path);
            else
                await RefreshSidebarAfterMutationAsync(targetLibraryId, null, item.Presentation.Path);
            ViewModel.StatusMessage = $"Moved \"{item.Presentation.Title}\" to library.";
        }, "Could not move to library");
    }

    private async Task SetPresentationArrangementAsync(ShowPresentationTreeItem item, string arrangementId)
    {
        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.SetPresentationReferenceArrangementAsync(
                item.LibraryId,
                item.PlaylistId,
                item.Presentation.Path,
                arrangementId,
                item.PlaylistIndex);
            await RefreshSidebarAfterMutationAsync(item.LibraryId, item.PlaylistId, item.Presentation.Path);
            ViewModel.StatusMessage = $"Updated arrangement for \"{item.Presentation.Title}\".";
        }, "Could not update arrangement");
    }

    private async Task SetPresentationDestinationAsync(ShowPresentationTreeItem item, BackendOutputLayerKind layerKind)
    {
        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.SetPresentationReferenceDestinationAsync(
                item.LibraryId,
                item.PlaylistId,
                item.Presentation.Path,
                OutputRoutingDefaults.GetLayerId(layerKind),
                item.PlaylistIndex);
            await RefreshSidebarAfterMutationAsync(item.LibraryId, item.PlaylistId, item.Presentation.Path);
            ViewModel.StatusMessage = $"Updated destination for \"{item.Presentation.Title}\".";
        }, "Could not update destination");
    }

    private async Task ResizePresentationAsync(ShowPresentationTreeItem item, PresentationSizeChoice choice)
    {
        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.ResizePresentationAsync(item.Presentation.Path, choice.Size);
            await RefreshSidebarAfterMutationAsync(item.LibraryId, item.PlaylistId, item.Presentation.Path);
            ViewModel.StatusMessage = $"Resized \"{item.Presentation.Title}\" to {choice.Size.Width} x {choice.Size.Height}.";
        }, "Could not resize presentation");
    }

    private async Task ResizePresentationWithCustomSizeAsync(ShowPresentationTreeItem item)
    {
        PresentationSizeChoice currentSize;
        try
        {
            var project = _projects.Open(item.Presentation.Path);
            var size = PresentationModelUtilities.GetBaseSlideSize(project.Manifest.AspectRatio, project.Manifest.SlideSize);
            currentSize = CreatePresentationSizeChoice("Current", size.Width, size.Height);
        }
        catch
        {
            currentSize = BuildCommonPresentationSizeChoices().First();
        }

        var customSize = await PromptForCustomPresentationSizeAsync(currentSize);
        if (customSize != null)
            await ResizePresentationAsync(item, customSize);
    }

    private void CopyPresentation(ShowPresentationTreeItem item)
    {
        _presentationClipboard.SetPresentation(item.Presentation.Path);
        ViewModel.StatusMessage = $"Copied \"{item.Presentation.Title}\".";
    }

    private async Task PastePresentationAsync(ShowPresentationTreeItem item)
    {
        if (!IsPresentationClipboardValid())
        {
            ViewModel.StatusMessage = "Nothing to paste - clipboard is empty or source no longer exists.";
            return;
        }

        await ExecuteSidebarActionAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(item.LibraryId))
            {
                var duplicated = await _presentationActions.DuplicatePresentationAsync(
                    _presentationClipboard.PresentationPath!,
                    item.LibraryId,
                    targetPlaylistId: null);
                await RefreshSidebarAfterMutationAsync(item.LibraryId, null, duplicated.PresentationPath);
                ViewModel.StatusMessage = $"Pasted \"{duplicated.Title}\" into the library.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.PlaylistId))
            {
                var targetLibraryId = await ResolveTargetLibraryIdForPlaylistMutationAsync(_presentationClipboard.PresentationPath!);
                if (string.IsNullOrWhiteSpace(targetLibraryId))
                    return;

                var duplicated = await _presentationActions.DuplicatePresentationAsync(
                    _presentationClipboard.PresentationPath!,
                    targetLibraryId,
                    item.PlaylistId);
                await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, duplicated.PresentationPath);
                ViewModel.StatusMessage = $"Pasted \"{duplicated.Title}\" into the playlist.";
            }
        }, "Could not paste presentation");
    }

    private async Task RemovePresentationFromSourceAsync(ShowPresentationTreeItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.PlaylistId))
        {
            var removeAllInstances = await ConfirmRemovePlaylistPresentationAsync(item);
            if (removeAllInstances == null)
            {
                return;
            }

            await ExecuteSidebarActionAsync(async () =>
            {
                ClearSelectedPresentationIfMatches(item.Presentation.Path);
                await _localCollection.RemovePresentationFromPlaylistAsync(
                    item.PlaylistId,
                    item.Presentation.Path,
                    item.PlaylistIndex,
                    removeAllInstances.Value);
                await ViewModel.RefreshCatalogAsync();
                ViewModel.SelectPlaylistCommand.Execute(item.PlaylistId);
                ViewModel.StatusMessage = removeAllInstances.Value
                    ? $"Removed all instances of \"{item.Presentation.Title}\" from the playlist."
                    : $"Removed \"{item.Presentation.Title}\" from the playlist.";
            }, "Could not remove presentation from playlist");
            return;
        }

        if (string.IsNullOrWhiteSpace(item.LibraryId))
            return;

        if (!await ConfirmAsync(
                "Delete Presentation",
                $"Delete \"{item.Presentation.Title}\" everywhere?",
                "Delete"))
        {
            return;
        }

        await ExecuteSidebarActionAsync(async () =>
        {
            var deleted = await _presentationActions.DeletePresentationAsync(item.Presentation.Path);
            InvalidatePresentationClipboard(deleted.PresentationPath);
            await RefreshSidebarAfterPresentationDeleteAsync(item.LibraryId, null, deleted.PresentationPath);
            ViewModel.StatusMessage = $"Deleted \"{deleted.Title}\".";
        }, "Could not delete presentation");
    }

    private void ClearSelectedPresentationIfMatches(string presentationPath)
    {
        if (ViewModel.PresentationPathsMatch(ViewModel.SelectedPresentationPath, presentationPath))
            ViewModel.SelectedPresentationPath = null;
    }

    private async Task<bool?> ConfirmRemovePlaylistPresentationAsync(ShowPresentationTreeItem item)
    {
        var instanceCount = ViewModel.Playlists
            .FirstOrDefault(playlist => string.Equals(playlist.Id, item.PlaylistId, StringComparison.OrdinalIgnoreCase))
            ?.Items.Count(presentation => ViewModel.PresentationPathsMatch(presentation.Path, item.Presentation.Path)) ?? 0;

        if (instanceCount <= 1)
        {
            return await ConfirmAsync(
                "Remove from Playlist",
                $"Remove \"{item.Presentation.Title}\" from this playlist?",
                "Remove")
                ? false
                : null;
        }

        var removeAllCheckBox = new CheckBox
        {
            Content = $"Remove all {instanceCount} instances of this presentation from the playlist",
            IsChecked = false,
        };
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Remove this instance of \"{item.Presentation.Title}\" from the playlist?",
                    TextWrapping = TextWrapping.Wrap,
                },
                removeAllCheckBox,
            },
        };
        var dialog = new ContentDialog
        {
            Title = "Remove from Playlist",
            Content = content,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? removeAllCheckBox.IsChecked == true
            : null;
    }

    private async Task DuplicatePresentationAsync(ShowPresentationTreeItem item)
    {
        await ExecuteSidebarActionAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(item.LibraryId))
            {
                var duplicated = await _presentationActions.DuplicatePresentationAsync(item.Presentation.Path, item.LibraryId, targetPlaylistId: null);
                await RefreshSidebarAfterMutationAsync(item.LibraryId, null, duplicated.PresentationPath);
                ViewModel.StatusMessage = $"Duplicated \"{item.Presentation.Title}\".";
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.PlaylistId))
            {
                var targetLibraryId = await ResolveTargetLibraryIdForPlaylistMutationAsync(item.Presentation.Path);
                if (string.IsNullOrWhiteSpace(targetLibraryId))
                    return;

                var duplicated = await _presentationActions.DuplicatePresentationAsync(item.Presentation.Path, targetLibraryId, item.PlaylistId);
                await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, duplicated.PresentationPath);
                ViewModel.StatusMessage = $"Duplicated \"{item.Presentation.Title}\".";
            }
        }, "Could not duplicate presentation");
    }

    private async Task RenamePresentationAsync(ShowPresentationTreeItem item)
    {
        var name = await PromptForNameAsync("Rename Presentation", "Presentation name", item.Presentation.Title);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var renamed = await _presentationActions.RenamePresentationAsync(item.Presentation.Path, name);
            RemapPresentationClipboard(item.Presentation.Path, renamed.NewPresentationPath);
            await RefreshSidebarAfterMutationAsync(item.LibraryId, item.PlaylistId, renamed.NewPresentationPath);
            ViewModel.StatusMessage = $"Renamed to \"{renamed.Title}\".";
        }, "Could not rename presentation");
    }

    private Task EditPresentationAsync(ShowPresentationTreeItem item) =>
        OpenPresentationInEditorAsync(item.Presentation.Path, item.LibraryId, item.PlaylistId);

    private Task ReflowPresentationAsync(ShowPresentationTreeItem item) =>
        OpenPresentationInReflowAsync(item.Presentation.Path, item.LibraryId, item.PlaylistId);

    private async Task ExportPresentationDocumentAsync(ShowPresentationTreeItem item)
    {
        var file = await CreatePresentationSavePicker(item.Presentation.Title).PickSaveFileAsync();
        if (file == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.ExportPresentationDocumentAsync(item.Presentation.Path, file.Path);
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\" (document).";
        }, "Could not export presentation");
    }

    private async Task ExportPresentationBundleAsync(ShowPresentationTreeItem item)
    {
        var file = await CreatePresentationSavePicker(item.Presentation.Title).PickSaveFileAsync();
        if (file == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.ExportPresentationBundleAsync(item.Presentation.Path, file.Path);
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\" (bundle).";
        }, "Could not export presentation");
    }

    private async Task ExportPresentationImagesAsync(ShowPresentationTreeItem item)
    {
        var folder = await CreateFolderPicker().PickSingleFolderAsync();
        if (folder == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var result = await ExportPresentationSlidesAsImagesAsync(item.Presentation.Path, folder.Path);
            ViewModel.StatusMessage = result.FailedCount == 0
                ? $"Exported {result.ExportedCount} slide image(s) for \"{item.Presentation.Title}\"."
                : $"Exported {result.ExportedCount} slide(s), {result.FailedCount} failed for \"{item.Presentation.Title}\".";
        }, "Could not export images");
    }

    private async Task RestorePresentationSourceSelectionAsync(ShowPresentationTreeItem item)
    {
        await ViewModel.RefreshCatalogAsync();
        if (!string.IsNullOrWhiteSpace(item.LibraryId))
            ViewModel.SelectLibraryCommand.Execute(item.LibraryId);
        else if (!string.IsNullOrWhiteSpace(item.PlaylistId))
            ViewModel.SelectPlaylistCommand.Execute(item.PlaylistId);
    }

    private async void LibraryPresentationAddToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.LibraryId))
            return;

        var libraryId = await PromptForLibrarySelectionAsync("Add To Library", primaryButtonText: "Add", excludedLibraryId: item.LibraryId);
        if (string.IsNullOrWhiteSpace(libraryId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.AddPresentationToLibraryAsync(libraryId, item.Presentation.Path);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectLibraryCommand.Execute(item.LibraryId);
            ViewModel.StatusMessage = $"Added \"{item.Presentation.Title}\" to library.";
        }, "Could not add to library");
    }

    private async void LibraryPresentationAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var playlistId = await PromptForPlaylistSelectionAsync("Add To Playlist");
        if (string.IsNullOrWhiteSpace(playlistId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.AddPresentationToPlaylistAsync(playlistId, item.Presentation.Path);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectLibraryCommand.Execute(item.LibraryId);
            ViewModel.StatusMessage = $"Added \"{item.Presentation.Title}\" to playlist.";
        }, "Could not add to playlist");
    }

    private async void LibraryPresentationMoveToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.LibraryId))
            return;

        var targetLibraryId = await PromptForLibrarySelectionAsync("Move To Library", primaryButtonText: "Move", excludedLibraryId: item.LibraryId);
        if (string.IsNullOrWhiteSpace(targetLibraryId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.MovePresentationToLibraryAsync(item.LibraryId, targetLibraryId, item.Presentation.Path);
            await RefreshSidebarAfterMutationAsync(targetLibraryId, null, item.Presentation.Path);
            ViewModel.StatusMessage = $"Moved \"{item.Presentation.Title}\" to library.";
        }, "Could not move to library");
    }

    private async void LibraryPresentationEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        await ExecuteSidebarActionAsync(
            () => OpenPresentationInEditorAsync(item.Presentation.Path, item.LibraryId, null),
            "Could not open editor");
    }

    private async void LibraryPresentationReflow_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        await ExecuteSidebarActionAsync(
            () => OpenPresentationInReflowAsync(item.Presentation.Path, item.LibraryId, null),
            "Could not open reflow");
    }

    private void LibraryPresentationCopy_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        _presentationClipboard.SetPresentation(item.Presentation.Path);
        ViewModel.StatusMessage = $"Copied \"{item.Presentation.Title}\".";
    }

    private async void LibraryPresentationPaste_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.LibraryId))
            return;

        if (!IsPresentationClipboardValid())
        {
            ViewModel.StatusMessage = "Nothing to paste — clipboard is empty or source no longer exists.";
            return;
        }

        await ExecuteSidebarActionAsync(async () =>
        {
            var duplicated = await _presentationActions.DuplicatePresentationAsync(
                _presentationClipboard.PresentationPath!,
                item.LibraryId,
                targetPlaylistId: null);

            await RefreshSidebarAfterMutationAsync(item.LibraryId, null, duplicated.PresentationPath);
            ViewModel.StatusMessage = $"Pasted \"{duplicated.Title}\" into the library.";
        }, "Could not paste presentation");
    }

    private async void LibraryPresentationDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.LibraryId))
            return;

        await RemovePresentationFromSourceAsync(item);
    }

    private async void LibraryPresentationDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.LibraryId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var duplicated = await _presentationActions.DuplicatePresentationAsync(item.Presentation.Path, item.LibraryId, targetPlaylistId: null);
            await RefreshSidebarAfterMutationAsync(item.LibraryId, null, duplicated.PresentationPath);
            ViewModel.StatusMessage = $"Duplicated \"{item.Presentation.Title}\".";
        }, "Could not duplicate presentation");
    }

    private async void LibraryPresentationRename_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var name = await PromptForNameAsync("Rename Presentation", "Presentation name", item.Presentation.Title);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var renamed = await _presentationActions.RenamePresentationAsync(item.Presentation.Path, name);
            RemapPresentationClipboard(item.Presentation.Path, renamed.NewPresentationPath);
            await RefreshSidebarAfterMutationAsync(item.LibraryId, null, renamed.NewPresentationPath);
            ViewModel.StatusMessage = $"Renamed to \"{renamed.Title}\".";
        }, "Could not rename presentation");
    }

    private void LibraryPresentationOpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        OpenPresentationFileLocation(item.Presentation.Path);
    }

    private async void LibraryPresentationExportBundle_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var file = await CreatePresentationSavePicker(item.Presentation.Title).PickSaveFileAsync();
        if (file == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.ExportPresentationBundleAsync(item.Presentation.Path, file.Path);
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\" (bundle).";
        }, "Could not export presentation");
    }

    private async void LibraryPresentationExportImages_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var folder = await CreateFolderPicker().PickSingleFolderAsync();
        if (folder == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var result = await ExportPresentationSlidesAsImagesAsync(item.Presentation.Path, folder.Path);
            ViewModel.StatusMessage = result.FailedCount == 0
                ? $"Exported {result.ExportedCount} slide image(s) for \"{item.Presentation.Title}\"."
                : $"Exported {result.ExportedCount} slide(s), {result.FailedCount} failed for \"{item.Presentation.Title}\".";
        }, "Could not export images");
    }

    private async void PlaylistPresentationAddToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var libraryId = await PromptForLibrarySelectionAsync("Add To Library", primaryButtonText: "Add");
        if (string.IsNullOrWhiteSpace(libraryId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.AddPresentationToLibraryAsync(libraryId, item.Presentation.Path);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectPlaylistCommand.Execute(item.PlaylistId);
            ViewModel.StatusMessage = $"Added \"{item.Presentation.Title}\" to library.";
        }, "Could not add to library");
    }

    private async void PlaylistPresentationAddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.PlaylistId))
            return;

        var playlistId = await PromptForPlaylistSelectionAsync("Add To Playlist", excludedPlaylistId: item.PlaylistId);
        if (string.IsNullOrWhiteSpace(playlistId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.AddPresentationToPlaylistAsync(playlistId, item.Presentation.Path);
            await ViewModel.RefreshCatalogAsync();
            ViewModel.SelectPlaylistCommand.Execute(item.PlaylistId);
            ViewModel.StatusMessage = $"Added \"{item.Presentation.Title}\" to another playlist.";
        }, "Could not add to playlist");
    }

    private async void PlaylistPresentationMoveToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.PlaylistId))
            return;

        var playlistId = await PromptForPlaylistSelectionAsync("Move To Playlist", primaryButtonText: "Move", excludedPlaylistId: item.PlaylistId);
        if (string.IsNullOrWhiteSpace(playlistId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.MovePresentationToPlaylistAsync(
                item.PlaylistId,
                playlistId,
                item.Presentation.Path,
                sourcePlaylistIndex: item.PlaylistIndex);
            await RefreshSidebarAfterMutationAsync(null, playlistId, item.Presentation.Path);
            ViewModel.StatusMessage = $"Moved \"{item.Presentation.Title}\" to another playlist.";
        }, "Could not move to playlist");
    }

    private async void PlaylistPresentationEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        await ExecuteSidebarActionAsync(
            () => OpenPresentationInEditorAsync(item.Presentation.Path, null, item.PlaylistId),
            "Could not open editor");
    }

    private async void PlaylistPresentationReflow_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        await ExecuteSidebarActionAsync(
            () => OpenPresentationInReflowAsync(item.Presentation.Path, null, item.PlaylistId),
            "Could not open reflow");
    }

    private void PlaylistPresentationCopy_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        _presentationClipboard.SetPresentation(item.Presentation.Path);
        ViewModel.StatusMessage = $"Copied \"{item.Presentation.Title}\".";
    }

    private async void PlaylistPresentationPaste_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.PlaylistId))
            return;

        if (!IsPresentationClipboardValid())
        {
            ViewModel.StatusMessage = "Nothing to paste — clipboard is empty or source no longer exists.";
            return;
        }

        var targetLibraryId = await ResolveTargetLibraryIdForPlaylistMutationAsync(_presentationClipboard.PresentationPath!);
        if (string.IsNullOrWhiteSpace(targetLibraryId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var duplicated = await _presentationActions.DuplicatePresentationAsync(
                _presentationClipboard.PresentationPath!,
                targetLibraryId,
                item.PlaylistId);

            await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, duplicated.PresentationPath);
            ViewModel.StatusMessage = $"Pasted \"{duplicated.Title}\" into the playlist.";
        }, "Could not paste presentation");
    }

    private async void PlaylistPresentationMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item
            || string.IsNullOrWhiteSpace(item.PlaylistId)
            || !item.CanMovePlaylistUp)
        {
            return;
        }

        await ExecuteSidebarActionAsync(async () =>
        {
            await _localCollection.MovePlaylistItemAsync(item.PlaylistId, item.Presentation.Path, -1, item.PlaylistIndex);
            await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, item.Presentation.Path);
        }, "Could not move item up");
    }

    private async void PlaylistPresentationMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item
            || string.IsNullOrWhiteSpace(item.PlaylistId)
            || !item.CanMovePlaylistDown)
        {
            return;
        }

        await ExecuteSidebarActionAsync(async () =>
        {
            await _localCollection.MovePlaylistItemAsync(item.PlaylistId, item.Presentation.Path, 1, item.PlaylistIndex);
            await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, item.Presentation.Path);
        }, "Could not move item down");
    }

    private async void PlaylistPresentationDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.PlaylistId))
            return;

        await RemovePresentationFromSourceAsync(item);
    }

    private async void PlaylistPresentationDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item || string.IsNullOrWhiteSpace(item.PlaylistId))
            return;

        var targetLibraryId = await ResolveTargetLibraryIdForPlaylistMutationAsync(item.Presentation.Path);
        if (string.IsNullOrWhiteSpace(targetLibraryId))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var duplicated = await _presentationActions.DuplicatePresentationAsync(item.Presentation.Path, targetLibraryId, item.PlaylistId);
            await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, duplicated.PresentationPath);
            ViewModel.StatusMessage = $"Duplicated \"{item.Presentation.Title}\".";
        }, "Could not duplicate presentation");
    }

    private async void PlaylistPresentationRename_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var name = await PromptForNameAsync("Rename Presentation", "Presentation name", item.Presentation.Title);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var renamed = await _presentationActions.RenamePresentationAsync(item.Presentation.Path, name);
            RemapPresentationClipboard(item.Presentation.Path, renamed.NewPresentationPath);
            await RefreshSidebarAfterMutationAsync(null, item.PlaylistId, renamed.NewPresentationPath);
            ViewModel.StatusMessage = $"Renamed to \"{renamed.Title}\".";
        }, "Could not rename presentation");
    }

    private void PlaylistPresentationOpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        OpenPresentationFileLocation(item.Presentation.Path);
    }

    private async void PlaylistPresentationExportBundle_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var file = await CreatePresentationSavePicker(item.Presentation.Title).PickSaveFileAsync();
        if (file == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.ExportPresentationBundleAsync(item.Presentation.Path, file.Path);
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\" (bundle).";
        }, "Could not export presentation");
    }

    private async void PlaylistPresentationExportImages_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<ShowPresentationTreeItem>(sender) is not { } item)
            return;

        var folder = await CreateFolderPicker().PickSingleFolderAsync();
        if (folder == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            var result = await ExportPresentationSlidesAsImagesAsync(item.Presentation.Path, folder.Path);
            ViewModel.StatusMessage = result.FailedCount == 0
                ? $"Exported {result.ExportedCount} slide image(s) for \"{item.Presentation.Title}\"."
                : $"Exported {result.ExportedCount} slide(s), {result.FailedCount} failed for \"{item.Presentation.Title}\".";
        }, "Could not export images");
    }

    private async Task RefreshSidebarAfterMutationAsync(string? libraryId, string? playlistId, string? presentationPath, string? slideId = null)
    {
        await ViewModel.RefreshCatalogAsync();
        if (!string.IsNullOrWhiteSpace(libraryId))
            ViewModel.SelectLibraryCommand.Execute(libraryId);
        else if (!string.IsNullOrWhiteSpace(playlistId))
            ViewModel.SelectPlaylistCommand.Execute(playlistId);

        if (!string.IsNullOrWhiteSpace(presentationPath))
        {
            await ViewModel.OpenPresentationFromPathAsync(presentationPath);
            if (!string.IsNullOrWhiteSpace(slideId))
                await ViewModel.ActivateSlideSelectionAsync(presentationPath, slideId);
        }
    }

    private async Task RefreshSidebarAfterPresentationDeleteAsync(string? libraryId, string? playlistId, string deletedPresentationPath)
    {
        await ViewModel.RefreshCatalogAsync();
        if (!string.IsNullOrWhiteSpace(libraryId))
            ViewModel.SelectLibraryCommand.Execute(libraryId);
        else if (!string.IsNullOrWhiteSpace(playlistId))
            ViewModel.SelectPlaylistCommand.Execute(playlistId);

        await ViewModel.ClearDeletedPresentationAsync(deletedPresentationPath);
    }

    private void InvalidatePresentationClipboard(string deletedPath)
    {
        if (_presentationClipboard.HasPresentation &&
            PathsMatch(_presentationClipboard.PresentationPath!, deletedPath))
        {
            _presentationClipboard.Clear();
        }
    }

    private void RemapPresentationClipboard(string oldPath, string newPath)
    {
        if (_presentationClipboard.HasPresentation &&
            PathsMatch(_presentationClipboard.PresentationPath!, oldPath))
        {
            _presentationClipboard.SetPresentation(newPath);
        }
    }

    private async Task OpenPresentationInEditorAsync(string presentationPath, string? libraryId, string? playlistId, string? slideId = null)
    {
        await RefreshSidebarAfterMutationAsync(libraryId, playlistId, presentationPath, slideId);
        if (App.MainWindow is MainWindow window)
            window.NavigateToEditorPage();
    }

    private async Task OpenPresentationInReflowAsync(string presentationPath, string? libraryId, string? playlistId, string? slideId = null)
    {
        await RefreshSidebarAfterMutationAsync(libraryId, playlistId, presentationPath, slideId);
        if (App.MainWindow is MainWindow window)
            window.NavigateToReflowPage();
    }

    private async Task<string?> ResolveTargetLibraryIdForPlaylistMutationAsync(string presentationPath)
    {
        var preferred = ResolvePreferredLibraryIdForPresentation(presentationPath);
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        if (!string.IsNullOrWhiteSpace(ViewModel.SelectedLibraryId))
            return ViewModel.SelectedLibraryId;

        return await PromptForLibrarySelectionAsync("Choose Library", primaryButtonText: "Select");
    }

    private string? ResolvePreferredLibraryIdForPresentation(string presentationPath)
    {
        foreach (var library in ViewModel.Libraries)
        {
            if (library.Presentations.Any(presentation => PathsMatch(presentation.Path, presentationPath)))
                return library.Id;
        }

        return null;
    }

    private bool PathsMatch(string left, string right)
    {
        try
        {
            return string.Equals(
                _content.ResolvePresentationPath(left),
                _content.ResolvePresentationPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OpenPresentationFileLocation(string presentationPath)
    {
        try
        {
            var resolvedPath = _content.ResolvePresentationPath(presentationPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{resolvedPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open file location: {ex.Message}";
        }
    }

    private async Task<SlideImageExportResult> ExportPresentationSlidesAsImagesAsync(string presentationPath, string targetFolderPath)
    {
        var project = _projects.Open(presentationPath);
        var engine = App.Services.GetRequiredService<ThumbnailEngine>();
        var exported = 0;
        var failed = 0;

        Directory.CreateDirectory(targetFolderPath);

        foreach (var slide in project.Slides)
        {
            try
            {
                var fileName = $"{SanitizeFileName(project.Manifest.Title)}_{exported + failed + 1:D3}.png";
                var outputPath = Path.Combine(targetFolderPath, fileName);
                await using var stream = File.Open(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                using var randomAccessStream = stream.AsRandomAccessStream();
                await engine.SaveSlidePngAsync(project, slide, RenderIntent.Export, randomAccessStream);
                exported++;
            }
            catch
            {
                failed++;
            }
        }

        if (exported == 0 && failed > 0)
            throw new InvalidOperationException($"All {failed} slide(s) failed to render. No images were exported.");

        return new SlideImageExportResult(exported, failed);
    }

    private static async Task SavePixelsAsPngAsync(string outputPath, IBuffer pixels, int width, int height)
    {
        await using var stream = File.Open(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var randomAccessStream = stream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccessStream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)width,
            (uint)height,
            96,
            96,
            pixels.ToArray());
        await encoder.FlushAsync();
    }

    private static string SanitizeFileName(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "Presentation" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            source = source.Replace(invalid, '_');
        return source;
    }
    private sealed record SlideImageExportResult(int ExportedCount, int FailedCount);

    private sealed record ShowPresentationSearchSuggestion(
        string Title,
        string PresentationPath,
        string? LibraryId,
        string? PlaylistId,
        string SourceLabel,
        bool IsPlaceholder = false)
    {
        public string DisplayText => $"{Title} - {SourceLabel}";
    }

    private enum ShowSourceNavigationKind
    {
        Library,
        Playlist,
        Presentation,
    }

    private sealed record ShowSourceNavigationTag(
        ShowSourceNavigationKind Kind,
        string? LibraryId,
        string? PlaylistId,
        string? PresentationPath,
        int PlaylistIndex)
    {
        public static ShowSourceNavigationTag ForLibrary(string libraryId) =>
            new(ShowSourceNavigationKind.Library, libraryId, null, null, -1);

        public static ShowSourceNavigationTag ForPlaylist(string playlistId) =>
            new(ShowSourceNavigationKind.Playlist, null, playlistId, null, -1);

        public static ShowSourceNavigationTag ForPresentation(string presentationPath, string? libraryId, string? playlistId, int playlistIndex) =>
            new(ShowSourceNavigationKind.Presentation, libraryId, playlistId, presentationPath, playlistIndex);
    }

    private sealed record SourceNavigationDragPayload(ShowSourceNavigationKind Kind, string SourceId);

    private sealed record PresentationNavigationDragPayload(
        string PresentationPath,
        string Title,
        string? SourceLibraryId,
        string? SourcePlaylistId,
        int SourcePlaylistIndex);

    public ShowViewModel ViewModel
    {
        get => (ShowViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ShowViewModel),
            typeof(ShowSidebarView),
            new PropertyMetadata(null, OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ShowSidebarView view)
            return;

        if (e.OldValue is ShowViewModel oldVm)
        {
            oldVm.PropertyChanged -= view.ViewModel_SourcesPropertyChanged;
            oldVm.Libraries.CollectionChanged -= view.CatalogCollections_CollectionChanged;
            oldVm.Playlists.CollectionChanged -= view.CatalogCollections_CollectionChanged;
        }

        if (e.NewValue is ShowViewModel newVm)
        {
            newVm.PropertyChanged += view.ViewModel_SourcesPropertyChanged;
            newVm.Libraries.CollectionChanged += view.CatalogCollections_CollectionChanged;
            newVm.Playlists.CollectionChanged += view.CatalogCollections_CollectionChanged;
            view.UpdateSourcesSearchBoxHost();
            view.RebuildSourcesNavigation();
            view.UpdateSourcesNavigationSelection();
            view.RefreshSourcesSearchSuggestions();
        }
    }

    /// <summary>Off-screen host on ShowPage for presentation drag bitmaps.</summary>
    public Grid? PresentationDragPreviewHost { get; set; }

    /// <summary>Host page keyboard focus target (ShowPage.KeyboardFocusTarget).</summary>
    public Action? RestoreKeyboardFocus { get; set; }

    public Grid? DropOverlayRoot { get; set; }

    public ShowSidebarView()
    {
        InitializeComponent();
        var services = App.Services;
        _localCollection = services.GetRequiredService<ILocalCollectionService>();
        _collectionPackages = services.GetRequiredService<ICollectionPackageService>();
        _projects = services.GetRequiredService<IPresentationProjectService>();
        _content = services.GetRequiredService<IContentDirectoryService>();
        _presentationActions = services.GetRequiredService<IPresentationItemActionService>();
        _presentationClipboard = services.GetRequiredService<ISidebarPresentationClipboardService>();
        _themeLibrary = services.GetRequiredService<IThemeLibraryService>();
        _themeApplier = services.GetRequiredService<IThemeApplicationService>();
        _topology = services.GetRequiredService<IOutputTopologyService>();
        _logger = services.GetRequiredService<ILogger<ShowSidebarView>>();
        Loaded += ShowSidebarView_Loaded;
        Unloaded += ShowSidebarView_Unloaded;
    }

    private void ShowSidebarView_Loaded(object sender, RoutedEventArgs e)
    {
        IsPaneOpen = true;
        if (ViewModel != null)
        {
            RebuildSourcesNavigation();
            UpdateSourcesNavigationSelection();
            RefreshSourcesSearchSuggestions();
        }
    }

    private void ShowSidebarView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= ViewModel_SourcesPropertyChanged;
            ViewModel.Libraries.CollectionChanged -= CatalogCollections_CollectionChanged;
            ViewModel.Playlists.CollectionChanged -= CatalogCollections_CollectionChanged;
        }
    }

    internal void ScheduleSourcesNavigationRefreshFromHost() => ScheduleSourcesNavigationRefresh();

    internal void FocusSourcesSearchFromHost()
    {
        if (ViewModel == null || !ViewModel.HasPresentationSources || AutoSuggestBox != SourcesSearchBox)
        {
            RestoreKeyboardFocus?.Invoke();
            return;
        }

        SourcesSearchBox.Focus(FocusState.Programmatic);
    }

    private Brush? LookupThemeBrush(string key)
    {
        if (Resources.TryGetValue(key, out var local) && local is Brush lb)
            return lb;
        if (Application.Current.Resources.TryGetValue(key, out var app) && app is Brush ab)
            return ab;
        return null;
    }

    private FontFamily LookupSymbolFont()
    {
        if (Resources.TryGetValue("SymbolThemeFontFamily", out var local) && local is FontFamily lf)
            return lf;
        if (Application.Current.Resources.TryGetValue("SymbolThemeFontFamily", out var app) && app is FontFamily af)
            return af;
        return new FontFamily("Segoe MDL2 Assets");
    }

    private static Brush FlattenBrushForBitmapRender(Brush? brush, Brush fallback)
    {
        if (brush is AcrylicBrush acrylic)
            return new SolidColorBrush(acrylic.FallbackColor);
        if (brush is SolidColorBrush solid)
            return solid;
        return fallback;
    }
}