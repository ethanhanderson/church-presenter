using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.ComponentModel;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using ChurchPresenter.Backend.Output;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
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

namespace ChurchPresenter.Views;

public sealed partial class ShowPage : Page
{
    private const int MaxTopLevelThemeMenuItems = 8;

    private readonly IContentDirectoryService _content;
    private readonly ISettingsService _settings;
    private readonly ILocalCollectionService _localCollection;
    private readonly ICollectionPackageService _collectionPackages;
    private readonly IPresentationProjectService _projects;
    private readonly IBundleAssetCacheService _assetCache;
    private readonly IPresentationItemActionService _presentationActions;
    private readonly IOutputTopologyService _topology;
    private readonly ISidebarPresentationClipboardService _presentationClipboard;
    private readonly IThemeLibraryService _themeLibrary;
    private readonly IThemeApplicationService _themeApplier;
    private readonly IQuickEditTextLayerService _quickEditTextLayers;
    private readonly ISlideItemActionService _slideItemActions;
    private readonly ISlideClipboardService _slideClipboard;
    private readonly ISlideTextStyleClipboardService _textStyleClipboard;
    private readonly IShowTimerService _timers;
    private readonly IMediaLibraryService _mediaLibrary;
    private readonly IOutputRoutingService _outputRouting;
    private readonly ILogger<ShowPage> _logger;
    private Flyout? _activeQuickEditFlyout;
    private bool _outputSplitterDragging;
    private double _outputSplitterStartX;
    private double _outputSplitterStartWidth;
    private RectangleClip? _outputPreviewAndClearBarsClip;
    private RectangleClip? _mediaTransportCardClip;

    // ── Page-level reusable flyouts for transition picker ─────────────────────
    private Flyout? _transitionFlyout;
    private TransitionPickerDialogContent? _transitionFlyoutContent;
    private Button? _transitionFlyoutRemoveButton;
    private Flyout? _clearGroupsFlyout;
    private ClearGroupsFlyoutContent? _clearGroupsFlyoutContent;

    // ── Auto-advance flyout target (browse-stack sections or single-deck) ────
    private ShowPresentationDeckSection? _autoAdvanceFlyoutSection;

    // Stored when a slide card context menu opens so that menu-item Click handlers can
    // resolve the owning ShowSlideDeckItem without relying on DataContext (which ItemsRepeater
    // does not set on generated elements).
    private ShowSlideDeckItem? _contextMenuSlideItem;
    private FrameworkElement? _contextMenuSlideTarget;

    // ── Media panel ──────────────────────────────────────────────────────────
    private const string MediaItemDragFormat = "application/x-churchpresenter-media-item-id";
    private const string SourceNavigationDragFormat = "application/x-churchpresenter-source-item";
    private const string PresentationNavigationDragFormat = "application/x-churchpresenter-presentation-ref";
    private const string SidebarDropBeforeIndicatorName = "SidebarDropBeforeIndicator";
    private const string SidebarDropAfterIndicatorName = "SidebarDropAfterIndicator";
    private MediaLibraryItem? _contextMenuMediaItem;
    private string? _contextMenuMediaPlaylistId;
    /// <summary>Single flyout for media drawer items; must not be assigned as <see cref="UIElement.ContextFlyout"/> on multiple elements.</summary>
    private MenuFlyout? _mediaItemContextMenuFlyout;
    private double _mediaSplitterStartY;
    private double _mediaSplitterStartHeight;
    private bool _mediaSplitterDragging;
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

    public ShowViewModel ViewModel { get; }

    /// <summary>WinUI <see cref="Frame"/> activation entry; resolves dependencies from the app container.</summary>
    public ShowPage()
        : this(App.Services)
    {
    }

