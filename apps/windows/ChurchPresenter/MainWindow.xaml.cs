using System;
using System.Collections.Generic;

using ChurchPresenter.Hosting;
using ChurchPresenter.Interop;

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace ChurchPresenter;

public sealed partial class MainWindow : Window
{
    private readonly ShowViewModel _show;
    private readonly IAudienceWindowService _audienceWindows;
    private readonly IStageWindowService _stageWindows;
    private bool _isNavigatingContentFrame;
    private bool _audienceTitleToggleSync;
    private bool _stageTitleToggleSync;
    private bool _selectorBarSelectionSync;
    private string _currentNavTag = AppNavigationRoute.Show.Tag;
    private readonly List<Frame> _frameNavSubscribed = new();
    private readonly DispatcherQueueTimer _frameAttachDebounceTimer;

    public MainWindow(
        ShowViewModel show,
        IAudienceWindowService audienceWindows,
        IStageWindowService stageWindows)
    {
        InitializeComponent();

        _show = show ?? throw new ArgumentNullException(nameof(show));
        _audienceWindows = audienceWindows ?? throw new ArgumentNullException(nameof(audienceWindows));
        _stageWindows = stageWindows ?? throw new ArgumentNullException(nameof(stageWindows));
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

        SyncAppPageSelectorBarSelection();
        UpdatePresentationWorkspaceSelectorItems();
    }

    private void MainWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        App.MainWindow = null;
        _show.PropertyChanged -= ShowViewModel_PropertyChanged;
        ContentFrame.LayoutUpdated -= ContentFrame_LayoutUpdated;
        _frameAttachDebounceTimer.Stop();
        DetachFrameNavigatedHandlers();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AttachFrameNavigatedHandlers();
        ScheduleFrameAttachDebounced();
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
        foreach (var frame in NavigationFrameBackHelper.EnumerateAllFrames(ContentFrame))
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
        AppTitleBar.IsBackButtonEnabled = NavigationFrameBackHelper.HasAnyFrameCanGoBack(ContentFrame);

    private void AppTitleBar_BackRequested(TitleBar sender, object args) =>
        NavigationFrameBackHelper.TryGoBackDeepest(ContentFrame);

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
            await LeavePresentationWorkspaceIfUnavailableAsync();
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
        ContentFrame.Focus(FocusState.Programmatic);

