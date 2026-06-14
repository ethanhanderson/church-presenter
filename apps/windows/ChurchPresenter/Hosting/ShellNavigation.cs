using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Hosting;

/// <summary>
/// Top-level SelectorBar routes, horizontal slide transitions, and nested-frame back navigation.
/// Follows Microsoft Learn: SelectorBar SelectionChanged + Frame with
/// <see cref="SlideNavigationTransitionInfo"/> and <c>IsNavigationStackEnabled=false</c>.
/// </summary>
internal static class ShellNavigation
{
    /// <summary>SelectorBar item <c>Tag</c> values and content-frame page types (left-to-right bar order).</summary>
    internal sealed class Route
    {
        private Route(string tag, Type pageType)
        {
            Tag = tag;
            PageType = pageType;
        }

        public string Tag { get; }

        public Type PageType { get; }

        public static readonly Route Show = new("Show", typeof(ShowPage));
        public static readonly Route Editor = new("EditorPage", typeof(EditPage));
        public static readonly Route Reflow = new("Reflow", typeof(ReflowPage));
        public static readonly Route ThemeLibrary = new("ThemeLibraryPage", typeof(ThemesPage));
        public static readonly Route Settings = new("Settings", typeof(SettingsPage));

        public static IReadOnlyList<Route> TopLevel { get; } =
        [
            Show,
            Editor,
            Reflow,
            ThemeLibrary,
            Settings,
        ];

        public static IReadOnlyList<Type> ActivePageTypes { get; } =
        [
            typeof(ShowPage),
            typeof(EditPage),
            typeof(ReflowPage),
            typeof(ThemesPage),
            typeof(SettingsPage),
            typeof(SettingsCategoryListPage),
            typeof(SettingsOutputPage),
            typeof(SettingsShowDetailPage),
            typeof(SettingsEditorDetailPage),
            typeof(SettingsReflowDetailPage),
            typeof(SettingsLibraryLocationPage),
            typeof(SettingsIntegrationsDetailPage),
            typeof(SettingsAppearanceDetailPage),
            typeof(OutputPage),
            typeof(StageOutputPage),
        ];

        public static Route Resolve(string tag) =>
            TopLevel.FirstOrDefault(route => string.Equals(route.Tag, tag, StringComparison.Ordinal))
            ?? Show;

        public static int IndexOf(string tag)
        {
            for (int i = 0; i < TopLevel.Count; i++)
            {
                if (string.Equals(TopLevel[i].Tag, tag, StringComparison.Ordinal))
                    return i;
            }

            return 0;
        }

        public static int IndexOf(Route route) => IndexOf(route.Tag);

        /// <summary>Maps the content frame's current page type to a SelectorBar tag, if any.</summary>
        public static string? TagForPageType(Type? pageType)
        {
            if (pageType is null)
                return null;

            foreach (Route route in TopLevel)
            {
                if (route.PageType == pageType)
                    return route.Tag;
            }

            return null;
        }
    }

    /// <summary>
    /// Horizontal slide for sibling SelectorBar tabs (WinUI Gallery / Microsoft Learn SelectorBar sample).
    /// </summary>
    internal static class Transitions
    {
        public static SlideNavigationTransitionInfo ForSiblingIndex(int previousIndex, int currentIndex) =>
            new()
            {
                Effect = currentIndex > previousIndex
                    ? SlideNavigationTransitionEffect.FromRight
                    : SlideNavigationTransitionEffect.FromLeft,
            };

        public static SlideNavigationTransitionInfo ForSiblingTag(string fromTag, string toTag) =>
            ForSiblingIndex(Route.IndexOf(fromTag), Route.IndexOf(toTag));

        public static string Describe(NavigationTransitionInfo transition) =>
            transition is SlideNavigationTransitionInfo slide
                ? $"SlideNavigationTransitionInfo({slide.Effect})"
                : transition.GetType().Name;
    }

    /// <summary>
    /// Finds nested <see cref="Frame"/> instances under the shell content frame and routes title-bar back.
    /// </summary>
    internal static class FrameBack
    {
        public static IEnumerable<Frame> EnumerateAllFrames(Frame shellRootFrame)
        {
            foreach (var f in EnumerateFramesPreOrder(shellRootFrame))
                yield return f;
        }

        public static bool HasAnyFrameCanGoBack(Frame shellRootFrame)
        {
            foreach (var frame in EnumerateFramesPreOrder(shellRootFrame))
            {
                if (frame.CanGoBack)
                    return true;
            }

            return false;
        }

        public static bool TryGoBackDeepest(Frame shellRootFrame)
        {
            foreach (var frame in FramesSortedDeepestFirst(shellRootFrame))
            {
                if (!frame.CanGoBack)
                    continue;

                if (ReferenceEquals(frame, shellRootFrame))
                    shellRootFrame.GoBack();
                else
                    frame.GoBack();

                return true;
            }

            return false;
        }

        private static IEnumerable<Frame> FramesSortedDeepestFirst(Frame shellRootFrame)
        {
            var withDepth = new List<(Frame Frame, int Depth)>();
            CollectFramesWithDepth(shellRootFrame, 0, withDepth);
            return withDepth.OrderByDescending(t => t.Depth).Select(t => t.Frame);
        }

        private static void CollectFramesWithDepth(DependencyObject? node, int depth, List<(Frame Frame, int Depth)> list)
        {
            if (node is null)
                return;

            if (node is Frame f)
                list.Add((f, depth));

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                CollectFramesWithDepth(VisualTreeHelper.GetChild(node, i), depth + 1, list);
        }

        private static IEnumerable<Frame> EnumerateFramesPreOrder(DependencyObject? node)
        {
            if (node is null)
                yield break;

            if (node is Frame f)
                yield return f;

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                foreach (var nested in EnumerateFramesPreOrder(VisualTreeHelper.GetChild(node, i)))
                    yield return nested;
            }
        }
    }
}
