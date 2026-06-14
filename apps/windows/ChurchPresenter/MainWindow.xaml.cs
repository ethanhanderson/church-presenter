using System;
using System.Collections.Generic;

using ChurchPresenter.Adapters.Rendering.Pipeline;
using ChurchPresenter.Hosting;
using ChurchPresenter.Interop;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChurchPresenter;

public sealed partial class MainWindow : Window
{
    /// <summary>App-lifetime canonical render anchor (for compositor resolution from preview mirrors).</summary>
    internal Panel RenderAnchorPanel => ProgramRenderAnchor;

    private readonly ShowViewModel _show;
    private readonly INavigationService _navigation;
    private readonly IShellNavigationCoordinator _shellNav;
    private bool _audienceTitleToggleSync;
    private bool _stageTitleToggleSync;
    private bool _mediaPanelToggleSync;
    private readonly List<Frame> _frameNavSubscribed = new();
    private readonly DispatcherQueueTimer _frameAttachDebounceTimer;

    /// <summary>Current top-level page in the content frame for diagnostics.</summary>
    internal string? CurrentContentPageTypeName => _navigation.CurrentPage?.GetType().Name;

    public MainWindow(
        ShowViewModel show,
        INavigationService navigation,
        IShellNavigationCoordinator shellNavigation,
        IProgramRenderService renderService,
        OperatorPreviewSession operatorPreviewSession)
    {
        InitializeComponent();

        _show = show ?? throw new ArgumentNullException(nameof(show));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _shellNav = shellNavigation ?? throw new ArgumentNullException(nameof(shellNavigation));
        ArgumentNullException.ThrowIfNull(renderService);
        ArgumentNullException.ThrowIfNull(operatorPreviewSession);
        ContentFrame.CacheSize = 8;
        _navigation.Initialize(ContentFrame);

        // Bind the app-lifetime render service to the always-composing shell anchor. Every logical output
        // screen's single canonical render lives here (never on a page); pages only host passive surface
        // mirrors of it.
        renderService.Initialize(ProgramRenderAnchor);
        operatorPreviewSession.InitializeOverlay(OperatorPreviewOverlayHost);

        _navigation.Navigated += NavigationService_Navigated;
        _show.PropertyChanged += ShowViewModel_PropertyChanged;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            // Match system caption button height to the tall title bar (see AppWindowTitleBar.PreferredHeightOption).
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }

        if (AppWindow.Presenter is OverlappedPresenter overlapped)
            overlapped.Maximize();
        else
        {
            var created = OverlappedPresenter.Create();
            AppWindow.SetPresenter(created);
            created.Maximize();
        }

        Closed += MainWindow_Closed;
        ContentFrame.Navigated += ContentFrame_Navigated;
        ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
        ContentFrame.LayoutUpdated += ContentFrame_LayoutUpdated;

        var dq = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DispatcherQueue is required for the main window.");
        _frameAttachDebounceTimer = dq.CreateTimer();
        _frameAttachDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
        _frameAttachDebounceTimer.IsRepeating = false;
        _frameAttachDebounceTimer.Tick += (_, _) =>
        {
            AttachFrameNavigatedHandlers();
            UpdateTitleBarBackButton();
        };