        if (ContentFrame.Content is ShowPage showPage)
            showPage.RestoreKeyboardFocus();
    }

    private async void AudienceTitleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_audienceTitleToggleSync)
            return;
        if (sender is ToggleSwitch sw)
        {
            if (sw.IsOn)
                _audienceWindows.Open();
            else
                _audienceWindows.CloseAll();

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
        if (!string.Equals(_currentNavTag, AppNavigationRoute.Show.Tag, StringComparison.Ordinal))
            NavigateToTag(AppNavigationRoute.Show.Tag);

        if (ContentFrame.Content is ShowPage showPage)
            showPage.FocusSourcesSearch();
    }

    private async void StageTitleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_stageTitleToggleSync)
            return;
        if (sender is ToggleSwitch sw)
        {
            if (sw.IsOn)
                _stageWindows.Open();
            else
                _stageWindows.CloseAll();

            await _show.SetStageEnabledAsync(sw.IsOn);
            RestoreForegroundFocus();
        }
    }

    public void NavigateToShowPage()
    {
        NavigateToTag(AppNavigationRoute.Show.Tag);
    }

    public void NavigateToEditorPage()
    {
        if (!CanNavigateToPresentationWorkspace(AppNavigationRoute.Editor.Tag))
            return;

        NavigateToTag(AppNavigationRoute.Editor.Tag);
    }

    public void NavigateToReflowPage()
    {
        if (!CanNavigateToPresentationWorkspace(AppNavigationRoute.Reflow.Tag))
            return;

        NavigateToTag(AppNavigationRoute.Reflow.Tag);
    }

    public void NavigateToThemeLibraryPage()
    {
        NavigateToTag(AppNavigationRoute.ThemeLibrary.Tag);
    }

    private async void AppPageSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_selectorBarSelectionSync)
            return;

        if (sender.SelectedItem is not FrameworkElement { Tag: string tag })
            return;

        if (!await CanNavigateToTagAsync(tag))
        {
            SyncAppPageSelectorBarSelection();
            return;
        }

        NavigateToTag(tag);
    }

    private async Task<bool> CanNavigateToTagAsync(string targetTag)
    {
        if (string.Equals(targetTag, _currentNavTag, StringComparison.Ordinal))
            return false;

        if (!CanNavigateToPresentationWorkspace(targetTag))
            return false;

        if (string.Equals(_currentNavTag, AppNavigationRoute.Editor.Tag, StringComparison.Ordinal)
            && ContentFrame.Content is EditPage editPage)
            return await editPage.ViewModel.ConfirmNavigateAwayAsync();

        return true;
    }

    private void NavigateToPage(Type pageType, NavigationTransitionInfo? transition)
    {
        if (_isNavigatingContentFrame || ContentFrame.CurrentSourcePageType == pageType)
            return;

        var navOptions = new FrameNavigationOptions
        {
            TransitionInfoOverride = transition ?? new EntranceNavigationTransitionInfo(),
            IsNavigationStackEnabled = false,
        };

        try
        {
            _isNavigatingContentFrame = true;
            ContentFrame.NavigateToType(pageType, null, navOptions);
        }
        finally
        {
            _isNavigatingContentFrame = false;
        }
    }

    private void NavigateToTag(string tag)
    {
        AppNavigationRoute route = AppNavigationRoute.Resolve(tag);

        _currentNavTag = route.Tag;
        SyncAppPageSelectorBarSelection();
        UpdateShowSpecificControls();
        NavigateToPage(route.PageType, null);
    }

    private void SyncAppPageSelectorBarSelection()
    {
        SelectorBarItem? target = _currentNavTag switch
        {
            var tag when string.Equals(tag, AppNavigationRoute.Show.Tag, StringComparison.Ordinal) => ShowSelectorItem,
            var tag when string.Equals(tag, AppNavigationRoute.Editor.Tag, StringComparison.Ordinal) => EditorSelectorItem,
            var tag when string.Equals(tag, AppNavigationRoute.Reflow.Tag, StringComparison.Ordinal) => ReflowSelectorItem,
            var tag when string.Equals(tag, AppNavigationRoute.ThemeLibrary.Tag, StringComparison.Ordinal) => ThemeLibrarySelectorItem,
            var tag when string.Equals(tag, AppNavigationRoute.Settings.Tag, StringComparison.Ordinal) => SettingsSelectorItem,
            _ => null,
        };

        if (target == null || ReferenceEquals(AppPageSelectorBar.SelectedItem, target))
            return;

        _selectorBarSelectionSync = true;
        try
        {
            AppPageSelectorBar.SelectedItem = target;
        }
        finally
        {
            _selectorBarSelectionSync = false;
        }
    }

    private void UpdateShowSpecificControls()
    {
        var isShow = string.Equals(_currentNavTag, AppNavigationRoute.Show.Tag, StringComparison.Ordinal);
        MediaPanelToggleButton.Visibility = isShow ? Visibility.Visible : Visibility.Collapsed;
        UpdatePresentationWorkspaceSelectorItems();
        if (isShow)
        {
            SyncMediaPanelToggleButton();
            SyncLooksTitleButton();
        }
    }

    private bool CanNavigateToPresentationWorkspace(string targetTag)
    {
        if (!IsPresentationWorkspaceTag(targetTag))
            return true;

        if (_show.CanUsePresentationWorkspace)
            return true;

        _show.StatusMessage = "Open a presentation from Show before using Editor or Reflow.";
        return false;
    }

    private static bool IsPresentationWorkspaceTag(string tag) =>
        string.Equals(tag, AppNavigationRoute.Editor.Tag, StringComparison.Ordinal)
        || string.Equals(tag, AppNavigationRoute.Reflow.Tag, StringComparison.Ordinal);

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

    private async Task LeavePresentationWorkspaceIfUnavailableAsync()
    {
        if (_show.CanUsePresentationWorkspace || !IsPresentationWorkspaceTag(_currentNavTag))
            return;

        if (!await CanNavigateToTagAsync(AppNavigationRoute.Show.Tag))
        {
            SyncAppPageSelectorBarSelection();
            return;
        }

        NavigateToTag(AppNavigationRoute.Show.Tag);
    }

    private void SyncMediaPanelToggleButton()
    {
        if (MediaPanelToggleButton == null)
            return;
        var isOpen = _show.MediaPanelOpen;
        var resources = Application.Current.Resources;
        var primaryTextBrush = TryGetBrush(resources, "TextFillColorPrimaryBrush");
        if (isOpen)
        {
            MediaPanelToggleButton.Background = TryGetBrush(resources, "AccentFillColorDefaultBrush");
            MediaPanelToggleButton.Foreground = TryGetBrush(resources, "TextOnAccentFillColorPrimaryBrush")
                ?? TryGetBrush(resources, "TextOnAccentFillColorSecondaryBrush")
                ?? primaryTextBrush
                ?? new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            MediaPanelToggleButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            MediaPanelToggleButton.Foreground = primaryTextBrush
                ?? TryGetBrush(resources, "TextFillColorSecondaryBrush");
        }
    }

    private void MediaPanelToggle_Click(object sender, RoutedEventArgs e)
    {
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

    private static Brush? TryGetBrush(ResourceDictionary resources, string key) =>
        resources.TryGetValue(key, out var o) ? o as Brush : null;
}