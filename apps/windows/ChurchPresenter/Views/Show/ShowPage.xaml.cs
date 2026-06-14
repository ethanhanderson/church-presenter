using System;
using System.Threading.Tasks;

using ChurchPresenter.Features.Show.Deck;
using ChurchPresenter.Features.Show.Output;
using ChurchPresenter.Features.Show.Shell;
using ChurchPresenter.Features.Show.Sidebar;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Views;

public sealed partial class ShowPage : Page, IShowNavigationLifecycleHost
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ShowPage> _logger;
    private readonly ShowNavigationLifecycle _navigationLifecycle;

    public ShowViewModel ViewModel { get; }

    public ShowPage()
        : this(App.Services)
    {
    }

    private ShowPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ViewModel = services.GetRequiredService<ShowViewModel>();
        _navigationLifecycle = new ShowNavigationLifecycle(this);
        _settings = services.GetRequiredService<ISettingsService>();
        _logger = services.GetRequiredService<ILogger<ShowPage>>();
        InitializeComponent();
        WireShowSidebarHost();
        WireShowDeckHost();
        WireOutputColumnHost();
        WireMediaDrawerHost();
        DataContext = ViewModel;
        Loaded += ShowPage_Loaded;
        Unloaded += ShowPage_Unloaded;
        PreviewKeyDown += ShowPage_KeyDown;
        PreviewKeyUp += ShowPage_KeyUp;
        ActualThemeChanged += ShowPage_ActualThemeChanged;
    }

    private void ShowPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= ShowPage_Unloaded;
        PreviewKeyUp -= ShowPage_KeyUp;
        ViewModel.PropertyChanged -= ViewModel_MediaPanelPropertyChanged;
        ViewModel.StopSlideSeek();
        _navigationLifecycle.OnUnloaded();
    }

    private void ShowPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ShowDeck?.OnThemeChanged();
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
        OutputColumn?.SyncOutputPanelColumnWidth();
        UpdateMediaPanelLayout();
        ShowSidebar?.ScheduleSourcesNavigationRefreshFromHost();
        _navigationLifecycle.OnLoaded();
    }

    private async void ShowPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || ShouldIgnoreShowPageKey(e.OriginalSource as DependencyObject))
            return;

        if (ShowDeck is not null && await ShowDeck.TryHandlePreviewKeyDownAsync(e))
        {
            e.Handled = true;
            return;
        }
    }

    private void ShowPage_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || ShouldIgnoreShowPageKey(e.OriginalSource as DependencyObject))
            return;

        if (ShowDeck?.TryHandlePreviewKeyUp(e) == true)
            e.Handled = true;
    }

    private void LayoutRoot_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        ShowDeck?.ClearSlideSelectionIfOutsideDeck(e);

    private static bool ShouldIgnoreShowPageKey(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TextBox or AutoSuggestBox or RichEditBox)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    public void RestoreKeyboardFocus() =>
        KeyboardFocusTarget.Focus(FocusState.Programmatic);

    public void FocusSourcesSearch() => ShowSidebar?.FocusSourcesSearchFromHost();

    private void ApplyPanelChromeStates()
    {
        OutputColumn?.ApplyPanelChromeState();
        MediaDrawer?.ApplyPanelChromeState();
    }

    private void WireShowSidebarHost()
    {
        if (ShowSidebar == null)
            return;

        ShowSidebar.RestoreKeyboardFocus = RestoreKeyboardFocus;
        ShowSidebar.DropOverlayRoot = LayoutRoot;
        ShowSidebar.PresentationDragPreviewHost = PresentationDragPreviewHost;
        if (ShowSidebar.ViewModel == null)
            ShowSidebar.ViewModel = ViewModel;
    }

    private void WireShowDeckHost()
    {
        if (ShowDeck == null)
            return;

        if (ShowDeck.ViewModel == null)
            ShowDeck.ViewModel = ViewModel;

        if (ShowDeck.KeyboardFocusTarget == null)
            ShowDeck.KeyboardFocusTarget = KeyboardFocusTarget;
    }

    private void WireOutputColumnHost()
    {
        if (OutputColumn == null)
            return;

        OutputColumn.HostColumnDefinition = OutputPanelColumn;
        OutputColumn.LayoutCoordinateRoot = LayoutRoot;
        if (OutputColumn.ViewModel == null)
            OutputColumn.ViewModel = ViewModel;
    }

    private void WireMediaDrawerHost()
    {
        if (MediaDrawer == null)
            return;

        MediaDrawer.HostContentRow = MediaPanelContentRow;
        MediaDrawer.LayoutCoordinateRoot = LayoutRoot;
        if (MediaDrawer.ViewModel == null)
            MediaDrawer.ViewModel = ViewModel;
    }

    private void ViewModel_MediaPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShowViewModel.MediaPanelOpen) or nameof(ShowViewModel.MediaPanelHeight))
            UpdateMediaPanelLayout();
    }

    private void UpdateMediaPanelLayout() => MediaDrawer?.UpdatePanelLayout();
}
