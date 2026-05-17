using System;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace ChurchPresenter.Views;

/// <summary>Stage output surface — shows the current live slide on the stage (operator) monitor.</summary>
public sealed partial class StageOutputPage : Page
{
    /// <summary>Stage output surface (shared <see cref="OutputViewModel"/> with live session).</summary>
    public OutputViewModel ViewModel { get; }

    private readonly ShowViewModel _show;

    private static readonly SolidColorBrush CloseCircleFillBase = new(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush CloseCircleFillHover = new(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

    /// <summary>Activated only via DI (<see cref="ChurchPresenter.Services.Output.OutputWindowService"/>); not used with <see cref="Frame"/> navigation.</summary>
    public StageOutputPage(StageOutputViewModel viewModel, ShowViewModel show)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _show = show ?? throw new ArgumentNullException(nameof(show));
        InitializeComponent();
        CloseCircle.Fill = CloseCircleFillBase;
    }

    private void PointerOverlay_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ShowCloseChrome();
    }

    private void PointerOverlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ShowCloseChrome();
    }

    private void PointerOverlay_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        HideCloseChrome();
    }

    private void CloseStageHitArea_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseCircle.Fill = CloseCircleFillHover;
    }

    private void CloseStageHitArea_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseCircle.Fill = CloseCircleFillBase;
    }

    private async void CloseStageHitArea_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await _show.SetStageEnabledAsync(false).ConfigureAwait(true);
        if (App.MainWindow is MainWindow window)
            window.RestoreForegroundFocus();
    }

    /// <summary>
    /// Forwards keyboard navigation to the show view model when the output window has focus.
    /// Also immediately returns keyboard focus to the main control surface.
    /// The close button on this page is a <see cref="Border"/> handled via <c>Tapped</c>,
    /// so it is not reachable by keyboard — only by pointer/touch.
    /// </summary>
    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        _ = sender;
        var handled = IsSlideSeekKey(e.Key)
            ? await _show.StartSlideSeekAsync(e.Key).ConfigureAwait(true)
            : await _show.HandleKeyAsync(e.Key).ConfigureAwait(true);
        if (handled)
        {
            e.Handled = true;
            if (App.MainWindow is MainWindow window)
                window.RestoreForegroundFocus();
        }
    }

    private void Page_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        _ = sender;
        if (!IsSlideSeekKey(e.Key))
            return;

        _show.StopSlideSeek();
        e.Handled = true;
        if (App.MainWindow is MainWindow window)
            window.RestoreForegroundFocus();
    }

    private static bool IsSlideSeekKey(Windows.System.VirtualKey key) =>
        key is Windows.System.VirtualKey.Right
            or Windows.System.VirtualKey.PageDown
            or Windows.System.VirtualKey.Left
            or Windows.System.VirtualKey.PageUp
            or Windows.System.VirtualKey.Back;

    private void ShowCloseChrome()
    {
        CloseStageChrome.Opacity = 1;
        CloseStageChrome.IsHitTestVisible = true;
        CloseCircle.Fill = CloseCircleFillBase;
    }

    private void HideCloseChrome()
    {
        CloseStageChrome.Opacity = 0;
        CloseStageChrome.IsHitTestVisible = false;
        CloseCircle.Fill = CloseCircleFillBase;
    }
}