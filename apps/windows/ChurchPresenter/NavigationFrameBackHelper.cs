using System.Linq;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter;

/// <summary>
/// Finds <see cref="Frame"/> instances under the shell <see cref="Frame"/> and performs back on the deepest frame
/// that can go back (nested navigation first, then the shell frame).
/// </summary>
internal static class NavigationFrameBackHelper
{
    /// <summary>All <see cref="Frame"/> elements under <paramref name="shellRootFrame"/> (pre-order).</summary>
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

    /// <summary>
    /// Pops the deepest frame in the visual tree that has history (matches title-bar / system back expectations).
    /// </summary>
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