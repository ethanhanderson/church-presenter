using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
    private bool _mediaTransportScrubbing;
    private bool _mediaTransportTargetSelectionSync;
    private bool _showControlsPanelSelectionSync;
    // Held so we can unregister the exact same delegate from AddHandler on Unloaded.
    private PointerEventHandler? _mediaTransportSeekPointerPressedHandler;

    private void OutputPanelResize_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        if (OutputPanelResizeHandleHoverLine != null)
            OutputPanelResizeHandleHoverLine.Opacity = 0.9;
    }

    private void OutputPanelResize_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_outputSplitterDragging)
            ProtectedCursor = null;
        if (!_outputSplitterDragging && OutputPanelResizeHandleHoverLine != null)
            OutputPanelResizeHandleHoverLine.Opacity = 0;
    }

    private void OutputPanelResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _outputSplitterDragging = true;
        _outputSplitterStartX = e.GetCurrentPoint(LayoutRoot).Position.X;
        _outputSplitterStartWidth = OutputPanelColumn.ActualWidth;
        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void OutputPanelResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_outputSplitterDragging)
            return;

        var currentX = e.GetCurrentPoint(LayoutRoot).Position.X;
        var delta = currentX - _outputSplitterStartX;
        // Handle is on the output column's left edge: drag right → edge moves right → panel narrows.
        ViewModel.OutputPanelWidth = WorkspaceDto.NormalizeStoredShowOutputPanelWidth(
            _outputSplitterStartWidth - delta);
    }

    private void OutputPanelResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var endedResizeGesture = _outputSplitterDragging;
        _outputSplitterDragging = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        if (endedResizeGesture)
            _ = ViewModel.SaveWorkspaceUiStateAsync();
    }

    private void OutputPanelResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        var endedResizeGesture = _outputSplitterDragging;
        _outputSplitterDragging = false;
        if (sender is FrameworkElement el)
        {
            var pt = e.GetCurrentPoint(el).Position;
            var bounds = new Rect(0, 0, el.ActualWidth, el.ActualHeight);
            var inside = bounds.Contains(pt);
            ProtectedCursor = inside
                ? InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast)
                : null;
            if (OutputPanelResizeHandleHoverLine != null)
                OutputPanelResizeHandleHoverLine.Opacity = inside ? 0.9 : 0;
        }

        if (endedResizeGesture)
            _ = ViewModel.SaveWorkspaceUiStateAsync();
    }

    private void OutputPanelResize_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        _outputSplitterDragging = false;
        if (ProtectedCursor != null)
            ProtectedCursor = null;

        if (OutputPanelResizeHandleHoverLine != null)
            OutputPanelResizeHandleHoverLine.Opacity = 0;

        if (sender is UIElement ue)
            ue.ReleasePointerCaptures();

        ViewModel.OutputPanelWidth = WorkspaceDto.ShowOutputPanelDefaultWidthDpi;
        _ = ViewModel.SaveWorkspaceUiStateAsync();
    }

    private void ApplyPanelChromeStates()
    {
        var normalBrush = ResolveThemeBrush("CardStrokeColorDefaultBrush", "ShowChromeDividerBrush");
        if (normalBrush == null)
            return;

        if (OutputPanelChromeBorder != null)
            OutputPanelChromeBorder.BorderBrush = normalBrush;

        if (MediaPanelChromeBorder != null)
            MediaPanelChromeBorder.BorderBrush = normalBrush;
    }

    private Brush? ResolveThemeBrush(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Resources.TryGetValue(key, out var local) && local is Brush lb)
                return lb;
            if (Application.Current?.Resources.TryGetValue(key, out var app) == true && app is Brush ab)
                return ab;
        }

        return null;
    }
    // ── Media panel layout ───────────────────────────────────────────────────

    private void ViewModel_MediaPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShowViewModel.MediaPanelOpen) or nameof(ShowViewModel.MediaPanelHeight))
            UpdateMediaPanelLayout();
        else if (e.PropertyName == nameof(ShowViewModel.OutputPanelWidth))
            SyncOutputPanelColumnWidth();
    }

    private void SyncOutputPanelColumnWidth()
    {
        var w = WorkspaceDto.NormalizeStoredShowOutputPanelWidth(ViewModel.OutputPanelWidth);
        OutputPanelColumn.Width = new GridLength(w);
    }

    private void UpdateMediaPanelLayout()
    {
        var isOpen = ViewModel.MediaPanelOpen;
        var height = Math.Max(120, Math.Min(600, ViewModel.MediaPanelHeight));

        if (isOpen)
            MediaPanelContentRow.Height = new GridLength(height);
        else
            MediaPanelContentRow.Height = new GridLength(0);

        ApplyPanelChromeStates();
    }

    // ── Media panel resize splitter ──────────────────────────────────────────

    private void MediaPanelSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        if (MediaPanelResizeHandleHoverLine != null)
            MediaPanelResizeHandleHoverLine.Opacity = 0.9;
    }

    private void MediaPanelSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_mediaSplitterDragging)
            ProtectedCursor = null;
        if (!_mediaSplitterDragging && MediaPanelResizeHandleHoverLine != null)
            MediaPanelResizeHandleHoverLine.Opacity = 0;
    }

    private void MediaPanelSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _mediaSplitterDragging = true;
        _mediaSplitterStartY = e.GetCurrentPoint(LayoutRoot).Position.Y;
        _mediaSplitterStartHeight = MediaPanelChromeBorder.ActualHeight;
        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void MediaPanelSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_mediaSplitterDragging)
            return;
        var currentY = e.GetCurrentPoint(LayoutRoot).Position.Y;
        var delta = _mediaSplitterStartY - currentY;
        var newHeight = Math.Max(120, Math.Min(600, _mediaSplitterStartHeight + delta));
        MediaPanelContentRow.Height = new GridLength(newHeight);
        ViewModel.MediaPanelHeight = newHeight;
    }

    private void MediaPanelSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _mediaSplitterDragging = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
    }

    private void MediaPanelSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _mediaSplitterDragging = false;
        if (sender is FrameworkElement el)
        {
            var pt = e.GetCurrentPoint(el).Position;
            var bounds = new Rect(0, 0, el.ActualWidth, el.ActualHeight);
            var inside = bounds.Contains(pt);
            ProtectedCursor = inside
                ? InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth)
                : null;
            if (MediaPanelResizeHandleHoverLine != null)
                MediaPanelResizeHandleHoverLine.Opacity = inside ? 0.9 : 0;
        }
    }

    // ── Media panel playlist interactions ───────────────────────────────────

    private async void AddMediaPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "New Media Playlist",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var nameBox = new TextBox { PlaceholderText = "Playlist name", Margin = new Thickness(0, 8, 0, 0) };
        dialog.Content = nameBox;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            await ViewModel.CreateMediaPlaylistAsync(nameBox.Text.Trim());
    }

    private void MediaPlaylistAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MediaPanelSelectedPlaylistId = null;
        UpdateMediaPlaylistButtonVisuals();
    }

    private void MediaPlaylistItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
            ViewModel.MediaPanelSelectedPlaylistId = id;
        UpdateMediaPlaylistButtonVisuals();
    }

    private void UpdateMediaPlaylistButtonVisuals()
    {
        // Visual feedback is a simple approach: mark the selected playlist button.
        // Since we don't have a full selection-aware ItemsControl here, we use accent
        // background on the "All" button when no playlist is selected, and leave individual
        // playlist buttons without explicit SelectedItem styling for now.
        // A future iteration can upgrade to a proper ListView with item selection.
    }

    private async void RenameMediaPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;
        var playlist = ViewModel.MediaPlaylists.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (playlist == null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Rename Playlist",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var nameBox = new TextBox { Text = playlist.Name, Margin = new Thickness(0, 8, 0, 0) };
        dialog.Content = nameBox;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await _mediaLibrary.RenamePlaylistAsync(id, nameBox.Text.Trim());
            await ViewModel.LoadMediaPlaylistsAsync();
        }
    }

    private async void DeleteMediaPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;
        var playlist = ViewModel.MediaPlaylists.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (playlist == null)
            return;

        var dialog = new ContentDialog
        {
            Title = $"Delete \"{playlist.Name}\"?",
            Content = "This will permanently remove the playlist and all its media entries. The original files will not be deleted.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteMediaPlaylistAsync(id);
    }

    // ── Media panel items: add files ─────────────────────────────────────────

    private async void AddMediaFiles_Click(object sender, RoutedEventArgs e)
    {
        var targetPlaylistId = ViewModel.MediaPanelSelectedPlaylistId;
        if (string.IsNullOrWhiteSpace(targetPlaylistId))
        {
            if (ViewModel.MediaPlaylists.Count > 0)
                targetPlaylistId = ViewModel.MediaPlaylists[0].Id;
        }

        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
                                     ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv",
                                     ".mp3", ".wav", ".aac", ".m4a", ".flac" })
            picker.FileTypeFilter.Add(ext);

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
            return;

        foreach (var file in files)
            await ViewModel.AddMediaFileAsync(targetPlaylistId, file.Path);
    }

    // ── Media panel layout mode toggles ─────────────────────────────────────

    private void MediaPanelLayoutGrid_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MediaPanelLayoutMode = "grid";
        UpdateMediaLayoutButtonVisuals();
    }

    private void MediaPanelLayoutList_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MediaPanelLayoutMode = "list";
        UpdateMediaLayoutButtonVisuals();
    }

    private void UpdateMediaLayoutButtonVisuals()
    {
        var resources = Application.Current.Resources;
        var accentBrush = resources.TryGetValue("AccentFillColorDefaultBrush", out var ab) ? ab as Brush : null;
        var defaultBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (ViewModel.MediaPanelIsGridMode)
        {
            MediaLayoutGridButton.Background = accentBrush ?? defaultBrush;
            MediaLayoutListButton.Background = defaultBrush;
        }
        else
        {
            MediaLayoutListButton.Background = accentBrush ?? defaultBrush;
            MediaLayoutGridButton.Background = defaultBrush;
        }
    }

    // ── Media item interactions ──────────────────────────────────────────────

    private void MediaGridItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!TryGetMediaPanelItem(sender, out var item))
            return;

        ViewModel.PreWarmMediaItem(item);
    }

    private void MediaListItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!TryGetMediaPanelItem(sender, out var item))
            return;

        ViewModel.PreWarmMediaItem(item);
    }

    private void MediaGridItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!TryGetMediaPanelItem(sender, out var item))
            return;

        // #region agent log
        ChurchPresenter.Controls.OutputMediaSlotView.DbgLog("H-2", "ShowPage:MediaGridItem_Tapped",
            "click",
            $"{{\"mediaId\":\"{item.Path?.Replace("\\", "\\\\")??"null"}\"}}");
        // #endregion
        ViewModel.PreviewMediaItem(item);
    }

    private void MediaPanelListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MediaPanelItemViewModel vm)
            return;

        // #region agent log
        ChurchPresenter.Controls.OutputMediaSlotView.DbgLog("H-2", "ShowPage:MediaPanelListView_ItemClick",
            "click",
            $"{{\"mediaId\":\"{vm.MediaItem.Path?.Replace("\\", "\\\\")??"null"}\"}}");
        // #endregion
        ViewModel.PreviewMediaItem(vm.MediaItem);
    }

    private void MediaPanelListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not ListView list)
            return;
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space))
            return;
        if (list.SelectedItem is not MediaPanelItemViewModel item)
            return;

        e.Handled = true;
        ViewModel.PreviewMediaItem(item.MediaItem);
    }

    private void MediaPanelListName_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MediaPanelItemViewModel target)
            return;

        e.Handled = true;
        foreach (var vm in ViewModel.MediaPanelItems)
            vm.IsNameEditMode = false;

        target.NameEditDraft = target.Name;
        target.IsNameEditMode = true;
    }

    private void MediaPanelListNameEdit_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not MediaPanelItemViewModel vm)
            return;
        if (!vm.IsNameEditMode)
            return;

        tb.SelectAll();
        _ = tb.Focus(FocusState.Programmatic);
    }

    private async void MediaPanelListNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not MediaPanelItemViewModel vm)
            return;
        if (!vm.IsNameEditMode)
            return;

        await CommitMediaPanelListNameEditAsync(vm, tb.Text);
    }

    private async void MediaPanelListNameEdit_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not MediaPanelItemViewModel vm)
            return;

        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            vm.IsNameEditMode = false;
            vm.NameEditDraft = vm.Name;
            _ = MediaPanelListView?.Focus(FocusState.Programmatic);
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await CommitMediaPanelListNameEditAsync(vm, tb.Text);
            _ = MediaPanelListView?.Focus(FocusState.Programmatic);
        }
    }

    private async Task CommitMediaPanelListNameEditAsync(MediaPanelItemViewModel vm, string? rawText)
    {
        if (!vm.IsNameEditMode)
            return;

        var trimmed = (rawText ?? "").Trim();
        vm.IsNameEditMode = false;

        if (trimmed.Length == 0 || string.Equals(trimmed, vm.Name, StringComparison.Ordinal))
            return;

        var playlistId = ViewModel.FindPlaylistForItem(vm.Id)?.Id;
        if (await ViewModel.RenameMediaItemAsync(playlistId, vm.Id, trimmed))
            ViewModel.StatusMessage = $"Renamed to \"{trimmed}\".";
    }

    private void MediaItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element)
            return;

        if (!PrepareMediaItemContextMenu(element, out var flyout))
            return;

        args.Handled = true;
        if (args.TryGetPosition(element, out var point))
        {
            flyout.ShowAt(element, new FlyoutShowOptions
            {
                Position = point,
                ShowMode = FlyoutShowMode.Standard,
            });
        }
        else
        {
            flyout.ShowAt(element);
        }
    }

    private async void MediaItemEditTransition_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;

        var transitionResult = await PromptForTransitionAsync(
            _contextMenuMediaItem.CueDefaults.Transition,
            "Media file transition");

        if (!transitionResult.Submitted)
            return;

        if (transitionResult.ClearRequested)
        {
            _contextMenuMediaItem.CueDefaults.Transition = null;
        }
        else if (transitionResult.Transition != null)
        {
            _contextMenuMediaItem.CueDefaults.Transition =
                TransitionStorageNormalizer.NormalizeForStorage(transitionResult.Transition);
        }
        else
        {
            _contextMenuMediaItem.CueDefaults.Transition = null;
        }

        await ViewModel.UpdateMediaItemCueDefaultsAsync(
            _contextMenuMediaPlaylistId,
            _contextMenuMediaItem.Id,
            _contextMenuMediaItem.CueDefaults);

        if (ViewModel.MediaPanelItems.FirstOrDefault(i =>
                string.Equals(i.Id, _contextMenuMediaItem.Id, StringComparison.OrdinalIgnoreCase))
            is { } vm)
        {
            vm.NotifyCueDefaultsUiChanged();
        }

        ViewModel.StatusMessage = transitionResult.ClearRequested
            ? "Cleared media transition."
            : "Updated media transition.";
    }

    private async void MediaItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (!TryGetMediaPanelItemViewModel(sender, out var vm) || vm is null)
            return;

        args.Data.SetData(MediaItemDragFormat, vm.Id);
        args.AllowedOperations = DataPackageOperation.Copy;

        var deferral = args.GetDeferral();
        try
        {
            var bitmap = await TryCreateMediaDragCardBitmapAsync(vm).ConfigureAwait(true);
            if (bitmap != null)
            {
                var anchor = args.GetPosition(sender);
                args.DragUI.SetContentFromSoftwareBitmap(bitmap, anchor);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not build media drag preview.");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private bool TryGetMediaPanelItemViewModel(object sender, out MediaPanelItemViewModel? vm)
    {
        vm = null;
        var id = ResolveTaggedMediaItemId(sender as FrameworkElement);
        if (string.IsNullOrWhiteSpace(id))
            return false;

        vm = ViewModel.MediaPanelItems.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return vm != null;
    }

    /// <summary>
    /// Builds a drag bitmap that mirrors the media grid card. Acrylic and other composition brushes
    /// must be flattened to solid colors — <see cref="RenderTargetBitmap"/> often captures them as empty
    /// (see WinUI RenderTargetBitmap remarks on capture limitations).
    /// </summary>
    private async Task<SoftwareBitmap?> TryCreateMediaDragCardBitmapAsync(MediaPanelItemViewModel vm)
    {
        const double cardWidth = 200;
        const double thumbHeight = 112;

        var bg = FlattenBrushForBitmapRender(LookupThemeBrush("ShowMediaCardBackgroundAcrylicBrush"), new SolidColorBrush(Microsoft.UI.Colors.DimGray));
        var edge = FlattenBrushForBitmapRender(LookupThemeBrush("ShowMediaCardBorderAcrylicBrush"), new SolidColorBrush(Microsoft.UI.Colors.Gray));
        var layer = FlattenBrushForBitmapRender(LookupThemeBrush("LayerOnAcrylicFillColorDefaultBrush"), new SolidColorBrush(Microsoft.UI.Colors.DimGray));
        var subtle = FlattenBrushForBitmapRender(LookupThemeBrush("SubtleFillColorSecondaryBrush"), new SolidColorBrush(Microsoft.UI.Colors.Gray));
        var textPrimary = FlattenBrushForBitmapRender(LookupThemeBrush("TextFillColorPrimaryBrush"), new SolidColorBrush(Microsoft.UI.Colors.White));
        var textSecondary = FlattenBrushForBitmapRender(LookupThemeBrush("TextFillColorSecondaryBrush"), new SolidColorBrush(Microsoft.UI.Colors.LightGray));
        FontFamily symbolFont = LookupSymbolFont();

        var rootBorder = new Border
        {
            Width = cardWidth,
            Background = bg,
            BorderBrush = edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(thumbHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        var thumbArea = new Border
        {
            Background = subtle,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
        };
        Grid.SetRow(thumbArea, 0);

        var thumbInner = new Grid();
        if (vm.HasThumbnail && vm.ThumbnailSource != null)
        {
            thumbInner.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = layer,
            });
            thumbInner.Children.Add(new Image
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Source = vm.ThumbnailSource,
                Stretch = Stretch.Uniform,
            });
        }
        else
        {
            thumbInner.Children.Add(new Viewbox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 56,
                MaxHeight = 56,
                Stretch = Stretch.Uniform,
                Child = new FontIcon
                {
                    FontFamily = symbolFont,
                    FontSize = 128,
                    Foreground = textSecondary,
                    Glyph = vm.TypeGlyph,
                },
            });
        }

        thumbArea.Child = thumbInner;

        // Footer matches grid card: name (column 0) + duration/dims + type glyph (column 1).
        var footer = new Grid
        {
            MinHeight = 48,
            Padding = new Thickness(12, 11, 12, 11),
            Background = layer,
            ColumnSpacing = 10,
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(footer, 1);

        var nameBlock = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = textPrimary,
            Text = vm.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameBlock, 0);
        footer.Children.Add(nameBlock);

        var rightStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        Grid.SetColumn(rightStack, 1);
        if (vm.HasFooterRightMetaText)
        {
            rightStack.Children.Add(new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Foreground = textSecondary,
                Text = vm.FooterRightMetaText,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        rightStack.Children.Add(new FontIcon
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = symbolFont,
            FontSize = 12,
            Foreground = textSecondary,
            Glyph = vm.TypeGlyph,
        });
        footer.Children.Add(rightStack);

        grid.Children.Add(thumbArea);
        grid.Children.Add(footer);
        rootBorder.Child = grid;

        MediaDragPreviewHost.Children.Clear();
        MediaDragPreviewHost.Children.Add(rootBorder);
        MediaDragPreviewHost.UpdateLayout();

        rootBorder.Measure(new Size(cardWidth, 500));
        var h = Math.Max(thumbHeight + 48, rootBorder.DesiredSize.Height);
        rootBorder.Arrange(new Rect(0, 0, cardWidth, h));

        var rw = (int)Math.Max(1, Math.Ceiling(rootBorder.ActualWidth));
        var rh = (int)Math.Max(1, Math.Ceiling(rootBorder.ActualHeight));

        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(rootBorder, rw, rh);
        var pixels = await rtb.GetPixelsAsync();

        MediaDragPreviewHost.Children.Clear();

        var sb = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            rtb.PixelWidth,
            rtb.PixelHeight,
            BitmapAlphaMode.Premultiplied);
        sb.CopyFromBuffer(pixels);

        if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sb.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        return sb;
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

    // ── Media item context menu ──────────────────────────────────────────────

    private void MediaItemContextMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout || flyout.Target is not FrameworkElement target)
            return;

        PrepareMediaItemContextMenu(target, out _);
    }

    private MenuFlyout EnsureMediaItemContextMenuFlyout()
    {
        if (_mediaItemContextMenuFlyout != null)
            return _mediaItemContextMenuFlyout;

        if (Resources.TryGetValue("MediaItemContextMenuFlyout", out var obj) && obj is MenuFlyout menu)
            return _mediaItemContextMenuFlyout = menu;

        throw new InvalidOperationException("Missing resource MediaItemContextMenuFlyout.");
    }

    private bool PrepareMediaItemContextMenu(FrameworkElement target, out MenuFlyout flyout)
    {
        flyout = EnsureMediaItemContextMenuFlyout();

        string? itemId = null;
        var el = target;
        while (el != null)
        {
            if (el.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                itemId = tag;
                break;
            }
            el = el.Parent as FrameworkElement;
        }

        if (itemId == null)
            return false;

        var item = ViewModel.MediaPanelItems.FirstOrDefault(i =>
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return false;

        _contextMenuMediaItem = item.MediaItem;
        _contextMenuMediaPlaylistId = ViewModel.FindPlaylistForItem(itemId)?.Id;

        SetMenuItemEnabled(flyout, "Open File Location", CanResolveMediaItemPath(item.MediaItem.Path));
        SyncMediaContextMenuToDefaults(flyout, item.CueDefaults);
        return true;
    }

    private void SlideDeckItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(MediaItemDragFormat))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add media cue to slide";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void SlideDeckItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ShowSlideDeckItem slideItem })
            return;
        if (!e.DataView.Contains(MediaItemDragFormat))
            return;

        var itemId = await e.DataView.GetDataAsync(MediaItemDragFormat) as string;
        if (string.IsNullOrWhiteSpace(itemId))
            return;

        var mediaItem = ViewModel.MediaPanelItems.FirstOrDefault(item =>
            string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase))?.MediaItem;
        if (mediaItem == null)
            return;

        var presentationPath = slideItem.PresentationPath ?? ViewModel.OpenDocument?.SourcePath;
        if (string.IsNullOrWhiteSpace(presentationPath))
            return;

        await ViewModel.AddMediaItemToSlideAsync(presentationPath, slideItem.Slide.Id, mediaItem);
    }

    private bool TryGetMediaPanelItem(object sender, out MediaLibraryItem item)
    {
        item = null!;

        var itemId = ResolveTaggedMediaItemId(sender as FrameworkElement);
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var vm = ViewModel.MediaPanelItems.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (vm == null)
            return false;

        item = vm.MediaItem;
        return true;
    }

    private static string? ResolveTaggedMediaItemId(FrameworkElement? element)
    {
        while (element != null)
        {
            if (element.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
                return tag;

            element = element.Parent as FrameworkElement;
        }

        return null;
    }

    private static void SyncMediaContextMenuToDefaults(MenuFlyout flyout, MediaCueDefaults defaults)
    {
        foreach (var menuItem in EnumerateMenuItems(flyout.Items))
        {
            if (menuItem is ToggleMenuFlyoutItem toggle)
            {
                toggle.IsChecked = toggle.Text switch
                {
                    "Autoplay" => defaults.Autoplay,
                    "Loop" => defaults.Loop,
                    "Muted" => defaults.Muted,
                    _ => toggle.IsChecked,
                };
            }
        }
    }

    private bool CanResolveMediaItemPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(_mediaLibrary.ResolveStoredMediaPath(path));

    private async void MediaCueTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;
        if (sender is not FrameworkElement { Tag: string target })
            return;

        _contextMenuMediaItem.CueDefaults.Target = target;
        await ViewModel.UpdateMediaItemCueDefaultsAsync(
            _contextMenuMediaPlaylistId, _contextMenuMediaItem.Id, _contextMenuMediaItem.CueDefaults);
    }

    private async void MediaCueFit_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;
        if (sender is not FrameworkElement { Tag: string fit })
            return;

        _contextMenuMediaItem.CueDefaults.Fit = fit;
        await ViewModel.UpdateMediaItemCueDefaultsAsync(
            _contextMenuMediaPlaylistId, _contextMenuMediaItem.Id, _contextMenuMediaItem.CueDefaults);
    }

    private async void MediaCueAutoplay_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;
        if (sender is ToggleMenuFlyoutItem { IsChecked: var isChecked })
        {
            _contextMenuMediaItem.CueDefaults.Autoplay = isChecked;
            await ViewModel.UpdateMediaItemCueDefaultsAsync(
                _contextMenuMediaPlaylistId, _contextMenuMediaItem.Id, _contextMenuMediaItem.CueDefaults);
        }
    }

    private async void MediaCueLoop_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;
        if (sender is ToggleMenuFlyoutItem { IsChecked: var isChecked })
        {
            _contextMenuMediaItem.CueDefaults.Loop = isChecked;
            await ViewModel.UpdateMediaItemCueDefaultsAsync(
                _contextMenuMediaPlaylistId, _contextMenuMediaItem.Id, _contextMenuMediaItem.CueDefaults);
        }
    }

    private async void MediaCueMuted_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;
        if (sender is ToggleMenuFlyoutItem { IsChecked: var isChecked })
        {
            _contextMenuMediaItem.CueDefaults.Muted = isChecked;
            await ViewModel.UpdateMediaItemCueDefaultsAsync(
                _contextMenuMediaPlaylistId, _contextMenuMediaItem.Id, _contextMenuMediaItem.CueDefaults);
        }
    }

    private async void MediaItemDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;

        var duplicate = await ViewModel.DuplicateMediaItemAsync(_contextMenuMediaPlaylistId, _contextMenuMediaItem.Id);
        if (duplicate != null)
            ViewModel.StatusMessage = $"Duplicated \"{duplicate.Name}\".";
    }

    private async void MediaItemRename_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;

        var name = await PromptForNameAsync("Rename Media", "Media name", _contextMenuMediaItem.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (await ViewModel.RenameMediaItemAsync(_contextMenuMediaPlaylistId, _contextMenuMediaItem.Id, name))
            ViewModel.StatusMessage = $"Renamed to \"{name}\".";
    }

    private async void MediaItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;

        if (string.IsNullOrWhiteSpace(_contextMenuMediaPlaylistId))
            await _mediaLibrary.RemoveRootItemAsync(_contextMenuMediaItem.Id);
        else
            await _mediaLibrary.RemoveItemAsync(_contextMenuMediaPlaylistId, _contextMenuMediaItem.Id);
        await ViewModel.LoadMediaPlaylistsAsync();
        ViewModel.StatusMessage = $"Deleted \"{_contextMenuMediaItem.Name}\".";
        _contextMenuMediaItem = null;
        _contextMenuMediaPlaylistId = null;
    }

    private void MediaItemOpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuMediaItem == null)
            return;

        try
        {
            var resolved = _mediaLibrary.ResolveStoredMediaPath(_contextMenuMediaItem.Path);
            if (!CanResolveMediaItemPath(_contextMenuMediaItem.Path))
            {
                ViewModel.StatusMessage = "Could not resolve the media file location.";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{resolved}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open media file location: {ex.Message}";
        }
    }

    // ── Media transport controls ─────────────────────────────────────────────

    private void MediaTransportTargetSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_mediaTransportTargetSelectionSync)
            return;

        if (sender.SelectedItem is not FrameworkElement { Tag: string tag } ||
            !TryParseMediaPlaybackTarget(tag, out var target))
        {
            SyncMediaTransportTargetSelector();
            return;
        }

        _mediaTransportScrubbing = false;
        ViewModel.PlaybackCoordinator.SelectedTransportTarget = target;
    }

    private void ShowControlsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_showControlsPanelSelectionSync)
            return;

        if (sender.SelectedItem is not FrameworkElement { Tag: string tag })
        {
            SyncShowControlsPanelSelector();
            return;
        }

        ViewModel.SelectedShowControlsPanel = tag;
    }

    private void SyncShowControlsPanelSelector()
    {
        if (ShowControlsSelectorBar == null)
            return;

        SelectorBarItem targetItem = ViewModel.SelectedShowControlsPanel switch
        {
            "stage" => StageScreensSelectorItem,
            "timers" => TimersSelectorItem,
            "messages" => MessagesSelectorItem,
            "props" => PropsSelectorItem,
            "macros" => MacrosSelectorItem,
            _ => AudioBinSelectorItem,
        };

        if (ReferenceEquals(ShowControlsSelectorBar.SelectedItem, targetItem))
            return;

        _showControlsPanelSelectionSync = true;
        try
        {
            ShowControlsSelectorBar.SelectedItem = targetItem;
        }
        finally
        {
            _showControlsPanelSelectionSync = false;
        }
    }

    private void SyncMediaTransportTargetSelector()
    {
        if (MediaTransportTargetSelectorBar == null)
            return;

        var targetItem = ViewModel.PlaybackCoordinator.SelectedTransportTarget switch
        {
            MediaPlaybackTarget.AudioFiles => AudioFilesTransportSelectorItem,
            MediaPlaybackTarget.Announcements => AnnouncementsTransportSelectorItem,
            _ => MediaFilesTransportSelectorItem,
        };

        if (ReferenceEquals(MediaTransportTargetSelectorBar.SelectedItem, targetItem))
            return;

        _mediaTransportTargetSelectionSync = true;
        try
        {
            MediaTransportTargetSelectorBar.SelectedItem = targetItem;
        }
        finally
        {
            _mediaTransportTargetSelectionSync = false;
        }
    }

    private static bool TryParseMediaPlaybackTarget(string tag, out MediaPlaybackTarget target)
    {
        target = tag switch
        {
            "AudioFiles" => MediaPlaybackTarget.AudioFiles,
            "Announcements" => MediaPlaybackTarget.Announcements,
            "MediaFiles" => MediaPlaybackTarget.MediaFiles,
            _ => MediaPlaybackTarget.MediaFiles,
        };
        return tag is "MediaFiles" or "AudioFiles" or "Announcements";
    }

    // Loaded / Unloaded: use AddHandler(handledEventsToo: true) so PointerPressed fires even
    // when the Slider's internal Thumb control captures and marks the pointer event as handled.
    // The XAML PointerPressed attribute is intentionally absent; this is the sole registration.
    private void MediaTransportSeekSlider_Loaded(object sender, RoutedEventArgs e)
    {
        _mediaTransportSeekPointerPressedHandler = new PointerEventHandler(MediaTransportSeek_PointerPressed);
        MediaTransportSeekSlider.AddHandler(
            UIElement.PointerPressedEvent,
            _mediaTransportSeekPointerPressedHandler,
            handledEventsToo: true);
    }

    private void MediaTransportSeekSlider_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_mediaTransportSeekPointerPressedHandler is null)
            return;

        MediaTransportSeekSlider.RemoveHandler(
            UIElement.PointerPressedEvent,
            _mediaTransportSeekPointerPressedHandler);
        _mediaTransportSeekPointerPressedHandler = null;
    }

    private void MediaTransportSeek_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        _mediaTransportScrubbing = true;
        ViewModel.PlaybackCoordinator.BeginScrub();
        ViewModel.PlaybackCoordinator.UpdateScrubPosition(slider.Value);
    }

    private void MediaTransportSeek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_mediaTransportScrubbing)
            return;

        ViewModel.PlaybackCoordinator.UpdateScrubPosition(e.NewValue);
    }

    private void MediaTransportSeek_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        CommitMediaTransportScrub(sender as Slider);

    private void MediaTransportSeek_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        CommitMediaTransportScrub(sender as Slider);

    private void CommitMediaTransportScrub(Slider? slider)
    {
        if (!_mediaTransportScrubbing)
            return;

        _mediaTransportScrubbing = false;
        if (slider == null)
        {
            ViewModel.PlaybackCoordinator.CancelScrub();
            return;
        }

        ViewModel.PlaybackCoordinator.CommitScrubPosition(slider.Value);
    }

    private void MediaPanelScaleSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.MediaPanelScaleStep = (int)Math.Round(e.NewValue);
    }
}