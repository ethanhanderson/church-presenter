using System;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.Foundation;

namespace ChurchPresenter.Views;

public sealed partial class SettingsOutputPage : Page
{
    private readonly DispatcherTimer _monitorRefreshTimer;
    private readonly IMonitorIdentifyService _monitorIdentify;

    // Tracks the hovered card border for each flyout row so we can show/hide the Identify button.
    private FrameworkElement? _audienceRowHoveredCard;
    private FrameworkElement? _stageRowHoveredCard;
    private bool _loadingClearGroups;

    public SettingsViewModel ViewModel { get; }

    public SettingsOutputPage()
        : this(App.Services)
    {
    }

    private SettingsOutputPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ViewModel = services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        _monitorIdentify = services.GetRequiredService<IMonitorIdentifyService>();
        _monitorRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _monitorRefreshTimer.Tick += (_, _) => ViewModel.RefreshMonitors();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadOutputSectionFromSettings();
        _loadingClearGroups = true;
        ViewModel.LoadClearGroupsFromRouting();
        _loadingClearGroups = false;
        _monitorRefreshTimer.Start();
        SettingsPageLayout.BindSettingsColumnWidth(this, SettingsColumnRoot);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _monitorRefreshTimer.Stop();
        _monitorIdentify.HideIdentifiers();
        _audienceRowHoveredCard = null;
        _stageRowHoveredCard = null;
    }

    // ── Audience card strip ───────────────────────────────────────────────────

    private void AudienceMonitorCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
            ViewModel.SelectAudienceMonitor(index);
    }

    // ── Audience picker flyout ────────────────────────────────────────────────

    private void AudienceMonitorOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
            ViewModel.SelectAudienceMonitor(index);
    }

    private void AudienceIdentifyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { Tag: int index })
            _monitorIdentify.ToggleIdentifier(index);
    }

    private void AudienceOutputDisplayFlyout_EscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        if (!_monitorIdentify.IsIdentificationActive)
            return;
        _monitorIdentify.HideIdentifiers();
        args.Handled = true;
    }

    private void AudienceOutputDisplayFlyout_Closed(object sender, object e)
    {
        _ = sender;
        _ = e;
        _audienceRowHoveredCard = null;
        _monitorIdentify.HideIdentifiers();
    }

    private void AudienceMonitorOptionRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = e;
        if (sender is not FrameworkElement row) return;
        if (row.FindName("AudienceIdentifyButton") is not Button identifyButton) return;

        if (_audienceRowHoveredCard is { } previous && !ReferenceEquals(previous, row))
        {
            if (previous.FindName("AudienceIdentifyButton") is Button previousIdentify)
                SetIdentifyButtonRevealed(previousIdentify, false);
        }

        _audienceRowHoveredCard = row;
        SetIdentifyButtonRevealed(identifyButton, true);
    }

    private void AudienceMonitorOptionRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement row) return;
        if (row.FindName("AudienceIdentifyButton") is not Button identifyButton) return;

        var dq = DispatcherQueue.GetForCurrentThread();
        _ = dq.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_audienceRowHoveredCard, row)) return;
            var pt = e.GetCurrentPoint(row).Position;
            var bounds = new Rect(0, 0, row.ActualWidth, row.ActualHeight);
            if (bounds.Contains(pt)) return;
            SetIdentifyButtonRevealed(identifyButton, false);
            if (ReferenceEquals(_audienceRowHoveredCard, row))
                _audienceRowHoveredCard = null;
        });
    }

    private void AudienceMonitorOptionRow_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _ = e;
        if (sender is not FrameworkElement row) return;
        if (row.FindName("AudienceIdentifyButton") is not Button identifyButton) return;
        if (!ReferenceEquals(_audienceRowHoveredCard, row)) return;
        SetIdentifyButtonRevealed(identifyButton, false);
        _audienceRowHoveredCard = null;
    }

    // ── Stage card strip ──────────────────────────────────────────────────────

    private void StageMonitorCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
            ViewModel.SelectStageMonitor(index);
    }

    // ── Stage picker flyout ───────────────────────────────────────────────────

    private void StageMonitorOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
            ViewModel.SelectStageMonitor(index);
    }

    private void StageIdentifyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { Tag: int index })
            _monitorIdentify.ToggleIdentifier(index);
    }

    private void StageOutputDisplayFlyout_EscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        if (!_monitorIdentify.IsIdentificationActive)
            return;
        _monitorIdentify.HideIdentifiers();
        args.Handled = true;
    }

    private void StageOutputDisplayFlyout_Closed(object sender, object e)
    {
        _ = sender;
        _ = e;
        _stageRowHoveredCard = null;
        _monitorIdentify.HideIdentifiers();
    }

    private void StageMonitorOptionRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = e;
        if (sender is not FrameworkElement row) return;
        if (row.FindName("StageIdentifyButton") is not Button identifyButton) return;

        if (_stageRowHoveredCard is { } previous && !ReferenceEquals(previous, row))
        {
            if (previous.FindName("StageIdentifyButton") is Button previousIdentify)
                SetIdentifyButtonRevealed(previousIdentify, false);
        }

        _stageRowHoveredCard = row;
        SetIdentifyButtonRevealed(identifyButton, true);
    }

    private void StageMonitorOptionRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement row) return;
        if (row.FindName("StageIdentifyButton") is not Button identifyButton) return;

        var dq = DispatcherQueue.GetForCurrentThread();
        _ = dq.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_stageRowHoveredCard, row)) return;
            var pt = e.GetCurrentPoint(row).Position;
            var bounds = new Rect(0, 0, row.ActualWidth, row.ActualHeight);
            if (bounds.Contains(pt)) return;
            SetIdentifyButtonRevealed(identifyButton, false);
            if (ReferenceEquals(_stageRowHoveredCard, row))
                _stageRowHoveredCard = null;
        });
    }

    private void StageMonitorOptionRow_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _ = e;
        if (sender is not FrameworkElement row) return;
        if (row.FindName("StageIdentifyButton") is not Button identifyButton) return;
        if (!ReferenceEquals(_stageRowHoveredCard, row)) return;
        SetIdentifyButtonRevealed(identifyButton, false);
        _stageRowHoveredCard = null;
    }

    // ── Clear groups ─────────────────────────────────────────────────────────

    private void LookRouteField_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_loadingClearGroups)
            return;

        _ = ViewModel.PersistLookRoutesAsync();
    }

    private void LookRouteField_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_loadingClearGroups)
            return;

        _ = ViewModel.PersistLookRoutesAsync();
    }

    private void ClearGroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
    }

    private void AddClearGroup_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = ViewModel.AddClearGroupAsync();
    }

    private void DeleteClearGroup_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = ViewModel.DeleteSelectedClearGroupAsync();
    }

    private void ClearGroupField_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_loadingClearGroups)
            return;

        _ = ViewModel.PersistClearGroupsAsync();
    }

    private void ClearGroupField_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_loadingClearGroups)
            return;

        _ = ViewModel.PersistClearGroupsAsync();
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static void SetIdentifyButtonRevealed(Button identifyButton, bool revealed)
    {
        if (revealed)
        {
            identifyButton.Opacity = 1;
            identifyButton.IsHitTestVisible = true;
        }
        else
        {
            identifyButton.Opacity = 0;
            identifyButton.IsHitTestVisible = false;
        }
    }
}