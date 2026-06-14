using ChurchPresenter.Features.Show.ShowControls.Pages;

using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Features.Show.ShowControls;

/// <summary>
/// Show Controls SelectorBar routes and horizontal slide transitions (Microsoft Learn SelectorBar + Frame pattern).
/// </summary>
internal static class ShowControlsNavigation
{
    internal sealed class Route
    {
        private Route(string tag, Type pageType)
        {
            Tag = tag;
            PageType = pageType;
        }

        public string Tag { get; }

        public Type PageType { get; }

        public static readonly Route Audio = new("audio", typeof(ShowControlsAudioPage));
        public static readonly Route Stage = new("stage", typeof(ShowControlsStagePage));
        public static readonly Route Timers = new("timers", typeof(ShowControlsTimersPage));
        public static readonly Route Messages = new("messages", typeof(ShowControlsMessagesPage));
        public static readonly Route Props = new("props", typeof(ShowControlsPropsPage));
        public static readonly Route Macros = new("macros", typeof(ShowControlsMacrosPage));

        public static IReadOnlyList<Route> All { get; } =
        [
            Audio,
            Stage,
            Timers,
            Messages,
            Props,
            Macros,
        ];

        public static Route Resolve(string tag)
        {
            foreach (Route route in All)
            {
                if (string.Equals(route.Tag, tag, StringComparison.Ordinal))
                    return route;
            }

            return Audio;
        }

        public static int IndexOf(string tag)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Tag, tag, StringComparison.Ordinal))
                    return i;
            }

            return 0;
        }
    }

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
    }
}
