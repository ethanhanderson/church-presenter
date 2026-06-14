using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Hosting;

/// <summary>
/// SelectorBar-driven top-level navigation: route resolution, leave guards, slide direction, and bar sync.
/// </summary>
public interface IShellNavigationCoordinator
{
    string CurrentTag { get; }

    bool IsOnShow { get; }

    void AttachSelectorBar(SelectorBar selectorBar);

    bool TryNavigateTo(string tag);

    Task<bool> TryNavigateToAsync(string tag, int? selectorBarIndex = null);

    Task<bool> HandleSelectorBarSelectionChangedAsync(SelectorBar bar);

    void SyncSelectorBar();

    bool CanNavigateToPresentationWorkspace(string targetTag);

    Task LeavePresentationWorkspaceIfUnavailableAsync();
}

internal sealed class ShellNavigationCoordinator : IShellNavigationCoordinator
{
    private readonly INavigationService _navigation;
    private readonly ShowViewModel _show;
    private SelectorBar? _selectorBar;
    private string _currentTag = ShellNavigation.Route.Show.Tag;
    private int _selectedBarIndex;
    private bool _selectorBarSelectionSync;

    public ShellNavigationCoordinator(INavigationService navigation, ShowViewModel show)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _show = show ?? throw new ArgumentNullException(nameof(show));
    }

    public string CurrentTag => _currentTag;

    public bool IsOnShow => _navigation.CurrentPage is ShowPage;

    public void AttachSelectorBar(SelectorBar selectorBar) =>
        _selectorBar = selectorBar ?? throw new ArgumentNullException(nameof(selectorBar));

    public bool TryNavigateTo(string tag) =>
        TryNavigateToAsync(tag).GetAwaiter().GetResult();

    public async Task<bool> TryNavigateToAsync(string tag, int? selectorBarIndex = null)
    {
        ShellNavigation.Route route = ShellNavigation.Route.Resolve(tag);

        // Frame is authoritative: _currentTag defaults to Show before the first navigation completes.
        if (_navigation.CurrentPageType == route.PageType)
        {
            _currentTag = route.Tag;
            _selectedBarIndex = selectorBarIndex ?? ShellNavigation.Route.IndexOf(tag);
            SyncSelectorBar();
            return true;
        }

        if (!await CanNavigateAwayAsync(tag))
            return false;

        return CommitNavigate(tag, selectorBarIndex ?? ShellNavigation.Route.IndexOf(tag));
    }

    public async Task<bool> HandleSelectorBarSelectionChangedAsync(SelectorBar bar)
    {
        if (_selectorBarSelectionSync)
            return false;

        if (bar.SelectedItem is not SelectorBarItem { Tag: string tag })
            return false;

        int newIndex = bar.Items.IndexOf(bar.SelectedItem);
        if (newIndex < 0)
            return false;

        if (!await CanNavigateAwayAsync(tag))
        {
            SyncSelectorBar();
            return false;
        }

        return CommitNavigate(tag, newIndex);
    }

    public void SyncSelectorBar()
    {
        if (_selectorBar is null)
            return;

        SelectorBarItem? target = null;
        for (int i = 0; i < _selectorBar.Items.Count; i++)
        {
            if (_selectorBar.Items[i] is SelectorBarItem item
                && item.Tag is string tag
                && string.Equals(tag, _currentTag, StringComparison.Ordinal))
            {
                target = item;
                break;
            }
        }

        if (target == null || ReferenceEquals(_selectorBar.SelectedItem, target))
            return;

        _selectorBarSelectionSync = true;
        try
        {
            _selectorBar.SelectedItem = target;
        }
        finally
        {
            _selectorBarSelectionSync = false;
        }
    }

    public bool CanNavigateToPresentationWorkspace(string targetTag)
    {
        if (!IsPresentationWorkspaceTag(targetTag))
            return true;

        if (_show.CanUsePresentationWorkspace)
            return true;

        _show.StatusMessage = "Open a presentation from Show before using Editor or Reflow.";
        return false;
    }

    public async Task LeavePresentationWorkspaceIfUnavailableAsync()
    {
        string? activeTag = ShellNavigation.Route.TagForPageType(_navigation.CurrentPageType);
        if (_show.CanUsePresentationWorkspace
            || activeTag is null
            || !IsPresentationWorkspaceTag(activeTag))
            return;

        if (!await CanNavigateAwayAsync(ShellNavigation.Route.Show.Tag))
        {
            SyncSelectorBar();
            return;
        }

        CommitNavigate(ShellNavigation.Route.Show.Tag, ShellNavigation.Route.IndexOf(ShellNavigation.Route.Show));
    }

    private async Task<bool> CanNavigateAwayAsync(string targetTag)
    {
        ShellNavigation.Route targetRoute = ShellNavigation.Route.Resolve(targetTag);
        if (_navigation.CurrentPageType == targetRoute.PageType)
            return false;

        if (!CanNavigateToPresentationWorkspace(targetTag))
            return false;

        string? activeTag = ShellNavigation.Route.TagForPageType(_navigation.CurrentPageType);

        if (string.Equals(activeTag, ShellNavigation.Route.Editor.Tag, StringComparison.Ordinal)
            && _navigation.CurrentPage is EditPage editPage)
            return await editPage.ViewModel.ConfirmNavigateAwayAsync();

        if (string.Equals(activeTag, ShellNavigation.Route.Reflow.Tag, StringComparison.Ordinal)
            && _navigation.CurrentPage is ReflowPage reflowPage)
            return await reflowPage.ViewModel.ConfirmNavigateAwayAsync();

        if (string.Equals(activeTag, ShellNavigation.Route.ThemeLibrary.Tag, StringComparison.Ordinal)
            && _navigation.CurrentPage is ThemesPage themesPage)
            await themesPage.ViewModel.FlushPendingSaveAsync();

        if (_navigation.CurrentPage is ShowPage)
            _show.StopSlideSeek();

        return true;
    }

    private bool CommitNavigate(string tag, int newBarIndex)
    {
        ShellNavigation.Route route = ShellNavigation.Route.Resolve(tag);

        if (_navigation.CurrentPageType == route.PageType)
        {
            _currentTag = route.Tag;
            _selectedBarIndex = newBarIndex;
            SyncSelectorBar();
            return true;
        }

        NavigationTransitionInfo transition =
            ShellNavigation.Transitions.ForSiblingIndex(_selectedBarIndex, newBarIndex);

        if (!_navigation.Navigate(route.PageType, transition))
        {
            SyncSelectorBar();
            return false;
        }

        _currentTag = route.Tag;
        _selectedBarIndex = newBarIndex;
        SyncSelectorBar();
        return true;
    }

    private static bool IsPresentationWorkspaceTag(string tag) =>
        string.Equals(tag, ShellNavigation.Route.Editor.Tag, StringComparison.Ordinal)
        || string.Equals(tag, ShellNavigation.Route.Reflow.Tag, StringComparison.Ordinal);
}