    private ShowPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // x:Bind paths use ViewModel.* — assign before InitializeComponent so compiled bindings do not see null.
        ViewModel = services.GetRequiredService<ShowViewModel>();
        _content = services.GetRequiredService<IContentDirectoryService>();
        _settings = services.GetRequiredService<ISettingsService>();
        InitializeComponent();
        SyncMediaTransportTargetSelector();
        SyncShowControlsPanelSelector();
        _localCollection = services.GetRequiredService<ILocalCollectionService>();
        _collectionPackages = services.GetRequiredService<ICollectionPackageService>();
        _projects = services.GetRequiredService<IPresentationProjectService>();
        _assetCache = services.GetRequiredService<IBundleAssetCacheService>();
        _presentationActions = services.GetRequiredService<IPresentationItemActionService>();
        _topology = services.GetRequiredService<IOutputTopologyService>();
        _presentationClipboard = services.GetRequiredService<ISidebarPresentationClipboardService>();
        _themeLibrary = services.GetRequiredService<IThemeLibraryService>();
        _themeApplier = services.GetRequiredService<IThemeApplicationService>();
        _quickEditTextLayers = services.GetRequiredService<IQuickEditTextLayerService>();
        _slideItemActions = services.GetRequiredService<ISlideItemActionService>();
        _slideClipboard = services.GetRequiredService<ISlideClipboardService>();
        _textStyleClipboard = services.GetRequiredService<ISlideTextStyleClipboardService>();
        _timers = services.GetRequiredService<IShowTimerService>();
        _mediaLibrary = services.GetRequiredService<IMediaLibraryService>();
        _outputRouting = services.GetRequiredService<IOutputRoutingService>();
        _logger = services.GetRequiredService<ILogger<ShowPage>>();
        DataContext = ViewModel;
        UpdateSourcesSearchBoxHost();
        RebuildSourcesNavigation();
        UpdateSourcesNavigationSelection();
        RefreshSourcesSearchSuggestions();
        Loaded += ShowPage_Loaded;
        Unloaded += ShowPage_Unloaded;
        PreviewKeyDown += ShowPage_KeyDown;
        PreviewKeyUp += ShowPage_KeyUp;
        ActualThemeChanged += ShowPage_ActualThemeChanged;
        ViewModel.BrowseStackScrollToSelectionRequested += OnBrowseStackScrollToSelectionRequested;
        ViewModel.PropertyChanged += ViewModel_SourcesPropertyChanged;
        ViewModel.Libraries.CollectionChanged += CatalogCollections_CollectionChanged;
        ViewModel.Playlists.CollectionChanged += CatalogCollections_CollectionChanged;
    }

    private void ShowPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= ShowPage_Unloaded;
        PreviewKeyUp -= ShowPage_KeyUp;
        ViewModel.BrowseStackScrollToSelectionRequested -= OnBrowseStackScrollToSelectionRequested;
        ViewModel.PropertyChanged -= ViewModel_SourcesPropertyChanged;
        ViewModel.PropertyChanged -= ViewModel_MediaPanelPropertyChanged;
        ViewModel.Libraries.CollectionChanged -= CatalogCollections_CollectionChanged;
        ViewModel.Playlists.CollectionChanged -= CatalogCollections_CollectionChanged;
        ViewModel.StopSlideSeek();
    }

    private void OutputPreviewAndClearBarsContainer_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateOutputPreviewAndClearBarsClip();
    }

    private void OutputPreviewAndClearBarsContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOutputPreviewAndClearBarsClip();
    }

    private void OutputPreviewAndClearBarsContainer_Unloaded(object sender, RoutedEventArgs e)
    {
        ElementCompositionPreview.GetElementVisual(OutputPreviewAndClearBarsContainer).Clip = null;
        _outputPreviewAndClearBarsClip?.Dispose();
        _outputPreviewAndClearBarsClip = null;
    }

    private void UpdateOutputPreviewAndClearBarsClip()
    {
        var width = (float)OutputPreviewAndClearBarsContainer.ActualWidth;
        var height = (float)OutputPreviewAndClearBarsContainer.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(OutputPreviewAndClearBarsContainer);
        var clip = _outputPreviewAndClearBarsClip ??= visual.Compositor.CreateRectangleClip();
        clip.Left = 0;
        clip.Top = 0;
        clip.Right = width;
        clip.Bottom = height;

        var radius = new Vector2(8, 8);
        clip.TopLeftRadius = radius;
        clip.TopRightRadius = radius;
        clip.BottomLeftRadius = radius;
        clip.BottomRightRadius = radius;
        visual.Clip = clip;
    }

    private void MediaTransportCardContainer_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMediaTransportCardClip();
    }

    private void MediaTransportCardContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMediaTransportCardClip();
    }

    private void MediaTransportCardContainer_Unloaded(object sender, RoutedEventArgs e)
    {
        ElementCompositionPreview.GetElementVisual(MediaTransportCardContainer).Clip = null;
        _mediaTransportCardClip?.Dispose();
        _mediaTransportCardClip = null;
    }

    private void UpdateMediaTransportCardClip()
    {
        var width = (float)MediaTransportCardContainer.ActualWidth;
        var height = (float)MediaTransportCardContainer.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(MediaTransportCardContainer);
        var clip = _mediaTransportCardClip ??= visual.Compositor.CreateRectangleClip();
        clip.Left = 0;
        clip.Top = 0;
        clip.Right = width;
        clip.Bottom = height;

        var radius = new Vector2(8, 8);
        clip.TopLeftRadius = radius;
        clip.TopRightRadius = radius;
        clip.BottomLeftRadius = radius;
        clip.BottomRightRadius = radius;
        visual.Clip = clip;
    }

    private void OnBrowseStackScrollToSelectionRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => ScrollBrowseStackToSelectedPresentation(0));
    }

    private void ScrollBrowseStackToSelectedPresentation(int attempt)
    {
        var path = ViewModel.SelectedPresentationPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        ShowPresentationDeckSection? targetSection = null;
        foreach (var s in ViewModel.BrowseStackSections)
        {
            if (ViewModel.PresentationPathsMatch(s.PresentationPath, path))
            {
                targetSection = s;
                break;
            }
        }

        if (targetSection == null)
            return;

        BrowseStackItemsControl.UpdateLayout();
        var container = BrowseStackItemsControl.ContainerFromItem(targetSection);
        if (container is UIElement uie)
        {
            uie.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.12f });
            return;
        }

        if (attempt < 10)
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ScrollBrowseStackToSelectedPresentation(attempt + 1));
        }
    }

    private void ShowPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ViewModel.NotifySlideDeckThemeChromeChanged();
        ApplyPanelChromeStates();
    }

    private async void ShowPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
            await ViewModel.LoadShowControlsAsync();
            RestoreKeyboardFocus();
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Show startup failed: {ex.Message}";
        }

        ViewModel.PropertyChanged += ViewModel_MediaPanelPropertyChanged;
        SyncOutputPanelColumnWidth();
        UpdateMediaPanelLayout();
        ScheduleSourcesNavigationRefresh();
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
        if (ViewModel.HasPresentationSources)
        {
            if (ShowNavigationView.AutoSuggestBox != SourcesSearchBox)
                ShowNavigationView.AutoSuggestBox = SourcesSearchBox;

            SourcesSearchBox.Visibility = Visibility.Visible;
            return;
        }

        if (!string.IsNullOrEmpty(_sourcesSearchText))
            _sourcesSearchText = string.Empty;

        if (!string.IsNullOrEmpty(SourcesSearchBox.Text))
            SourcesSearchBox.Text = string.Empty;

        SourcesSearchBox.ItemsSource = null;

        if (ShowNavigationView.AutoSuggestBox == SourcesSearchBox)
            ShowNavigationView.AutoSuggestBox = null;
    }

    private void RebuildSourcesNavigation()
    {
        _suppressSourcesNavigationSelectionChanged = true;
        try
        {
            ShowNavigationView.MenuItems.Clear();
            var hasSearchFilter = !string.IsNullOrWhiteSpace(_sourcesSearchText);

            UpdateSourcesSearchBoxHost();
            UpdateContentActionButton();

            ShowNavigationView.MenuItems.Add(CreateSourcesNavigationHeader("Libraries"));

            if (ViewModel.Libraries.Count > 0)
            {
                foreach (var library in ViewModel.Libraries)
                {
                    var matchingPresentations = GetMatchingPresentations(library.Presentations, _sourcesSearchText);
                    if (hasSearchFilter && matchingPresentations.Count == 0)
                        continue;

                    ShowNavigationView.MenuItems.Add(CreateLibraryNavigationItem(library, matchingPresentations));
                }
            }
            else
            {
                ShowNavigationView.MenuItems.Add(CreateEmptySourceNavigationItem(
                    "\uE8B7",
                    "No libraries yet"));
            }

            ShowNavigationView.MenuItems.Add(CreateSourcesNavigationDivider());

            ShowNavigationView.MenuItems.Add(CreateSourcesNavigationHeader("Playlists"));

            if (ViewModel.Playlists.Count > 0)
            {
                foreach (var playlist in ViewModel.Playlists)
                {
                    var matchingPresentations = GetMatchingPresentations(playlist.Items, _sourcesSearchText);
                    if (hasSearchFilter && matchingPresentations.Count == 0)
                        continue;

                    ShowNavigationView.MenuItems.Add(CreatePlaylistNavigationItem(playlist, matchingPresentations));
                }
            }
            else
            {
                ShowNavigationView.MenuItems.Add(CreateEmptySourceNavigationItem(
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
        if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return;

        var position = target.TransformToVisual(LayoutRoot).TransformPoint(new Point(0, 0));
        var inset = 2d;
        var overlay = _activeSidebarIntoDropOverlay ??= CreateSidebarIntoDropOverlay();
        overlay.Width = Math.Max(0, target.ActualWidth - inset * 2);
        overlay.Height = Math.Max(0, target.ActualHeight - inset * 2);
        overlay.Margin = new Thickness(position.X + inset, position.Y + inset, 0, 0);
        overlay.Visibility = Visibility.Visible;

        if (!LayoutRoot.Children.Contains(overlay))
            LayoutRoot.Children.Add(overlay);
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
        if (_activeSidebarIntoDropOverlay == null)
            return;

        LayoutRoot.Children.Remove(_activeSidebarIntoDropOverlay);
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

        PresentationDragPreviewHost.Children.Clear();
        PresentationDragPreviewHost.Children.Add(rootBorder);
        PresentationDragPreviewHost.UpdateLayout();

        rootBorder.Measure(new Size(cardWidth, 160));
        var height = Math.Max(52, rootBorder.DesiredSize.Height);
        rootBorder.Arrange(new Rect(0, 0, cardWidth, height));

        var width = (int)Math.Max(1, Math.Ceiling(rootBorder.ActualWidth));
        var renderHeight = (int)Math.Max(1, Math.Ceiling(rootBorder.ActualHeight));
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(rootBorder, width, renderHeight);
        var pixels = await rtb.GetPixelsAsync();

        PresentationDragPreviewHost.Children.Clear();

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

        var exportBundleItem = CreatePresentationMenuItem("Export as Bundle...", "\uE159", null, VirtualKey.None, _ => ExportPresentationBundleAsync(item));
        exportBundleItem.IsEnabled = CanResolvePresentationPath(item.Presentation.Path);
        flyout.Items.Add(exportBundleItem);

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
        RestoreKeyboardFocus();
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
                RestoreKeyboardFocus();
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
            target = FindNavigationItem(ShowNavigationView.MenuItems, tag => SourceNavigationTagsEqual(tag, _selectedSourceNavigationTag));
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
                        ShowNavigationView.MenuItems,
                        tag => tag.Kind == ShowSourceNavigationKind.Presentation
                            && string.Equals(tag.LibraryId, ViewModel.SelectedLibraryId, StringComparison.OrdinalIgnoreCase)
                            && ViewModel.PresentationPathsMatch(tag.PresentationPath, selectedPresentationPath));
                }
                else if (!string.IsNullOrWhiteSpace(ViewModel.SelectedPlaylistId))
                {
                    target = FindNavigationItem(
                        ShowNavigationView.MenuItems,
                        tag => tag.Kind == ShowSourceNavigationKind.Presentation
                            && string.Equals(tag.PlaylistId, ViewModel.SelectedPlaylistId, StringComparison.OrdinalIgnoreCase)
                            && ViewModel.PresentationPathsMatch(tag.PresentationPath, selectedPresentationPath));
                }
            }

            target ??= !string.IsNullOrWhiteSpace(ViewModel.SelectedPlaylistId)
                ? FindNavigationItem(
                    ShowNavigationView.MenuItems,
                    tag => tag.Kind == ShowSourceNavigationKind.Playlist
                        && string.Equals(tag.PlaylistId, ViewModel.SelectedPlaylistId, StringComparison.OrdinalIgnoreCase))
                : null;

            target ??= !string.IsNullOrWhiteSpace(ViewModel.SelectedLibraryId)
                ? FindNavigationItem(
                    ShowNavigationView.MenuItems,
                    tag => tag.Kind == ShowSourceNavigationKind.Library
                        && string.Equals(tag.LibraryId, ViewModel.SelectedLibraryId, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        _suppressSourcesNavigationSelectionChanged = true;
        try
        {
            ShowNavigationView.SelectedItem = target;
            if (target != null)
                ExpandAncestorsForTarget(ShowNavigationView.MenuItems, target);
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

        RestoreKeyboardFocus();
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
        RestoreKeyboardFocus();
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
        RestoreKeyboardFocus();
    }

    private async void ShowPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || ShouldIgnoreShowPageKey(e.OriginalSource as DependencyObject))
            return;

        if (await TryHandleSelectedSlideShortcutAsync(e.Key))
        {
            e.Handled = true;
            BringSelectedSlideDeckItemIntoView();
            RestoreKeyboardFocus();
            return;
        }

        if (IsSlideSeekKey(e.Key))
        {
            // Claim the event synchronously before any await so the tunneling event
            // does NOT propagate to child elements (e.g. the slide deck ListView) that
            // would otherwise consume the same arrow key and fight the seek loop.
            e.Handled = true;
            await ViewModel.StartSlideSeekAsync(e.Key);
            BringSelectedSlideDeckItemIntoView();
            RestoreKeyboardFocus();
            return;
        }

        var handled = await ViewModel.HandleKeyAsync(e.Key);
        if (!handled)
            return;

        e.Handled = true;
        BringSelectedSlideDeckItemIntoView();
        RestoreKeyboardFocus();
    }

    private void ShowPage_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || ShouldIgnoreShowPageKey(e.OriginalSource as DependencyObject))
            return;
        if (!IsSlideSeekKey(e.Key))
            return;

        ViewModel.StopSlideSeek();
        e.Handled = true;
        RestoreKeyboardFocus();
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
        SetMenuItemEnabled(sender, "Export as Bundle...", canResolvePath);
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
        SetMenuItemEnabled(sender, "Export as Bundle...", canResolvePath);
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

    private bool TryGetSlideDeckItem(object sender, out ShowSlideDeckItem item)
    {
        item = null!;

        // MenuFlyout Opening event: resolve from the target button's Tag.
        if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement flyoutTarget
            && flyoutTarget.Tag is ShowSlideDeckItem flyoutItem)
        {
            item = flyoutItem;
            return true;
        }

        // Direct FrameworkElement (button Click): Tag holds the data item set via x:Bind.
        if (sender is FrameworkElement element && element.Tag is ShowSlideDeckItem elementItem)
        {
            item = elementItem;
            return true;
        }

        // MenuFlyoutItem Click — sender is the menu item itself, which has no direct reference
        // to the card data. Use the item stored when the context menu opened.
        if (_contextMenuSlideItem != null)
        {
            item = _contextMenuSlideItem;
            return true;
        }

        return false;
    }

    private string ResolveSlideDeckPresentationPath(ShowSlideDeckItem item)
    {
        return string.IsNullOrWhiteSpace(item.PresentationPath)
            ? ViewModel.SelectedPresentationPath ?? item.ThumbnailProject?.SourcePath ?? string.Empty
            : item.PresentationPath;
    }

    private bool TryGetSlideDeckTarget(object sender, out FrameworkElement target, out ShowSlideDeckItem item)
    {
        item = null!;
        target = null!;

        // MenuFlyout Opening event: resolve from the target button's Tag.
        if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement flyoutTarget
            && flyoutTarget.Tag is ShowSlideDeckItem flyoutItem)
        {
            target = flyoutTarget;
            item = flyoutItem;
            return true;
        }

        // Direct FrameworkElement (button Click): Tag holds the data item set via x:Bind.
        if (sender is FrameworkElement element && element.Tag is ShowSlideDeckItem elementItem)
        {
            target = element;
            item = elementItem;
            return true;
        }

        // MenuFlyoutItem Click — fall back to the item and target stored when the context menu opened.
        if (_contextMenuSlideItem != null && _contextMenuSlideTarget != null)
        {
            target = _contextMenuSlideTarget;
            item = _contextMenuSlideItem;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildQuickEditUpdates(IReadOnlyDictionary<string, TextBox> textBoxes)
    {
        return textBoxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.OrdinalIgnoreCase);
    }

    private async Task ShowQuickEditFlyoutAsync(FrameworkElement target, ShowSlideDeckItem item)
    {
        var project = _projects.Open(ResolveSlideDeckPresentationPath(item));
        var projectSlide = project.Slides.FirstOrDefault(slide =>
            string.Equals(slide.Id, item.Slide.Id, StringComparison.OrdinalIgnoreCase)) ?? item.Slide;
        var draft = await _quickEditTextLayers.BuildDraftAsync(project, projectSlide);
        var textLayers = draft.TextLayers.ToList();

        _activeQuickEditFlyout?.Hide();
        item.ResetTransientThumbnailPreview();

        var editorStack = new StackPanel { Spacing = 12 };
        var textBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);

        void RefreshPreview()
        {
            item.SetTransientThumbnailPreview(textLayers, BuildQuickEditUpdates(textBoxes));
        }

        for (var index = 0; index < textLayers.Count; index++)
        {
            var layer = textLayers[index];
            var box = new TextBox
            {
                AcceptsReturn = true,
                MinHeight = 120,
                TextWrapping = TextWrapping.Wrap,
                Text = layer.Content,
            };
            box.TextChanged += (_, _) => RefreshPreview();
            editorStack.Children.Add(box);
            textBoxes[layer.Id] = box;
        }

        RefreshPreview();

        var applyButton = new Button
        {
            Content = "Apply",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                cancelButton,
                applyButton,
            },
        };

        var flyout = new Flyout
        {
            Placement = ResolveQuickEditFlyoutPlacement(target),
            Content = new Border
            {
                Width = 420,
                Padding = new Thickness(8),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new ScrollViewer
                        {
                            MaxHeight = 420,
                            Content = editorStack,
                        },
                        buttonRow,
                    },
                },
            },
        };

        var committed = false;
        cancelButton.Click += (_, _) => flyout.Hide();
        applyButton.Click += async (_, _) =>
        {
            committed = true;
            flyout.Hide();

            var result = await _slideItemActions.UpdateSlideAsync(
                ResolveSlideDeckPresentationPath(item),
                item.Slide.Id,
                (slide, mutationProject) =>
                {
                    _quickEditTextLayers.ApplyEdits(
                        mutationProject,
                        slide,
                        draft,
                        BuildQuickEditUpdates(textBoxes));
                });

            await ReloadSlideMutationAsync(result);
            ViewModel.StatusMessage = "Updated slide text.";
        };

        flyout.Closed += (_, _) =>
        {
            if (!committed)
                item.ResetTransientThumbnailPreview();

            if (ReferenceEquals(_activeQuickEditFlyout, flyout))
                _activeQuickEditFlyout = null;
        };

        _activeQuickEditFlyout = flyout;
        flyout.ShowAt(target);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (textBoxes.Values.FirstOrDefault() is { } firstEditor)
                firstEditor.Focus(FocusState.Programmatic);
        });
    }

    private FlyoutPlacementMode ResolveQuickEditFlyoutPlacement(FrameworkElement target)
    {
        const double estimatedFlyoutHeight = 500;
        try
        {
            var targetTop = target.TransformToVisual(this).TransformPoint(new Point(0, 0)).Y;
            var targetBottom = targetTop + target.ActualHeight;
            var spaceBelow = ActualHeight - targetBottom;
            var spaceAbove = targetTop;

            return spaceBelow < estimatedFlyoutHeight && spaceAbove > spaceBelow
                ? FlyoutPlacementMode.TopEdgeAlignedLeft
                : FlyoutPlacementMode.BottomEdgeAlignedLeft;
        }
        catch (InvalidOperationException)
        {
            return FlyoutPlacementMode.BottomEdgeAlignedLeft;
        }
    }

    private async Task<ThemeSelection?> PromptForThemeSelectionAsync(PresentationProject project)
    {
        var choices = new List<ThemeChoice>();
        var globalThemes = await _themeLibrary.LoadAsync();
        choices.AddRange(globalThemes.Select(theme => new ThemeChoice(theme, theme.Name)));
        choices.AddRange(project.EmbeddedThemes
            .Where(entry => entry.Template != null)
            .Select(entry => new ThemeChoice(entry.Template!, $"{entry.Template!.Name} (Embedded)")));

        if (choices.Count == 0)
        {
            ViewModel.StatusMessage = "No themes are available for this slide.";
            return null;
        }

        var themeCombo = new ComboBox
        {
            Header = "Theme",
            DisplayMemberPath = nameof(ThemeChoice.Name),
            ItemsSource = choices,
            SelectedItem = choices[0],
        };
        var slideCombo = new ComboBox
        {
            Header = "Theme Slide",
            DisplayMemberPath = nameof(ThemeSlideChoice.Name),
        };

        void RefreshThemeSlides()
        {
            var selectedTheme = themeCombo.SelectedItem as ThemeChoice;
            var slides = selectedTheme?.Theme.Slides.Select((slide, index) =>
                    new ThemeSlideChoice(slide, slide.Name ?? $"Slide {index + 1}"))
                .ToList() ?? new List<ThemeSlideChoice>();
            slideCombo.ItemsSource = slides;
            slideCombo.SelectedItem = slides.FirstOrDefault();
        }

        themeCombo.SelectionChanged += (_, _) => RefreshThemeSlides();
        RefreshThemeSlides();

        var dialog = new ContentDialog
        {
            Title = "Apply Theme",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    themeCombo,
                    slideCombo,
                },
            },
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || themeCombo.SelectedItem is not ThemeChoice themeChoice
            || slideCombo.SelectedItem is not ThemeSlideChoice slideChoice)
        {
            return null;
        }

        return new ThemeSelection(themeChoice.Theme, slideChoice.Slide);
    }

    private async Task<TransitionDialogResult> PromptForTransitionAsync(
        SlideTransition? currentTransition,
        string? dialogTitle = null)
    {
        var picker = new TransitionPickerDialogContent();
        // Initialize without a viewModel — favorites/recents are show-page concerns, not per-slide.
        picker.Initialize(null, currentTransition);

        var dialog = new ContentDialog
        {
            Title = dialogTitle ?? "Slide Transition Override",
            PrimaryButtonText = "Apply",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Content = picker,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return new TransitionDialogResult(false, false, null);
        if (result == ContentDialogResult.Secondary)
            return new TransitionDialogResult(true, true, null);

        return new TransitionDialogResult(true, false, picker.BuildTransition());
    }

    private async Task<SelectionDialogResult> PromptForHotKeyAsync(string? currentHotKey)
    {
        var input = new TextBox
        {
            Header = "Hot Key",
            MaxLength = 1,
            PlaceholderText = "A-Z",
            Text = NormalizeHotKeyInput(currentHotKey) ?? string.Empty,
        };
        var helpText = new TextBlock
        {
            Text = "Enter one letter. Pressing that key on the Show page will take this slide live.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var dialog = new ContentDialog
        {
            Title = "Slide Hot Key",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    input,
                    helpText,
                    errorText,
                },
            },
            XamlRoot = XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (NormalizeHotKeyInput(input.Text) != null)
                return;

            errorText.Text = "Enter a single letter from A to Z.";
            errorText.Visibility = Visibility.Visible;
            args.Cancel = true;
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => new SelectionDialogResult(true, false, NormalizeHotKeyInput(input.Text)),
            ContentDialogResult.Secondary => new SelectionDialogResult(true, true, null),
            _ => new SelectionDialogResult(false, false, null),
        };
    }

    private static string? NormalizeHotKeyInput(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length != 1)
            return null;

        var c = char.ToUpperInvariant(trimmed[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : null;
    }

    private async Task<SelectionDialogResult> PromptForTimerAssignmentAsync(string? currentTimerId)
    {
        var timers = (await _timers.GetTimersAsync()).ToList();
        var timerChoices = timers.Select(timer => new TimerChoice(timer)).ToList();
        var timerCombo = new ComboBox
        {
            Header = "Existing Timer",
            DisplayMemberPath = nameof(TimerChoice.Name),
            ItemsSource = timerChoices,
            SelectedItem = timerChoices.FirstOrDefault(choice => string.Equals(choice.Timer.Id, currentTimerId, StringComparison.OrdinalIgnoreCase)),
        };
        var newNameBox = new TextBox
        {
            Header = "Create New Timer",
            PlaceholderText = "Timer name",
        };
        var durationBox = new NumberBox
        {
            Header = "Duration (seconds)",
            Minimum = 0,
            Maximum = 7200,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = 60,
        };

        var dialog = new ContentDialog
        {
            Title = "Go to Next Timer",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    timerCombo,
                    newNameBox,
                    durationBox,
                },
            },
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return new SelectionDialogResult(false, false, null);
        if (result == ContentDialogResult.Secondary)
            return new SelectionDialogResult(true, true, null);

        var newName = NormalizeDialogValue(newNameBox.Text);
        if (!string.IsNullOrWhiteSpace(newName))
        {
            var timer = await _timers.SaveTimerAsync(new ShowTimerDefinition
            {
                Name = newName,
                DurationSeconds = (int)Math.Round(durationBox.Value),
            });
            return new SelectionDialogResult(true, false, timer.Id);
        }

        return new SelectionDialogResult(true, false, (timerCombo.SelectedItem as TimerChoice)?.Timer.Id);
    }

    private async Task<List<SlideActionDefinition>?> PromptForSlideActionsAsync(IReadOnlyList<SlideActionDefinition> currentActions)
    {
        var nextActions = currentActions
            .Select(action => PresentationModelUtilities.DeepClone(action) ?? new SlideActionDefinition())
            .ToList();
        var existingChoices = nextActions.Select(action => new ActionChoice(action)).ToList();
        var removeCombo = new ComboBox
        {
            Header = "Remove Existing Action",
            DisplayMemberPath = nameof(ActionChoice.Name),
            ItemsSource = existingChoices,
        };
        var addCombo = new ComboBox
        {
            Header = "Add Action",
            ItemsSource = new List<string> { "", "clearPresentation", "clearMedia", "blackoutOn", "blackoutOff", "clearAll" },
            SelectedIndex = 0,
        };
        var summary = new TextBlock
        {
            Text = nextActions.Count == 0
                ? "No actions are currently assigned to this slide."
                : string.Join(Environment.NewLine, nextActions.Select(action => $"- {GetSlideActionLabel(action)}")),
            TextWrapping = TextWrapping.Wrap,
        };

        var dialog = new ContentDialog
        {
            Title = "Slide Actions",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    summary,
                    removeCombo,
                    addCombo,
                },
            },
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        if (removeCombo.SelectedItem is ActionChoice removeChoice)
            nextActions.RemoveAll(action => string.Equals(action.Id, removeChoice.Action.Id, StringComparison.OrdinalIgnoreCase));

        var addType = NormalizeDialogValue(addCombo.SelectedItem as string);
        if (!string.IsNullOrWhiteSpace(addType))
            nextActions.Add(CreateSlideAction(addType));

        return nextActions;
    }

    private async Task<MediaConfigurationResult?> PromptForMediaConfigurationAsync(PresentationProject project, PresentationSlide slide)
    {
        var mediaChoices = project.Manifest.Media.Select(entry => new MediaChoice(entry)).ToList();
        var nonAudioChoices = mediaChoices.Where(choice => !string.Equals(choice.Entry.Type, "audio", StringComparison.OrdinalIgnoreCase)).ToList();
        var currentBackground = slide.Background;
        var currentBackgroundMediaId = currentBackground switch
        {
            ImageSlideBackground image => image.MediaId,
            VideoSlideBackground video => video.MediaId,
            _ => null,
        };

        var backgroundMode = new ComboBox
        {
            Header = "Slide Background",
            ItemsSource = new List<string> { "Keep current", "Solid Black", "Transparent", "Image Background", "Video Background" },
            SelectedItem = currentBackground switch
            {
                TransparentSlideBackground => "Transparent",
                ImageSlideBackground => "Image Background",
                VideoSlideBackground => "Video Background",
                _ => "Keep current",
            },
        };
        var backgroundMedia = new ComboBox
        {
            Header = "Background Media",
            DisplayMemberPath = nameof(MediaChoice.Name),
            ItemsSource = nonAudioChoices,
            SelectedItem = nonAudioChoices.FirstOrDefault(choice => string.Equals(choice.Entry.Id, currentBackgroundMediaId, StringComparison.OrdinalIgnoreCase)),
        };
        var backgroundFit = new ComboBox
        {
            Header = "Background Fit",
            ItemsSource = new List<string> { "cover", "contain", "fill", "none" },
            SelectedItem = currentBackground switch
            {
                ImageSlideBackground image => image.Fit,
                VideoSlideBackground video => video.Fit,
                _ => "cover",
            },
        };
        var backgroundOpacity = new NumberBox
        {
            Header = "Background Opacity",
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.05,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = currentBackground switch
            {
                ImageSlideBackground image => image.Opacity,
                VideoSlideBackground video => video.Opacity,
                _ => 1,
            },
        };
        var backgroundLoop = new ToggleSwitch
        {
            Header = "Loop Background Video",
            IsOn = currentBackground is VideoSlideBackground currentVideo && currentVideo.Loop,
        };
        var backgroundMuted = new ToggleSwitch
        {
            Header = "Mute Background Video",
            IsOn = currentBackground is not VideoSlideBackground currentVideoMuted || currentVideoMuted.Muted,
        };

        var existingCueChoices = GetMediaCues(slide)
            .Select(cue => new CueChoice(cue, ResolveCueDisplayName(cue, mediaChoices)))
            .ToList();
        var removeCueCombo = new ComboBox
        {
            Header = "Remove Existing Cue",
            DisplayMemberPath = nameof(CueChoice.Name),
            ItemsSource = existingCueChoices,
        };
        var newCueMedia = new ComboBox
        {
            Header = "Add Cue Media",
            DisplayMemberPath = nameof(MediaChoice.Name),
            ItemsSource = mediaChoices,
        };
        var newCueTarget = new ComboBox
        {
            Header = "Cue Target",
            ItemsSource = new List<string> { "mediaUnderlay", "mediaOverlay", "audio" },
            SelectedItem = "mediaUnderlay",
        };
        var newCueFit = new ComboBox
        {
            Header = "Cue Fit",
            ItemsSource = new List<string> { "cover", "contain", "fill", "none" },
            SelectedItem = "cover",
        };
        var cueLoop = new ToggleSwitch { Header = "Loop Cue Video", IsOn = true };
        var cueMuted = new ToggleSwitch { Header = "Mute Cue Video", IsOn = true };
        var cueAutoplay = new ToggleSwitch { Header = "Autoplay Cue", IsOn = true };

        var dialog = new ContentDialog
        {
            Title = "Slide Media Actions",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 520,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        backgroundMode,
                        backgroundMedia,
                        backgroundFit,
                        backgroundOpacity,
                        backgroundLoop,
                        backgroundMuted,
                        new TextBlock { Text = "Media Cues", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        removeCueCombo,
                        newCueMedia,
                        newCueTarget,
                        newCueFit,
                        cueLoop,
                        cueMuted,
                        cueAutoplay,
                    },
                },
            },
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var cues = CloneMediaCues(slide);
        if (removeCueCombo.SelectedItem is CueChoice removeCue)
            cues.RemoveAll(cue => string.Equals(cue.Id, removeCue.Cue.Id, StringComparison.OrdinalIgnoreCase));

        if (newCueMedia.SelectedItem is MediaChoice cueMediaChoice)
        {
            cues.Add(new SlideMediaCue
            {
                Id = Guid.NewGuid().ToString("N"),
                MediaId = cueMediaChoice.Entry.Id,
                MediaType = cueMediaChoice.Entry.Type,
                Target = (newCueTarget.SelectedItem as string) ?? "mediaUnderlay",
                Fit = (newCueFit.SelectedItem as string) ?? "cover",
                Loop = cueMediaChoice.Entry.Type == "video" ? cueLoop.IsOn : null,
                Muted = cueMediaChoice.Entry.Type == "video" ? cueMuted.IsOn : null,
                Autoplay = cueMediaChoice.Entry.Type == "image" ? null : cueAutoplay.IsOn,
            });
        }

        var selectedBackgroundMode = backgroundMode.SelectedItem as string;
        SlideBackground? nextBackground = PresentationModelUtilities.DeepClone(currentBackground);
        if (string.Equals(selectedBackgroundMode, "Solid Black", StringComparison.Ordinal))
            nextBackground = new SolidSlideBackground { Color = "#000000" };
        else if (string.Equals(selectedBackgroundMode, "Transparent", StringComparison.Ordinal))
            nextBackground = new TransparentSlideBackground();
        else if (string.Equals(selectedBackgroundMode, "Image Background", StringComparison.Ordinal))
        {
            if (backgroundMedia.SelectedItem is not MediaChoice backgroundMediaChoice)
            {
                ViewModel.StatusMessage = "Choose background media before saving.";
                return null;
            }

            nextBackground = new ImageSlideBackground
            {
                MediaId = backgroundMediaChoice.Entry.Id,
                Fit = (backgroundFit.SelectedItem as string) ?? "cover",
                Opacity = backgroundOpacity.Value,
            };
        }
        else if (string.Equals(selectedBackgroundMode, "Video Background", StringComparison.Ordinal))
        {
            if (backgroundMedia.SelectedItem is not MediaChoice backgroundMediaChoice)
            {
                ViewModel.StatusMessage = "Choose background media before saving.";
                return null;
            }

            nextBackground = new VideoSlideBackground
            {
                MediaId = backgroundMediaChoice.Entry.Id,
                Fit = (backgroundFit.SelectedItem as string) ?? "cover",
                Opacity = backgroundOpacity.Value,
                Loop = backgroundLoop.IsOn,
                Muted = backgroundMuted.IsOn,
            };
        }

        return new MediaConfigurationResult(nextBackground, cues);
    }

    private static List<string> BuildHotKeyOptions()
    {
        var keys = new List<string>();
        keys.AddRange(Enumerable.Range(1, 12).Select(index => $"F{index}"));
        keys.AddRange(Enumerable.Range(0, 10).Select(index => $"Number{index}"));
        keys.AddRange(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()));
        return keys;
    }

    private static SlideActionDefinition CreateSlideAction(string type)
    {
        return new SlideActionDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Label = type switch
            {
                "clearPresentation" => "Clear Presentation",
                "clearMedia" => "Clear Media",
                "blackoutOn" => "Blackout On",
                "blackoutOff" => "Blackout Off",
                "clearAll" => "Clear All",
                _ => type,
            },
        };
    }

    private static string GetSlideActionLabel(SlideActionDefinition action) =>
        string.IsNullOrWhiteSpace(action.Label) ? action.Type : action.Label;

    private static string ResolveCueDisplayName(SlideMediaCue cue, IReadOnlyList<MediaChoice> mediaChoices)
    {
        var name = MediaCueDisplayNameResolver.Normalize(cue.DisplayName)
                   ?? mediaChoices.FirstOrDefault(choice => string.Equals(choice.Entry.Id, cue.MediaId, StringComparison.OrdinalIgnoreCase))?.Name
                   ?? MediaCueDisplayNameResolver.ResolveFallback(cue.MediaId);
        return $"{name} ({SlideMediaLayerBuilder.MapCueTarget(cue.Target)})";
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

        var file = await CreatePackageSavePicker(".cpplaylist", item.Playlist.Name).PickSaveFileAsync();
        if (file == null)
            return;

        try
        {
            await _collectionPackages.ExportPlaylistAsync(item.Playlist.Id, file.Path);
            ViewModel.StatusMessage = $"Exported {item.Playlist.Name} package.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            ViewModel.StatusMessage = ex.Message;
        }
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

    private async Task ExportPresentationBundleAsync(ShowPresentationTreeItem item)
    {
        var file = await CreatePresentationSavePicker(item.Presentation.Title).PickSaveFileAsync();
        if (file == null)
            return;

        await ExecuteSidebarActionAsync(async () =>
        {
            await _presentationActions.ExportPresentationBundleAsync(item.Presentation.Path, file.Path);
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\".";
        }, "Could not export bundle");
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
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\".";
        }, "Could not export bundle");
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
            ViewModel.StatusMessage = $"Exported \"{item.Presentation.Title}\".";
        }, "Could not export bundle");
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
        var slideSize = PresentationModelUtilities.GetBaseSlideSize(project.Manifest.AspectRatio, project.Manifest.SlideSize);
        var width = Math.Max(1, slideSize.Width);
        var height = Math.Max(1, slideSize.Height);
        var exported = 0;
        var failed = 0;

        Directory.CreateDirectory(targetFolderPath);

        foreach (var slide in project.Slides)
        {
            try
            {
                var stage = new SlideStageView
                {
                    Width = width,
                    Height = height,
                    Project = project,
                    Slide = slide,
                    RenderMode = SlideStageRenderMode.Output,
                };

                ExportRenderHost.Children.Clear();
                ExportRenderHost.Children.Add(stage);
                ExportRenderHost.UpdateLayout();
                await stage.WaitForExternalContentAsync();
                await Task.Delay(150);

                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(stage, width, height);
                var pixels = await bitmap.GetPixelsAsync();

                var fileName = $"{SanitizeFileName(project.Manifest.Title)}_{exported + failed + 1:D3}.png";
                var outputPath = Path.Combine(targetFolderPath, fileName);
                await SavePixelsAsPngAsync(outputPath, pixels, width, height);
                exported++;
            }
            catch
            {
                failed++;
            }
        }

        ExportRenderHost.Children.Clear();

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


    private void SlideDeckItemMenu_Opening(object sender, object e)
    {
        _contextMenuSlideItem = null;
        _contextMenuSlideTarget = null;

        if (sender is not MenuFlyout flyout || !TryGetSlideDeckItem(sender, out var item))
            return;

        _contextMenuSlideItem = item;
        _contextMenuSlideTarget = flyout.Target as FrameworkElement;
        BuildSlideDeckContextMenu(flyout, item);
    }

    private void BuildSlideDeckContextMenu(MenuFlyout flyout, ShowSlideDeckItem item)
    {
        flyout.Items.Clear();

        flyout.Items.Add(CreateSlideMenuItem(
            "Quick Edit",
            string.Empty,
            VirtualKeyModifiers.Control,
            VirtualKey.E,
            true,
            () => ShowQuickEditForSlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem(
            "Edit Slide",
            "\uE70F",
            null,
            VirtualKey.F4,
            true,
            () => OpenSlideInEditorAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateSlideMenuItem(
            item.Slide.Disabled ? "Enable" : "Disable",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => ToggleSlideDisabledAsync(item)));
        flyout.Items.Add(BuildSlideDeckThemesMenu(item));
        flyout.Items.Add(CreateSlideMenuItem(
            "Transitions...",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => EditSlideTransitionAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem(
            "Hot Key...",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => EditSlideHotKeyAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem(
            "Go to Next Timer...",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => EditTimerActionAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        var addActionMenu = new MenuFlyoutSubItem
        {
            Text = "Add Action",
        };
        BuildAddActionMenu(addActionMenu, item);
        flyout.Items.Add(addActionMenu);

        foreach (var dynamicItem in BuildExistingActionMenuItems(item))
            flyout.Items.Add(dynamicItem);
        if (item.Slide.Actions.Count > 0 || GetMediaCues(item.Slide).Count > 0)
            flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(BuildSlideGroupMenu(item));
        flyout.Items.Add(BuildSlideLabelMenu(item));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateSlideMenuItem(
            "Copy Text Style",
            string.Empty,
            VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift,
            VirtualKey.C,
            item.Slide.Layers.OfType<TextLayer>().Any(),
            () =>
            {
                _textStyleClipboard.SetFromSlide(item.Slide);
                ViewModel.StatusMessage = "Copied slide text style.";
                return Task.CompletedTask;
            }));
        flyout.Items.Add(CreateSlideMenuItem(
            "Paste Text Style",
            string.Empty,
            VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift,
            VirtualKey.V,
            _textStyleClipboard.HasEntries && item.Slide.Layers.OfType<TextLayer>().Any(),
            () => PasteSlideTextStyleAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateSlideMenuItem("Cut", "\uE8C6", VirtualKeyModifiers.Control, VirtualKey.X, true, () => CutSlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem("Copy", "\uE8C8", VirtualKeyModifiers.Control, VirtualKey.C, true, () => CopySlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem("Paste", "\uE77F", VirtualKeyModifiers.Control, VirtualKey.V, _slideClipboard.HasSlide, () => PasteSlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem("Delete", "\uE74D", null, VirtualKey.Delete, true, () => DeleteSlideAsync(item)));
    }

    private MenuFlyoutItem CreateSlideMenuItem(
        string text,
        string iconGlyph,
        VirtualKeyModifiers? acceleratorModifiers,
        VirtualKey acceleratorKey,
        bool isEnabled,
        Func<Task> execute)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = isEnabled,
            Icon = string.IsNullOrWhiteSpace(iconGlyph)
                ? null
                : new FontIcon { Glyph = iconGlyph },
        };

        if (acceleratorKey != VirtualKey.None)
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = acceleratorKey,
                Modifiers = acceleratorModifiers ?? VirtualKeyModifiers.None,
                ScopeOwner = KeyboardFocusTarget,
            };
            accelerator.Invoked += async (_, args) =>
            {
                if (!item.IsEnabled)
                    return;

                await ExecuteHandledActionAsync(execute, $"Could not complete \"{text}\".");
                args.Handled = true;
            };
            item.KeyboardAccelerators.Add(accelerator);
        }

        item.Click += async (_, _) =>
        {
            if (item.IsEnabled)
                await ExecuteHandledActionAsync(execute, $"Could not complete \"{text}\".");
        };
        return item;
    }

    private static MenuFlyoutItem CreateDisabledSlideMenuItem(string text) =>
        new()
        {
            Text = text,
            IsEnabled = false,
        };

    private MenuFlyoutSubItem BuildSlideDeckThemesMenu(ShowSlideDeckItem item)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Themes",
        };
        menu.Items.Add(CreateDisabledSlideMenuItem("Loading themes..."));
        _ = PopulateSlideThemeMenuAsync(menu, item);
        return menu;
    }

    private async Task PopulateSlideThemeMenuAsync(MenuFlyoutSubItem menu, ShowSlideDeckItem item)
    {
        try
        {
            var presentationPath = ResolveSlideDeckPresentationPath(item);
            var project = _projects.Open(presentationPath);
            var globalThemes = (await _themeLibrary.LoadAsync()).ToList();
            var choices = BuildThemeMenuChoices(project, globalThemes);

            menu.Items.Clear();
            if (choices.Count == 0)
                menu.Items.Add(CreateNoThemesAvailableMenuItem());

            AddRecentThemeMenuItems(menu, item, choices);
            AddTopLevelThemeMenuItems(menu, item, choices);
            AddFolderThemeMenuItems(menu, item, choices);

            if (menu.Items.Count > 0)
                menu.Items.Add(new MenuFlyoutSeparator());

            var applyMediaActionsItem = new ToggleMenuFlyoutItem
            {
                Text = "Apply Media Actions with Theme Slide",
                IsChecked = _settings.Settings.Show.ApplyMediaActionsWithThemeSlide,
            };
            applyMediaActionsItem.Click += async (_, _) =>
            {
                _settings.Update(settings => settings.Show.ApplyMediaActionsWithThemeSlide = applyMediaActionsItem.IsChecked);
                await _settings.SaveAsync();
            };
            menu.Items.Add(applyMediaActionsItem);

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateSlideMenuItem("New Theme...", string.Empty, null, VirtualKey.None, true, () => CreateBlankThemeAsync(item)));
            menu.Items.Add(CreateSlideMenuItem("New Theme from Selection", string.Empty, null, VirtualKey.None, true, () => CreateThemeFromSelectionAsync(item)));
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            menu.Items.Add(CreateDisabledSlideMenuItem($"Could not load themes: {ex.Message}"));
        }
    }

    private List<ThemeMenuChoice> BuildThemeMenuChoices(PresentationProject project, IReadOnlyList<ThemeTemplate> globalThemes)
    {
        var choices = globalThemes
            .Select(theme => new ThemeMenuChoice(theme, theme.Name, theme.Folder, IsEmbedded: false))
            .ToList();

        choices.AddRange(project.EmbeddedThemes
            .Where(entry => entry.Template != null)
            .Select(entry => new ThemeMenuChoice(entry.Template!, $"{entry.Template!.Name} (Embedded)", entry.Template!.Folder, IsEmbedded: true)));

        return choices
            .Where(choice => !string.IsNullOrWhiteSpace(choice.Theme.Id) && !string.IsNullOrWhiteSpace(choice.Theme.Name))
            .OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void AddRecentThemeMenuItems(MenuFlyoutSubItem menu, ShowSlideDeckItem item, IReadOnlyList<ThemeMenuChoice> choices)
    {
        var recentChoices = _settings.Settings.Show.RecentThemeIds
            .Select(id => choices.FirstOrDefault(choice => string.Equals(choice.Theme.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(choice => choice != null)
            .Cast<ThemeMenuChoice>()
            .Take(MaxTopLevelThemeMenuItems)
            .ToList();
        if (recentChoices.Count == 0)
            return;

        menu.Items.Add(CreateDisabledSlideMenuItem("Recents"));
        foreach (var choice in recentChoices)
            menu.Items.Add(BuildThemeChoiceMenu(item, choice));
        menu.Items.Add(new MenuFlyoutSeparator());
    }

    private void AddTopLevelThemeMenuItems(MenuFlyoutSubItem menu, ShowSlideDeckItem item, IReadOnlyList<ThemeMenuChoice> choices)
    {
        var topLevel = choices
            .Where(choice => string.IsNullOrWhiteSpace(choice.Folder))
            .Take(MaxTopLevelThemeMenuItems)
            .ToList();
        foreach (var choice in topLevel)
            menu.Items.Add(BuildThemeChoiceMenu(item, choice));

        var hasFolderChoices = choices.Any(choice => !string.IsNullOrWhiteSpace(choice.Folder));
        if (topLevel.Count > 0 && hasFolderChoices)
            menu.Items.Add(new MenuFlyoutSeparator());
    }

    private void AddFolderThemeMenuItems(MenuFlyoutSubItem menu, ShowSlideDeckItem item, IReadOnlyList<ThemeMenuChoice> choices)
    {
        foreach (var folderGroup in choices
                     .Where(choice => !string.IsNullOrWhiteSpace(choice.Folder))
                     .GroupBy(choice => choice.Folder!.Trim(), StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var folderMenu = new MenuFlyoutSubItem
            {
                Text = folderGroup.Key,
            };
            foreach (var choice in folderGroup.OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                folderMenu.Items.Add(BuildThemeChoiceMenu(item, choice));
            menu.Items.Add(folderMenu);
        }
    }

    private MenuFlyoutSubItem BuildThemeChoiceMenu(ShowSlideDeckItem item, ThemeMenuChoice choice)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = choice.DisplayName,
        };

        var slides = choice.Theme.Slides.Count == 0
            ? new List<ThemeTemplateSlide> { new() { Id = string.Empty, Name = "Default", Background = new SolidSlideBackground { Color = "#000000" } } }
            : choice.Theme.Slides;
        foreach (var slide in slides)
        {
            var slideName = string.IsNullOrWhiteSpace(slide.Name)
                ? $"Theme Slide {choice.Theme.Slides.IndexOf(slide) + 1}"
                : slide.Name!;
            menu.Items.Add(CreateSlideMenuItem(slideName, string.Empty, null, VirtualKey.None, true, () => ApplyThemeToSlideAsync(item, choice, slide)));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateSlideMenuItem("Edit Theme...", string.Empty, null, VirtualKey.None, true, () => EditThemeAsync(choice)));
        menu.Items.Add(CreateSlideMenuItem("Add Selected Slide to Theme", string.Empty, null, VirtualKey.None, true, () => AddSelectedSlideToThemeAsync(item, choice)));
        return menu;
    }

    private MenuFlyoutSubItem BuildSlideGroupMenu(ShowSlideDeckItem item)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Group",
        };

        foreach (var (section, label) in new[]
                 {
                     ("title", "Title"),
                     ("intro", "Intro"),
                     ("verse", "Verse"),
                     ("pre-chorus", "Pre-Chorus"),
                     ("chorus", "Chorus"),
                     ("bridge", "Bridge"),
                     ("tag", "Tag"),
                     ("outro", "Outro"),
                 })
        {
            menu.Items.Add(CreateSlideGroupMenuItem(item, section, label));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateSlideMenuItem("Clear Group", string.Empty, null, VirtualKey.None, !string.IsNullOrWhiteSpace(item.Slide.Section) || !string.IsNullOrWhiteSpace(item.Slide.SectionLabel), () => ClearSlideGroupAsync(item)));
        return menu;
    }

    private MenuFlyoutItem CreateSlideGroupMenuItem(ShowSlideDeckItem item, string section, string label)
    {
        var menuItem = CreateSlideMenuItem(label, string.Empty, null, VirtualKey.None, true, () => SetSlideGroupAsync(item, section));
        var color = ParseMenuColor(SlideGroupThumbnailColors.GetHexColorForSlide(new PresentationSlide
        {
            Section = section,
            SectionLabel = label,
            Type = "content",
        }));
        menuItem.Icon = null;
        menuItem.Style = (Style)Resources["SlideGroupMenuFlyoutItemStyle"];
        menuItem.Tag = new SlideGroupMenuChoice(label, new SolidColorBrush(color));
        return menuItem;
    }

    private MenuFlyoutItem CreateNoThemesAvailableMenuItem() =>
        new()
        {
            Text = "No themes available",
            Tag = "No themes available",
            IsEnabled = false,
            Style = (Style)Resources["SlideContextNoThemeMenuFlyoutItemStyle"],
        };

    private MenuFlyoutSubItem BuildSlideLabelMenu(ShowSlideDeckItem item)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Label",
        };
        menu.Items.Add(CreateSlideMenuItem("Edit Label...", string.Empty, null, VirtualKey.None, true, () => EditSlideLabelAsync(item)));
        menu.Items.Add(CreateSlideMenuItem("Clear Label", string.Empty, null, VirtualKey.None, !string.IsNullOrWhiteSpace(item.Slide.SectionLabel), () => ClearSlideLabelAsync(item)));
        return menu;
    }

    private static Windows.UI.Color ParseMenuColor(string hex)
    {
        hex = (hex ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length != 6)
            return Windows.UI.Color.FromArgb(255, 100, 116, 139);

        return Windows.UI.Color.FromArgb(
            255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    private void BuildAddActionMenu(MenuFlyoutSubItem addActionMenu, ShowSlideDeckItem item)
    {
        addActionMenu.Items.Clear();
        addActionMenu.Items.Add(CreateAddActionCategory("Clear",
        [
            CreateMenuItem("Clear Presentation", async () => await AddSlideActionAsync(item, "clearPresentation", "Clear Presentation")),
            CreateMenuItem("Clear Media", async () => await AddSlideActionAsync(item, "clearMedia", "Clear Media")),
            CreateMenuItem("Clear All", async () => await AddSlideActionAsync(item, "clearAll", "Clear All")),
        ]));
        addActionMenu.Items.Add(CreateAddActionCategory("Audience Look",
        [
            CreateMenuItem("Blackout On", async () => await AddSlideActionAsync(item, "blackoutOn", "Blackout On")),
            CreateMenuItem("Blackout Off", async () => await AddSlideActionAsync(item, "blackoutOff", "Blackout Off")),
        ]));
        addActionMenu.Items.Add(CreateAddActionCategory("Timer",
        [
            CreateMenuItem("Go to Next Timer...", async () => await EditTimerActionAsync(item)),
        ]));
        addActionMenu.Items.Add(CreateAddActionCategory("Video Input",
        [
            CreateMenuItem("Media Action...", async () => await EditSlideMediaActionsAsync(item)),
        ]));
    }

    private List<MenuFlyoutItemBase> BuildExistingActionMenuItems(ShowSlideDeckItem item)
    {
        var items = new List<MenuFlyoutItemBase>();

        foreach (var action in item.Slide.Actions)
        {
            var label = GetSlideActionLabel(action);
            var editMenu = new MenuFlyoutSubItem { Text = $"Edit Action: {label}" };
            editMenu.Items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveSlideActionAsync(item, action.Id, label)));
            items.Add(editMenu);
            items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveSlideActionAsync(item, action.Id, label)));
        }

        foreach (var cue in GetMediaCues(item.Slide))
        {
            var label = ResolveMediaCueActionLabel(item, cue);
            var editMenu = new MenuFlyoutSubItem { Text = $"Edit Action: {label}" };
            editMenu.Items.Add(CreateMenuItem("Inspector...", async () => await EditSlideMediaActionsAsync(item)));
            editMenu.Items.Add(CreateMediaCueTargetMenu(item, cue));
            editMenu.Items.Add(CreateMediaCueFitMenu(item, cue));
            editMenu.Items.Add(CreateMediaCueToggleItem("Playback Behavior", cue.Autoplay ?? false, async isChecked =>
                await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Autoplay = isChecked, $"Updated playback behavior for \"{label}\".")));
            editMenu.Items.Add(CreateMediaCueToggleItem("Loop Behavior", cue.Loop ?? false, async isChecked =>
                await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Loop = isChecked, $"Updated loop behavior for \"{label}\".")));
            editMenu.Items.Add(CreateMenuItem("Open File Location", () => OpenMediaCueFileLocation(item, cue)));
            editMenu.Items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveMediaCueAsync(item, cue.Id, label)));
            items.Add(editMenu);
            items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveMediaCueAsync(item, cue.Id, label)));
        }

        if (GetMediaCues(item.Slide).Count > 0)
            items.Add(CreateMenuItem("Inspector...", async () => await EditSlideMediaActionsAsync(item)));

        return items;
    }

    private MenuFlyoutSubItem CreateMediaCueTargetMenu(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        var menu = new MenuFlyoutSubItem { Text = "Behavior" };
        menu.Items.Add(CreateMenuItem("Media Underlay", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Target = "mediaUnderlay", "Updated media action behavior.")));
        menu.Items.Add(CreateMenuItem("Media Overlay", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Target = "mediaOverlay", "Updated media action behavior.")));
        menu.Items.Add(CreateMenuItem("Audio", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Target = "audio", "Updated media action behavior.")));
        return menu;
    }

    private MenuFlyoutSubItem CreateMediaCueFitMenu(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        var menu = new MenuFlyoutSubItem { Text = "Scaling" };
        menu.Items.Add(CreateMenuItem("Cover", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "cover", "Updated media action scaling.")));
        menu.Items.Add(CreateMenuItem("Contain", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "contain", "Updated media action scaling.")));
        menu.Items.Add(CreateMenuItem("Fill", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "fill", "Updated media action scaling.")));
        menu.Items.Add(CreateMenuItem("None", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "none", "Updated media action scaling.")));
        return menu;
    }

    private MenuFlyoutSubItem CreateAddActionCategory(string text, IEnumerable<MenuFlyoutItemBase> children)
    {
        var menu = new MenuFlyoutSubItem { Text = text };
        foreach (var child in children)
            menu.Items.Add(child);
        return menu;
    }

    private MenuFlyoutItem CreateMenuItem(string text, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            try
            {
                onClick();
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"Could not complete \"{text}\": {ex.Message}";
            }
        };
        return item;
    }

    private MenuFlyoutItem CreateMenuItem(string text, Func<Task> onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) =>
        {
            try
            {
                await onClick();
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"Could not complete \"{text}\": {ex.Message}";
            }
        };
        return item;
    }

    private ToggleMenuFlyoutItem CreateMediaCueToggleItem(string text, bool isChecked, Func<bool, Task> onClick)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = isChecked,
        };
        item.Click += async (_, _) =>
        {
            try
            {
                await onClick(item.IsChecked);
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"Could not complete \"{text}\": {ex.Message}";
            }
        };
        return item;
    }

    private static int FindTopLevelMenuItemIndex(MenuFlyout flyout, string text)
    {
        for (var index = 0; index < flyout.Items.Count; index++)
        {
            switch (flyout.Items[index])
            {
                case MenuFlyoutSubItem subItem when string.Equals(subItem.Text, text, StringComparison.Ordinal):
                    return index;
                case MenuFlyoutItem menuItem when string.Equals(menuItem.Text, text, StringComparison.Ordinal):
                    return index;
            }
        }

        return -1;
    }

    private Task ShowQuickEditForSlideAsync(ShowSlideDeckItem item)
    {
        if (_contextMenuSlideTarget == null)
            return Task.CompletedTask;

        return ShowQuickEditFlyoutAsync(_contextMenuSlideTarget, item);
    }

    private Task OpenSlideInEditorAsync(ShowSlideDeckItem item) =>
        OpenPresentationInEditorAsync(
            ResolveSlideDeckPresentationPath(item),
            ViewModel.SelectedLibraryId,
            ViewModel.SelectedPlaylistId,
            item.Slide.Id);

    private async Task ToggleSlideDisabledAsync(ShowSlideDeckItem item)
    {
        var wasDisabled = item.Slide.Disabled;
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Disabled = !slide.Disabled);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = wasDisabled ? "Enabled slide." : "Disabled slide.";
    }

    private async Task ApplyThemeToSlideAsync(ShowSlideDeckItem item, ThemeMenuChoice choice, ThemeTemplateSlide themeSlide)
    {
        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var applyMediaActions = _settings.Settings.Show.ApplyMediaActionsWithThemeSlide;
        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            item.Slide.Id,
            (slide, targetProject) =>
            {
                var preservedBackground = PresentationModelUtilities.DeepClone(slide.Background);
                var preservedCues = slide.MediaCues
                    .Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue())
                    .ToList();

                _themeApplier.ApplyThemeSlideToSlide(
                    slide,
                    themeSlide,
                    new ThemeApplyOptions
                    {
                        ScaleMode = "fit",
                        SourceSize = choice.Theme.BaseSize,
                        TargetSize = targetProject.Manifest.SlideSize ?? PresentationModelUtilities.GetBaseSlideSize(targetProject.Manifest.AspectRatio),
                    });

                if (!applyMediaActions)
                {
                    slide.Background = preservedBackground;
                    slide.MediaCues = preservedCues;
                }
            });

        await RecordRecentThemeAsync(choice.Theme.Id);
        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Applied theme \"{choice.Theme.Name}\".";
    }

    private async Task RecordRecentThemeAsync(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return;

        _settings.Update(settings =>
        {
            settings.Show.RecentThemeIds.RemoveAll(id => string.Equals(id, themeId, StringComparison.OrdinalIgnoreCase));
            settings.Show.RecentThemeIds.Insert(0, themeId);
            if (settings.Show.RecentThemeIds.Count > MaxTopLevelThemeMenuItems)
                settings.Show.RecentThemeIds.RemoveRange(MaxTopLevelThemeMenuItems, settings.Show.RecentThemeIds.Count - MaxTopLevelThemeMenuItems);
        });
        await _settings.SaveAsync();
    }

    private async Task CreateBlankThemeAsync(ShowSlideDeckItem item)
    {
        var name = await PromptForNameAsync("New Theme", "Theme name", "New Theme");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var now = DateTime.UtcNow.ToString("O");
        var aspectRatio = project.Manifest.AspectRatio ?? "16:9";
        var theme = new ThemeTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            AspectRatio = aspectRatio,
            BaseSize = PresentationModelUtilities.GetBaseSlideSize(aspectRatio),
            Slides =
            {
                new ThemeTemplateSlide
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Default",
                    Background = new SolidSlideBackground { Color = "#000000" },
                },
            },
        };

        await _themeLibrary.SaveThemeAsync(theme);
        await RecordRecentThemeAsync(theme.Id);
        ViewModel.StatusMessage = $"Created theme \"{theme.Name}\".";
    }

    private async Task CreateThemeFromSelectionAsync(ShowSlideDeckItem item)
    {
        var defaultName = string.IsNullOrWhiteSpace(item.Slide.SectionLabel) ? "Selected Slide Theme" : item.Slide.SectionLabel!;
        var name = await PromptForNameAsync("New Theme from Selection", "Theme name", defaultName);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var theme = PresentationModelUtilities.CreateThemeFromSlide(name, item.Slide, project.Manifest.AspectRatio ?? "16:9");
        await _themeLibrary.SaveThemeAsync(theme);
        await RecordRecentThemeAsync(theme.Id);
        ViewModel.StatusMessage = $"Created theme \"{theme.Name}\" from slide {item.Ordinal}.";
    }

    private async Task AddSelectedSlideToThemeAsync(ShowSlideDeckItem item, ThemeMenuChoice choice)
    {
        var themeSlide = PresentationModelUtilities.CreateThemeSlideFromSlide(item.Slide);
        if (choice.IsEmbedded)
        {
            var presentationPath = ResolveSlideDeckPresentationPath(item);
            var project = _projects.Open(presentationPath);
            var embedded = project.EmbeddedThemes.FirstOrDefault(entry =>
                string.Equals(entry.Template?.Id, choice.Theme.Id, StringComparison.OrdinalIgnoreCase));
            if (embedded?.Template == null)
                return;

            embedded.Template.Slides.Add(themeSlide);
            project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
            _projects.Save(project, presentationPath);
            await ViewModel.OpenPresentationFromPathAsync(presentationPath);
        }
        else
        {
            var theme = await _themeLibrary.LoadThemeAsync(choice.Theme.Id) ?? choice.Theme;
            theme.Slides.Add(themeSlide);
            await _themeLibrary.SaveThemeAsync(theme);
        }

        await RecordRecentThemeAsync(choice.Theme.Id);
        ViewModel.StatusMessage = $"Added slide {item.Ordinal} to theme \"{choice.Theme.Name}\".";
    }

    private Task EditThemeAsync(ThemeMenuChoice choice)
    {
        if (App.MainWindow is MainWindow window)
            window.NavigateToThemeLibraryPage();

        ViewModel.StatusMessage = $"Opened Theme Library to edit \"{choice.Theme.Name}\".";
        return Task.CompletedTask;
    }

    private async Task EditSlideTransitionAsync(ShowSlideDeckItem item)
    {
        var transitionResult = await PromptForTransitionAsync(item.Slide.Animations?.Transition);
        if (!transitionResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Animations ??= new SlideAnimations();
                slide.Animations.Transition = transitionResult.ClearRequested
                    ? null
                    : TransitionStorageNormalizer.NormalizeForStorage(transitionResult.Transition);
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = transitionResult.ClearRequested
            ? "Cleared slide transition override."
            : "Updated slide transition override.";
    }

    private async Task EditSlideHotKeyAsync(ShowSlideDeckItem item)
    {
        var hotKeyResult = await PromptForHotKeyAsync(item.Slide.HotKey);
        if (!hotKeyResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.HotKey = hotKeyResult.ClearRequested ? null : hotKeyResult.Value);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = hotKeyResult.ClearRequested ? "Cleared slide hot key." : $"Assigned hot key {hotKeyResult.Value}.";
    }

    private async Task SetSlideGroupAsync(ShowSlideDeckItem item, string section)
    {
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = section;
                slide.SectionLabel = PresentationModelUtilities.FormatSectionLabel(section, slide.SectionIndex);
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Assigned slide to {PresentationModelUtilities.FormatSectionLabel(section)}.";
    }

    private async Task ClearSlideGroupAsync(ShowSlideDeckItem item)
    {
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = null;
                slide.SectionLabel = null;
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Cleared slide group.";
    }

    private async Task EditSlideLabelAsync(ShowSlideDeckItem item)
    {
        var label = await PromptForNameAsync("Slide Label", "Label", item.Slide.SectionLabel ?? string.Empty);
        if (label == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = label);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Updated slide label.";
    }

    private async Task ClearSlideLabelAsync(ShowSlideDeckItem item)
    {
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = null);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Cleared slide label.";
    }

    private async Task PasteSlideTextStyleAsync(ShowSlideDeckItem item)
    {
        if (!_textStyleClipboard.HasEntries)
            return;

        var entries = _textStyleClipboard.Entries.ToList();
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, project) =>
            {
                var targetTextLayers = slide.Layers.OfType<TextLayer>().ToList();
                for (var index = 0; index < targetTextLayers.Count && index < entries.Count; index++)
                {
                    var targetLayer = targetTextLayers[index];
                    var source = entries[index];
                    targetLayer.Style = PresentationModelUtilities.DeepClone(source.Style) ?? PresentationModelUtilities.CreateDefaultTextStyle();
                    targetLayer.Fills = source.Fills.Select(fill => PresentationModelUtilities.DeepClone(fill) ?? new LayerFillModel()).ToList();
                    targetLayer.Strokes = source.Strokes.Select(stroke => PresentationModelUtilities.DeepClone(stroke) ?? new LayerStrokeModel()).ToList();
                    targetLayer.Effects = source.Effects.Select(effect => PresentationModelUtilities.DeepClone(effect) ?? new LayerBlurEffectModel()).ToList();
                    targetLayer.Padding = source.Padding;
                    PresentationModelUtilities.NormalizeLayer(targetLayer, project.Manifest.SlideSize);
                }
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Pasted slide text style.";
    }

    private Task CutSlideAsync(ShowSlideDeckItem item)
    {
        _slideClipboard.SetCut(ResolveSlideDeckPresentationPath(item), item.Slide);
        ViewModel.StatusMessage = $"Cut slide {item.Ordinal}.";
        return Task.CompletedTask;
    }

    private Task CopySlideAsync(ShowSlideDeckItem item)
    {
        _slideClipboard.SetCopy(ResolveSlideDeckPresentationPath(item), item.Slide);
        ViewModel.StatusMessage = $"Copied slide {item.Ordinal}.";
        return Task.CompletedTask;
    }

    private async Task PasteSlideAsync(ShowSlideDeckItem item)
    {
        if (_slideClipboard.Entry == null)
            return;

        var result = await _slideItemActions.PasteSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            _slideClipboard.Entry,
            SlidePastePosition.After);

        if (_slideClipboard.Entry.IsCut)
            _slideClipboard.Clear();

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Pasted slide.";
    }

    private async Task DeleteSlideAsync(ShowSlideDeckItem item)
    {
        if (!await ConfirmAsync("Delete Slide", $"Delete slide {item.Ordinal} from this presentation?", "Delete"))
            return;

        var result = await _slideItemActions.DeleteSlideAsync(ResolveSlideDeckPresentationPath(item), item.Slide.Id);
        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Deleted slide.";
    }

    private async void SlideDeckQuickEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckTarget(sender, out var target, out var item))
            return;

        await ShowQuickEditFlyoutAsync(target, item);
    }

    private async void SlideDeckEditSlide_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await OpenPresentationInEditorAsync(
            ResolveSlideDeckPresentationPath(item),
            ViewModel.SelectedLibraryId,
            ViewModel.SelectedPlaylistId,
            item.Slide.Id);
    }

    private async void SlideDeckToggleDisable_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Disabled = !slide.Disabled);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = item.Slide.Disabled ? "Enabled slide." : "Disabled slide.";
    }

    private async void SlideDeckApplyTheme_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var selection = await PromptForThemeSelectionAsync(project);
        if (selection == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            item.Slide.Id,
            (slide, targetProject) =>
            {
                _themeApplier.ApplyThemeSlideToSlide(
                    slide,
                    selection.Slide,
                    new ThemeApplyOptions
                    {
                        ScaleMode = "fit",
                        SourceSize = selection.Theme.BaseSize,
                        TargetSize = targetProject.Manifest.SlideSize ?? PresentationModelUtilities.GetBaseSlideSize(targetProject.Manifest.AspectRatio),
                    });
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Applied theme \"{selection.Theme.Name}\".";
    }

    private async void SlideDeckEditTransition_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var transitionResult = await PromptForTransitionAsync(item.Slide.Animations?.Transition);
        if (!transitionResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Animations ??= new SlideAnimations();
                slide.Animations.Transition = transitionResult.ClearRequested
                    ? null
                    : TransitionStorageNormalizer.NormalizeForStorage(transitionResult.Transition);
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = transitionResult.ClearRequested
            ? "Cleared slide transition override."
            : "Updated slide transition override.";
    }

    private async void SlideDeckEditHotKey_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var hotKeyResult = await PromptForHotKeyAsync(item.Slide.HotKey);
        if (!hotKeyResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.HotKey = hotKeyResult.ClearRequested ? null : hotKeyResult.Value);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = hotKeyResult.ClearRequested ? "Cleared slide hot key." : $"Assigned hot key {hotKeyResult.Value}.";
    }

    private async void SlideDeckEditTimer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await EditTimerActionAsync(item);
    }

    private async void SlideDeckManageActions_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var actions = await PromptForSlideActionsAsync(item.Slide.Actions);
        if (actions == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Actions = actions.Select(action => PresentationModelUtilities.DeepClone(action) ?? new SlideActionDefinition()).ToList());

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Updated slide actions.";
    }

    private async void SlideDeckAddClearPresentationAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "clearPresentation", "Clear Presentation");

    private async void SlideDeckAddClearMediaAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "clearMedia", "Clear Media");

    private async void SlideDeckAddBlackoutOnAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "blackoutOn", "Blackout On");

    private async void SlideDeckAddBlackoutOffAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "blackoutOff", "Blackout Off");

    private async Task AddSlideActionAsync(object sender, string type, string label)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await AddSlideActionAsync(item, type, label);
    }

    private async Task AddSlideActionAsync(ShowSlideDeckItem item, string type, string label)
    {
        ArgumentNullException.ThrowIfNull(item);

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Actions ??= new List<SlideActionDefinition>();
                slide.Actions.Add(new SlideActionDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = type,
                    Label = label,
                });
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Added slide action \"{label}\".";
    }

    private async void SlideDeckEditMediaActions_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await ExecuteHandledActionAsync(
            () => EditSlideMediaActionsAsync(item),
            "Could not update slide media actions.");
    }

    private async Task EditSlideMediaActionsAsync(ShowSlideDeckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var mediaResult = await PromptForMediaConfigurationAsync(project, item.Slide);
        if (mediaResult == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Background = PresentationModelUtilities.DeepClone(mediaResult.Background);
                slide.MediaCues = mediaResult.Cues.Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue()).ToList();
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Updated slide media actions.";
    }

    private async Task EditTimerActionAsync(ShowSlideDeckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var timerResult = await PromptForTimerAssignmentAsync(item.Slide.GoToNextTimerId);
        if (!timerResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.GoToNextTimerId = timerResult.ClearRequested ? null : timerResult.Value);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = timerResult.ClearRequested ? "Cleared slide timer action." : "Updated slide timer action.";
    }

    private async Task RemoveSlideActionAsync(ShowSlideDeckItem item, string actionId, string label)
    {
        ArgumentNullException.ThrowIfNull(item);

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Actions.RemoveAll(action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase)));

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Removed action \"{label}\".";
    }

    private async Task UpdateMediaCueAsync(
        ShowSlideDeckItem item,
        string cueId,
        Action<SlideMediaCue> mutate,
        string statusMessage)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(mutate);

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.MediaCues ??= new List<SlideMediaCue>();
                var cue = slide.MediaCues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, cueId, StringComparison.OrdinalIgnoreCase));
                if (cue != null)
                    mutate(cue);
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = statusMessage;
    }

    private async Task RemoveMediaCueAsync(ShowSlideDeckItem item, string cueId, string label)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.MediaCues ??= new List<SlideMediaCue>();
                slide.MediaCues.RemoveAll(cue => string.Equals(cue.Id, cueId, StringComparison.OrdinalIgnoreCase));
            });
        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Removed action \"{label}\".";
    }

    private IReadOnlyList<SlideMediaCue> GetMediaCues(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return slide.MediaCues ?? [];
    }

    private static List<SlideMediaCue> CloneMediaCues(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return (slide.MediaCues ?? [])
            .Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue())
            .ToList();
    }

    private async Task ExecuteHandledActionAsync(Func<Task> action, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            ViewModel.StatusMessage = $"{failureMessage} {ex.Message}";
        }
    }

    private void DismissSlideContextMenu()
    {
        try
        {
            _contextMenuSlideTarget?.ContextFlyout?.Hide();
        }
        catch
        {
            // Best-effort only; the flyout may already be closing.
        }

        _contextMenuSlideItem = null;
        _contextMenuSlideTarget = null;
    }

    private void OpenMediaCueFileLocation(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        try
        {
            var presentationPath = ResolveSlideDeckPresentationPath(item);
            var project = _projects.Open(presentationPath);
            var resolvedPath = _assetCache.ResolveMediaPath(project, cue.MediaId);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                ViewModel.StatusMessage = "Could not resolve the media file location.";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{resolvedPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open media file location: {ex.Message}";
        }
    }

    private string ResolveMediaCueActionLabel(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        var cueDisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName);
        if (cueDisplayName != null)
            return cueDisplayName;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        return MediaCueDisplayNameResolver.Resolve(cue, project);
    }

    private async void SlideDeckSetGroupTitle_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "title");
    private async void SlideDeckSetGroupIntro_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "intro");
    private async void SlideDeckSetGroupVerse_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "verse");
    private async void SlideDeckSetGroupPreChorus_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "pre-chorus");
    private async void SlideDeckSetGroupChorus_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "chorus");
    private async void SlideDeckSetGroupBridge_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "bridge");
    private async void SlideDeckSetGroupTag_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "tag");
    private async void SlideDeckSetGroupOutro_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "outro");

    private async Task SetSlideGroupAsync(object sender, string section)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = section;
                slide.SectionLabel = PresentationModelUtilities.FormatSectionLabel(section, slide.SectionIndex);
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = $"Assigned slide to {PresentationModelUtilities.FormatSectionLabel(section)}.";
    }

    private async void SlideDeckClearGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = null;
                slide.SectionLabel = null;
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Cleared slide group.";
    }

    private async void SlideDeckEditLabel_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var label = await PromptForNameAsync("Slide Label", "Label", item.Slide.SectionLabel ?? string.Empty);
        if (label == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = label);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Updated slide label.";
    }

    private async void SlideDeckClearLabel_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = null);

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Cleared slide label.";
    }

    private void SlideDeckCopyTextStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        _textStyleClipboard.SetFromSlide(item.Slide);
        ViewModel.StatusMessage = "Copied slide text style.";
    }

    private async void SlideDeckPasteTextStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item) || !_textStyleClipboard.HasEntries)
            return;

        var entries = _textStyleClipboard.Entries.ToList();
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, project) =>
            {
                var targetTextLayers = slide.Layers.OfType<TextLayer>().ToList();
                for (var index = 0; index < targetTextLayers.Count && index < entries.Count; index++)
                {
                    var targetLayer = targetTextLayers[index];
                    var source = entries[index];
                    targetLayer.Style = PresentationModelUtilities.DeepClone(source.Style) ?? PresentationModelUtilities.CreateDefaultTextStyle();
                    targetLayer.Fills = source.Fills.Select(fill => PresentationModelUtilities.DeepClone(fill) ?? new LayerFillModel()).ToList();
                    targetLayer.Strokes = source.Strokes.Select(stroke => PresentationModelUtilities.DeepClone(stroke) ?? new LayerStrokeModel()).ToList();
                    targetLayer.Effects = source.Effects.Select(effect => PresentationModelUtilities.DeepClone(effect) ?? new LayerBlurEffectModel()).ToList();
                    targetLayer.Padding = source.Padding;
                    PresentationModelUtilities.NormalizeLayer(targetLayer, project.Manifest.SlideSize);
                }
            });

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Pasted slide text style.";
    }

    private void SlideDeckCut_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        _slideClipboard.SetCut(ResolveSlideDeckPresentationPath(item), item.Slide);
        ViewModel.StatusMessage = $"Cut slide {item.Ordinal}.";
    }

    private void SlideDeckCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        _slideClipboard.SetCopy(ResolveSlideDeckPresentationPath(item), item.Slide);
        ViewModel.StatusMessage = $"Copied slide {item.Ordinal}.";
    }

    private async void SlideDeckPaste_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item) || _slideClipboard.Entry == null)
            return;

        var result = await _slideItemActions.PasteSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            _slideClipboard.Entry,
            SlidePastePosition.After);

        if (_slideClipboard.Entry.IsCut)
            _slideClipboard.Clear();

        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Pasted slide.";
    }

    private async void SlideDeckDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        if (!await ConfirmAsync("Delete Slide", $"Delete slide {item.Ordinal} from this presentation?", "Delete"))
            return;

        var result = await _slideItemActions.DeleteSlideAsync(ResolveSlideDeckPresentationPath(item), item.Slide.Id);
        await ReloadSlideMutationAsync(result);
        ViewModel.StatusMessage = "Deleted slide.";
    }

    private async Task ReloadSlideMutationAsync(SlideItemMutationResult result)
    {
        var opened = await ViewModel.OpenPresentationFromPathAsync(result.PresentationPath);
        if (!string.IsNullOrWhiteSpace(result.SelectedSlideId))
            await ViewModel.ActivateSlideSelectionAsync(result.PresentationPath, result.SelectedSlideId);
    }

    private async void SlideDeckItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ShowSlideDeckItem row)
            return;

        var modifiers = GetCurrentKeyModifiers();
        if (modifiers.HasFlag(VirtualKeyModifiers.Shift))
            await ViewModel.SelectSlideRangeAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);
        else if (modifiers.HasFlag(VirtualKeyModifiers.Control))
            await ViewModel.ActivateSlideSelectionAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);
        else
            await ViewModel.TakeSlideLiveAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);

        BringElementIntoView(fe);
        RestoreKeyboardFocus();
    }

    private void LayoutRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(LayoutRoot);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (IsPointerInsideSlideCard(e.OriginalSource as DependencyObject))
            return;

        ViewModel.ClearSlideSelection();
        RestoreKeyboardFocus();
    }

    private async void SlideDeckItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ShowSlideDeckItem row)
            return;

        await ViewModel.ActivateSlideSelectionAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);
        BringElementIntoView(fe);
    }

    private static bool IsPointerInsideSlideCard(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: ShowSlideDeckItem })
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsSlideSeekKey(VirtualKey key) =>
        key is VirtualKey.Right
            or VirtualKey.PageDown
            or VirtualKey.Left
            or VirtualKey.PageUp
            or VirtualKey.Back;

    private async Task<bool> TryHandleSelectedSlideShortcutAsync(VirtualKey key)
    {
        if (ViewModel.SelectedDeckRowForView is not { } item)
            return false;

        var modifiers = GetCurrentKeyModifiers();
        if (modifiers == VirtualKeyModifiers.Control)
        {
            switch (key)
            {
                case VirtualKey.C:
                    await CopySlideAsync(item);
                    return true;
                case VirtualKey.X:
                    await CutSlideAsync(item);
                    return true;
                case VirtualKey.V when _slideClipboard.HasSlide:
                    await PasteSlideAsync(item);
                    return true;
                case VirtualKey.E:
                    if (FindSelectedSlideDeckItemHost(this) is FrameworkElement target)
                        await ShowQuickEditFlyoutAsync(target, item);
                    return true;
            }
        }

        if (modifiers == (VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift))
        {
            switch (key)
            {
                case VirtualKey.C:
                    _textStyleClipboard.SetFromSlide(item.Slide);
                    ViewModel.StatusMessage = "Copied slide text style.";
                    return true;
                case VirtualKey.V when _textStyleClipboard.HasEntries:
                    await PasteSlideTextStyleAsync(item);
                    return true;
            }
        }

        switch (key)
        {
            case VirtualKey.Delete:
                await DeleteSlideAsync(item);
                return true;
            case VirtualKey.F4:
                await OpenSlideInEditorAsync(item);
                return true;
        }

        return false;
    }

    private static VirtualKeyModifiers GetCurrentKeyModifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsKeyDown(VirtualKey.Control))
            modifiers |= VirtualKeyModifiers.Control;
        if (IsKeyDown(VirtualKey.Shift))
            modifiers |= VirtualKeyModifiers.Shift;
        if (IsKeyDown(VirtualKey.Menu))
            modifiers |= VirtualKeyModifiers.Menu;
        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private static bool ShouldIgnoreShowPageKey(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TextBox
                or PasswordBox
                or RichEditBox
                or AutoSuggestBox
                or ComboBox
                or NumberBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void BringSelectedSlideDeckItemIntoView()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FindSelectedSlideDeckItemHost(this) is not FrameworkElement host)
                return;

            BringElementIntoView(host);
        });
    }

    /// <summary>Returns keyboard focus to the show-surface target so slide navigation continues after pointer actions.</summary>
    public void RestoreKeyboardFocus()
    {
        KeyboardFocusTarget.Focus(FocusState.Programmatic);
    }

    /// <summary>Moves focus to the sidebar presentation search box from shell chrome.</summary>
    public void FocusSourcesSearch()
    {
        if (!ViewModel.HasPresentationSources || ShowNavigationView.AutoSuggestBox != SourcesSearchBox)
        {
            RestoreKeyboardFocus();
            return;
        }

        SourcesSearchBox.Focus(FocusState.Programmatic);
    }

    private static void BringElementIntoView(FrameworkElement element)
    {
        element.StartBringIntoView();
    }

    private FrameworkElement? FindSelectedSlideDeckItemHost(DependencyObject root)
    {
        var selectedItem = ViewModel.SelectedDeckRowForView;
        return selectedItem == null ? null : FindSelectedSlideDeckItemHost(root, selectedItem);
    }

    private static FrameworkElement? FindSelectedSlideDeckItemHost(DependencyObject root, ShowSlideDeckItem selectedItem)
    {
        // Tag is set to the ShowSlideDeckItem via x:Bind in each card DataTemplate.
        if (root is FrameworkElement { Tag: ShowSlideDeckItem item } element
            && ReferenceEquals(item, selectedItem))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var match = FindSelectedSlideDeckItemHost(VisualTreeHelper.GetChild(root, index), selectedItem);
            if (match != null)
                return match;
        }

        return null;
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

    private sealed record ThemeSelection(ThemeTemplate Theme, ThemeTemplateSlide Slide);

    private sealed record ThemeChoice(ThemeTemplate Theme, string Name);

    private sealed record ThemeSlideChoice(ThemeTemplateSlide Slide, string Name);

    private sealed record ThemeMenuChoice(ThemeTemplate Theme, string DisplayName, string? Folder, bool IsEmbedded);

    private sealed record SlideGroupMenuChoice(string Label, Brush Brush);

    private sealed record TransitionDialogResult(bool Submitted, bool ClearRequested, SlideTransition? Transition);

    private sealed record SelectionDialogResult(bool Submitted, bool ClearRequested, string? Value);

    private sealed record MediaConfigurationResult(SlideBackground? Background, List<SlideMediaCue> Cues);

    private sealed record TimerChoice(ShowTimerDefinition Timer)
    {
        public string Name => $"{Timer.Name} ({Timer.DurationSeconds}s)";
    }

    private sealed record ActionChoice(SlideActionDefinition Action)
    {
        public string Name => GetSlideActionLabel(Action);
    }

    private sealed record MediaChoice(MediaEntry Entry)
    {
        public string Name => string.IsNullOrWhiteSpace(Entry.FileName)
            ? $"{Entry.Id} ({Entry.Type})"
            : $"{Entry.FileName} ({Entry.Type})";
    }

    private sealed record CueChoice(SlideMediaCue Cue, string Name);
}