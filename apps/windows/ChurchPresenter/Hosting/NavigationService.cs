using System;

using ChurchPresenter.Features.Show.Shell;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace ChurchPresenter.Hosting;

/// <summary>
/// Owns top-level workspace navigation for the shell. All top-level pages, including Show, route
/// through a content <see cref="Frame"/> with native <see cref="SlideNavigationTransitionInfo"/>.
/// </summary>
public interface INavigationService
{
    void Initialize(Frame contentFrame);

    Type? CurrentPageType { get; }

    object? CurrentPage { get; }

    event EventHandler? Navigated;

    bool Navigate(Type pageType, NavigationTransitionInfo? transition = null);
}

internal sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    private bool _isNavigating;
    private Type? _currentPageType;
    private object? _currentPage;

    public Type? CurrentPageType => _currentPageType;

    public object? CurrentPage => _currentPage;

    public event EventHandler? Navigated;

    public void Initialize(Frame contentFrame)
    {
        _frame = contentFrame ?? throw new ArgumentNullException(nameof(contentFrame));
        _frame.Navigating += Frame_Navigating;
        _frame.Navigated += Frame_Navigated;
        _frame.NavigationFailed += Frame_NavigationFailed;
    }

    public bool Navigate(Type pageType, NavigationTransitionInfo? transition = null)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (_frame is null)
            throw new InvalidOperationException($"{nameof(NavigationService)}.{nameof(Initialize)} must be called before navigating.");

        if (_isNavigating || _currentPageType == pageType)
            return false;

        int navOp = ShowNavigationDiagnostics.BeginNavigationOp(
            $"from={_currentPageType?.Name ?? "null"} to={pageType.Name} transition={ShellNavigation.Transitions.Describe(transition ?? new SuppressNavigationTransitionInfo())}");

        try
        {
            _isNavigating = true;

            if (_frame.Content is Page outgoingPage)
            {
                TeardownOutgoingPageBeforeNavigate(outgoingPage, navOp, pageType);
            }

            NavigationTransitionInfo frameTransition =
                transition ?? ShellNavigation.Transitions.ForSiblingIndex(previousIndex: 0, currentIndex: 1);
            ShowNavigationDiagnostics.Log(
                "NavigateFrame",
                $"op={navOp} to={pageType.Name} transition={ShellNavigation.Transitions.Describe(frameTransition)}");

            if (!NavigateFrameContent(pageType, frameTransition))
            {
                ShowNavigationDiagnostics.Log("NavigateFrame", $"op={navOp} NavigateToType returned false");
                return false;
            }

            _currentPage = _frame.Content;
            _currentPageType = pageType;
            Navigated?.Invoke(this, EventArgs.Empty);
            ShowNavigationDiagnostics.EndNavigationOp(navOp, $"content={_currentPage?.GetType().Name}");
            return true;
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void Frame_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        _ = sender;
        ShowNavigationDiagnostics.Log(
            "Frame.Navigating",
            $"source={e.SourcePageType?.Name ?? "null"} mode={e.NavigationMode} cancel={e.Cancel}");
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        _ = sender;
        ShowNavigationDiagnostics.Log(
            "Frame.Navigated",
            $"content={e.Content?.GetType().Name ?? "null"} source={e.SourcePageType?.Name ?? "null"} mode={e.NavigationMode}");
    }

    private void Frame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        _ = sender;
        ShowNavigationDiagnostics.Log("Frame.NavigationFailed", $"{e.SourcePageType.Name}: {e.Exception}");
    }

    private static void TeardownOutgoingPageBeforeNavigate(Page outgoingPage, int navOp, Type targetPageType)
    {
        if (outgoingPage is Views.ShowPage outgoingShow)
        {
            ShowNavigationDiagnostics.Log(
                "TeardownBeforeNavigate",
                $"op={navOp} leaving Show hash={outgoingShow.GetHashCode()} target={targetPageType.Name}",
                ((IShowNavigationLifecycleHost)outgoingShow).DiagnosticsInstanceId);
            outgoingShow.TeardownForNavigation();
            return;
        }

        if (outgoingPage is Views.EditPage or Views.ReflowPage or Views.ThemesPage)
        {
            ShowNavigationDiagnostics.Log(
                "TeardownBeforeNavigate",
                $"op={navOp} leaving {outgoingPage.GetType().Name} target={targetPageType.Name}");
            NavigationTeardown.TeardownCompositionSurfaces(outgoingPage);
        }
    }

    private bool NavigateFrameContent(Type pageType, NavigationTransitionInfo transition)
    {
        var navOptions = new FrameNavigationOptions
        {
            TransitionInfoOverride = transition,
            IsNavigationStackEnabled = false,
        };

        return _frame!.NavigateToType(pageType, null, navOptions);
    }
}
