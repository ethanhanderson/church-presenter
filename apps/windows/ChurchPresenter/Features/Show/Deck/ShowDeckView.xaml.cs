using System;
using System.Linq;
using System.Threading.Tasks;

using ChurchPresenter.Features.Show.MediaDrawer;
using ChurchPresenter.Services.Show;
using ChurchPresenter.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace ChurchPresenter.Features.Show.Deck;

public sealed partial class ShowDeckView : UserControl
{
    private const int MaxTopLevelThemeMenuItems = 8;

    private readonly IPresentationProjectService _projects;
    private readonly IPresentationItemActionService _presentationActions;
    private readonly IThemeLibraryService _themeLibrary;
    private readonly IThemeApplicationService _themeApplier;
    private readonly ISettingsService _settings;
    private readonly IBundleAssetCacheService _assetCache;
    private readonly IQuickEditTextLayerService _quickEditTextLayers;
    private readonly ISlideItemActionService _slideItemActions;
    private readonly ISlideClipboardService _slideClipboard;
    private readonly ISlideTextStyleClipboardService _textStyleClipboard;
    private readonly IMediaLibraryService _mediaLibrary;
    private readonly IShowTimerService _timers;
    private readonly ILogger<ShowDeckView> _logger;

    private Flyout? _activeQuickEditFlyout;
    private ShowSlideDeckItem? _quickEditLivePreviewItem;
    private bool _quickEditLivePreviewEnqueuePending;

    private Flyout? _transitionFlyout;
    private TransitionPickerDialogContent? _transitionFlyoutContent;
    private Button? _transitionFlyoutRemoveButton;

    private ShowPresentationDeckSection? _autoAdvanceFlyoutSection;
    private ShowSlideDeckItem? _contextMenuSlideItem;
    private FrameworkElement? _contextMenuSlideTarget;

    public ShowDeckView()
    {
        InitializeComponent();

        var services = App.Services;
        _projects = services.GetRequiredService<IPresentationProjectService>();
        _presentationActions = services.GetRequiredService<IPresentationItemActionService>();
        _themeLibrary = services.GetRequiredService<IThemeLibraryService>();
        _themeApplier = services.GetRequiredService<IThemeApplicationService>();
        _settings = services.GetRequiredService<ISettingsService>();
        _assetCache = services.GetRequiredService<IBundleAssetCacheService>();
        _quickEditTextLayers = services.GetRequiredService<IQuickEditTextLayerService>();
        _slideItemActions = services.GetRequiredService<ISlideItemActionService>();
        _slideClipboard = services.GetRequiredService<ISlideClipboardService>();
        _textStyleClipboard = services.GetRequiredService<ISlideTextStyleClipboardService>();
        _mediaLibrary = services.GetRequiredService<IMediaLibraryService>();
        _timers = services.GetRequiredService<IShowTimerService>();
        _logger = services.GetRequiredService<ILogger<ShowDeckView>>();
    }

    public ShowViewModel? ViewModel
    {
        get => (ShowViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ShowViewModel),
            typeof(ShowDeckView),
            new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>Bound view model for code-behind (ShowPage sets before UI interaction).</summary>
    private ShowViewModel Vm => ViewModel ?? throw new InvalidOperationException($"{nameof(ViewModel)} is not set.");

    /// <summary>Hidden focus target owned by ShowPage for keyboard slide navigation.</summary>
    public Control? KeyboardFocusTarget
    {
        get => (Control?)GetValue(KeyboardFocusTargetProperty);
        set => SetValue(KeyboardFocusTargetProperty, value);
    }

    public static readonly DependencyProperty KeyboardFocusTargetProperty =
        DependencyProperty.Register(
            nameof(KeyboardFocusTarget),
            typeof(Control),
            typeof(ShowDeckView),
            new PropertyMetadata(null));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ShowDeckView view)
            return;

        if (e.OldValue is ShowViewModel oldVm)
            oldVm.BrowseStackScrollToSelectionRequested -= view.OnBrowseStackScrollToSelectionRequested;

        if (e.NewValue is ShowViewModel newVm)
            newVm.BrowseStackScrollToSelectionRequested += view.OnBrowseStackScrollToSelectionRequested;
    }

    internal void OnThemeChanged()
    {
        ViewModel?.NotifySlideDeckThemeChromeChanged();
        if (ViewModel is not null)
            App.Services?.GetService<IShowDeckThumbnailService>()?.OnThemeChanged(ViewModel);
    }

    public void RestoreKeyboardFocus() =>
        KeyboardFocusTarget?.Focus(FocusState.Programmatic);

    public async Task<bool> TryHandlePreviewKeyDownAsync(KeyRoutedEventArgs e)
    {
        if (ViewModel is null)
            return false;

        if (await TryHandleSelectedSlideShortcutAsync(e.Key))
        {
            BringSelectedSlideDeckItemIntoView();
            RestoreKeyboardFocus();
            return true;
        }

        if (IsSlideSeekKey(e.Key))
        {
            await ViewModel.StartSlideSeekAsync(e.Key);
            BringSelectedSlideDeckItemIntoView();
            RestoreKeyboardFocus();
            return true;
        }

        if (!await ViewModel.HandleKeyAsync(e.Key))
            return false;

        BringSelectedSlideDeckItemIntoView();
        RestoreKeyboardFocus();
        return true;
    }

    public bool TryHandlePreviewKeyUp(KeyRoutedEventArgs e)
    {
        if (ViewModel is null || !IsSlideSeekKey(e.Key))
            return false;

        ViewModel.StopSlideSeek();
        RestoreKeyboardFocus();
        return true;
    }

    public static bool IsPointerInsideSlideCard(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: ShowSlideDeckItem })
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    public void ClearSlideSelectionIfOutsideDeck(PointerRoutedEventArgs e)
    {
        if (ViewModel is null)
            return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (IsPointerInsideSlideCard(e.OriginalSource as DependencyObject))
            return;

        ViewModel.ClearSlideSelection();
        RestoreKeyboardFocus();
    }

    private void DeckRoot_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        ClearSlideSelectionIfOutsideDeck(e);

    private void SlideDeckItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(MediaDrawerView.MediaItemDragFormat))
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
        if (ViewModel is null)
            return;

        if (sender is not FrameworkElement { Tag: ShowSlideDeckItem slideItem })
            return;
        if (!e.DataView.Contains(MediaDrawerView.MediaItemDragFormat))
            return;

        var itemId = await e.DataView.GetDataAsync(MediaDrawerView.MediaItemDragFormat) as string;
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
}