        _shellNav.AttachSelectorBar(AppPageSelectorBar);
        _shellNav.SyncSelectorBar();
        SyncAudienceTitleToggle();
        SyncStageTitleToggle();
        UpdatePresentationWorkspaceSelectorItems();
    }

    private void MainWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        App.MainWindow = null;
        _show.PropertyChanged -= ShowViewModel_PropertyChanged;
        _navigation.Navigated -= NavigationService_Navigated;
        ContentFrame.LayoutUpdated -= ContentFrame_LayoutUpdated;
        _frameAttachDebounceTimer.Stop();
        DetachFrameNavigatedHandlers();
    }

    private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        e.Handled = true;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Frame-specific wiring (nested-frame back handlers, back button). Chrome updates also run from
        // NavigationService_Navigated for pages that complete navigation through the service.
        AttachFrameNavigatedHandlers();
        ScheduleFrameAttachDebounced();
    }

    private void NavigationService_Navigated(object? sender, EventArgs e)
    {
        ApplyPostNavigationChromeUpdate();
    }

    private void ApplyPostNavigationChromeUpdate()
    {
        _show.NotifyAudienceOutputChanged();
        _show.NotifyStageOutputChanged();
        SyncAudienceTitleToggle();
        SyncStageTitleToggle();
        UpdateShowSpecificControls();
        UpdateTitleBarBackButton();
    }

    private void ContentFrame_LayoutUpdated(object? sender, object e) =>
        ScheduleFrameAttachDebounced();

    private void ScheduleFrameAttachDebounced()
    {
        _frameAttachDebounceTimer.Stop();
        _frameAttachDebounceTimer.Start();
    }

    private void FrameNavigatedHandler(object sender, NavigationEventArgs e)
    {
        UpdateTitleBarBackButton();
        ScheduleFrameAttachDebounced();
    }

    private void AttachFrameNavigatedHandlers()
    {
        DetachFrameNavigatedHandlers();
        foreach (var frame in ShellNavigation.FrameBack.EnumerateAllFrames(ContentFrame))
        {
            frame.Navigated += FrameNavigatedHandler;
            _frameNavSubscribed.Add(frame);
        }
    }

    private void DetachFrameNavigatedHandlers()
    {
        foreach (var f in _frameNavSubscribed)
            f.Navigated -= FrameNavigatedHandler;
        _frameNavSubscribed.Clear();
    }

    private void UpdateTitleBarBackButton() =>
        AppTitleBar.IsBackButtonEnabled = ShellNavigation.FrameBack.HasAnyFrameCanGoBack(ContentFrame);

    private void AppTitleBar_BackRequested(TitleBar sender, object args) =>
        ShellNavigation.FrameBack.TryGoBackDeepest(ContentFrame);

    private async void ShowViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShowViewModel.AudienceOutputEnabled))
            SyncAudienceTitleToggle();
        else if (e.PropertyName == nameof(ShowViewModel.StageOutputEnabled))
            SyncStageTitleToggle();
        else if (e.PropertyName == nameof(ShowViewModel.MediaPanelOpen))
            SyncMediaPanelToggleButton();
        else if (e.PropertyName == nameof(ShowViewModel.CanUsePresentationWorkspace))
        {
            UpdatePresentationWorkspaceSelectorItems();
            await _shellNav.LeavePresentationWorkspaceIfUnavailableAsync();
        }
    }

    private void SyncAudienceTitleToggle()
    {
        _audienceTitleToggleSync = true;
        try
        {
            AudienceTitleToggle.IsOn = _show.AudienceOutputEnabled;
        }
        finally
        {
            _audienceTitleToggleSync = false;
        }
    }

    private void SyncStageTitleToggle()
    {
        _stageTitleToggleSync = true;
        try
        {
            StageTitleToggle.IsOn = _show.StageOutputEnabled;
        }
        finally
        {
            _stageTitleToggleSync = false;
        }
    }

    /// <summary>Returns keyboard focus to the shell and, when showing the Show page, restores its keyboard surface focus.</summary>
    public void RestoreForegroundFocus()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeWindowInterop.SetForegroundWindow(hwnd);

        if (_navigation.CurrentPage is ShowPage showPage)
            showPage.RestoreKeyboardFocus();
        else
            ContentFrame.Focus(FocusState.Programmatic);
    }

    private async void AudienceTitleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_audienceTitleToggleSync)
            return;
        if (sender is ToggleSwitch sw)
        {
            await _show.SetAudienceEnabledAsync(sw.IsOn);
            RestoreForegroundFocus();
        }
    }

    private void SyncLooksTitleButton()
    {
        LooksTitleButton.IsEnabled = false;
        ToolTipService.SetToolTip(
            LooksTitleButton,
            "Looks are configured from the Settings output page.");
    }

    private void LooksTitleButton_Click(object sender, RoutedEventArgs e)
    {
        _show.StatusMessage = "Looks are configured from Settings output.";
    }

    private void SearchTitleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_shellNav.IsOnShow)
            _shellNav.TryNavigateTo(ShellNavigation.Route.Show.Tag);

        if (_navigation.CurrentPage is ShowPage showPage)
            showPage.FocusSourcesSearch();
    }

    private async void StageTitleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_stageTitleToggleSync)
            return;
        if (sender is ToggleSwitch sw)
        {
            await _show.SetStageEnabledAsync(sw.IsOn);
            RestoreForegroundFocus();
        }
    }

    public void NavigateToShowPage() =>
        _shellNav.TryNavigateTo(ShellNavigation.Route.Show.Tag);

    public void NavigateToEditorPage()
    {
        if (!_shellNav.CanNavigateToPresentationWorkspace(ShellNavigation.Route.Editor.Tag))
            return;

        _shellNav.TryNavigateTo(ShellNavigation.Route.Editor.Tag);
    }

    public void NavigateToReflowPage()
    {
        if (!_shellNav.CanNavigateToPresentationWorkspace(ShellNavigation.Route.Reflow.Tag))
            return;

        _shellNav.TryNavigateTo(ShellNavigation.Route.Reflow.Tag);
    }

    public void NavigateToThemeLibraryPage() =>
        _shellNav.TryNavigateTo(ShellNavigation.Route.ThemeLibrary.Tag);

    private async void AppPageSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!await _shellNav.HandleSelectorBarSelectionChangedAsync(sender))
            return;

        UpdateShowSpecificControls();
    }

    private void UpdateShowSpecificControls()
    {
        var isShow = _shellNav.IsOnShow;
        MediaPanelToggleButton.Visibility = isShow ? Visibility.Visible : Visibility.Collapsed;
        UpdatePresentationWorkspaceSelectorItems();
        if (isShow)
        {
            SyncMediaPanelToggleButton();
            SyncLooksTitleButton();
        }
    }

    private void UpdatePresentationWorkspaceSelectorItems()
    {
        var canUsePresentationWorkspace = _show.CanUsePresentationWorkspace;
        EditorSelectorItem.IsEnabled = canUsePresentationWorkspace;
        ReflowSelectorItem.IsEnabled = canUsePresentationWorkspace;

        var tooltip = canUsePresentationWorkspace
            ? "Open the selected Show presentation."
            : "Open a presentation from Show to enable this workspace.";
        ToolTipService.SetToolTip(EditorSelectorItem, tooltip);
        ToolTipService.SetToolTip(ReflowSelectorItem, tooltip);
    }

    private void SyncMediaPanelToggleButton()
    {
        if (MediaPanelToggleButton == null)
            return;

        _mediaPanelToggleSync = true;
        try
        {
            MediaPanelToggleButton.IsChecked = _show.MediaPanelOpen;
        }
        finally
        {
            _mediaPanelToggleSync = false;
        }
    }

    private void MediaPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPanelToggleSync)
            return;

        _show.ToggleMediaPanel();
        SyncMediaPanelToggleButton();
        RestoreForegroundFocus();
    }

    private async Task ShowStartupErrorAsync(string message)
    {
        if (Content is not FrameworkElement root || root.XamlRoot == null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Startup error",
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root.XamlRoot,
        };

        await dialog.ShowAsync();
    }

}