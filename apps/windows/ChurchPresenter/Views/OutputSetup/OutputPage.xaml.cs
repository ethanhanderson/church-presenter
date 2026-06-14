using System;

using ChurchPresenter.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Views;

public sealed partial class OutputPage : Page, IOutputHostTargetAware, IOutputHostPreparer
{
    private const double CloseOverlayVisibleOpacity = 1d;
    private const double CloseOverlayHiddenOpacity = 0d;
    private static readonly TimeSpan CloseOverlayFadeDuration = TimeSpan.FromMilliseconds(140);

    /// <summary>Audience output surface host adapter wiring.</summary>
    public AudienceOutputViewModel ViewModel { get; }

    private readonly ShowViewModel _show;
    private OutputHostTarget _target = new("audience", "preview", string.Empty);
    private bool _closeChromeShown;
    private Storyboard? _closeChromeStoryboard;

    /// <summary>Activated only via DI (<see cref="ChurchPresenter.Services.Output.OutputWindowService"/>); not used with <see cref="Frame"/> navigation.</summary>
    public OutputPage(AudienceOutputViewModel viewModel, ShowViewModel show)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _show = show ?? throw new ArgumentNullException(nameof(show));
        InitializeComponent();
    }

    private async void OutputPage_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await PrepareForDisplayAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void SetOutputHostTarget(OutputHostTarget target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        ApplyOutputFrameSize();
        if (IsLoaded)
            _ = PrepareForDisplayAsync();
    }

    /// <inheritdoc />
    public async Task PrepareForDisplayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyOutputFrameSize();
        await ViewModel.AttachPreviewAsync(MirrorView, _target.ScreenId, cancellationToken).ConfigureAwait(true);
    }

    private void ApplyOutputFrameSize()
    {
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

    private void CloseAudienceHitArea_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ShowCloseChrome();
    }

    private async void CloseAudienceHitArea_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await _show.SetAudienceEnabledAsync(false).ConfigureAwait(true);
        if (App.MainWindow is MainWindow window)
            window.RestoreForegroundFocus();
    }

    /// <summary>
    /// Forwards keyboard navigation to the show view model when the output window has focus.
    /// Also immediately returns keyboard focus to the main control surface.
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
        if (_closeChromeShown) return;
        _closeChromeShown = true;
        CloseAudienceChrome.IsHitTestVisible = true;
        AnimateCloseChromeOpacity(CloseOverlayVisibleOpacity);
    }

    private void HideCloseChrome()
    {
        if (!_closeChromeShown) return;
        _closeChromeShown = false;
        AnimateCloseChromeOpacity(CloseOverlayHiddenOpacity);
    }

    private void AnimateCloseChromeOpacity(double targetOpacity)
    {
        double fromOpacity = CloseAudienceChrome.Opacity;
        _closeChromeStoryboard?.Stop();
        CloseAudienceChrome.Opacity = fromOpacity;

        var animation = new DoubleAnimation
        {
            From = fromOpacity,
            To = targetOpacity,
            Duration = new Duration(CloseOverlayFadeDuration),
            EasingFunction = TransitionAnimationHelper.BuildEasingFunction("ease-out"),
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, CloseAudienceChrome);
        Storyboard.SetTargetProperty(animation, nameof(Opacity));

        var sb = new Storyboard();
        sb.Children.Add(animation);

        if (targetOpacity <= CloseOverlayHiddenOpacity)
            sb.Completed += (_, _) => { if (!_closeChromeShown) CloseAudienceChrome.IsHitTestVisible = false; };

        _closeChromeStoryboard = sb;
        _closeChromeStoryboard.Begin();
    }
}
