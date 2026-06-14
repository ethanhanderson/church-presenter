using System.Collections.Generic;

using ChurchPresenter.Controls.Rendering.Engine;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter;

/// <summary>
/// Cancels in-flight offscreen render work on a transient page before the shell frame navigates away.
/// Walking the live visual tree (rather than relying on <c>Unloaded</c>) covers <see cref="SceneCanvasView"/>
/// instances declared directly in page XAML as well as those generated inside item templates (for example
/// slide-list or theme thumbnails), so their shared-device GPU raster work stops before the next page starts
/// its own.
/// </summary>
/// <remarks>
/// This applies to transient (framed) pages. Show's operator preview mirror detaches via
/// <c>ShowPage.TeardownForNavigation</c> instead of this walk.
/// </remarks>
internal static class NavigationTeardown
{
    /// <summary>
    /// Tears down every <see cref="SceneCanvasView"/> under <paramref name="root"/>. This runs from a page's
    /// <c>OnNavigatedFrom</c> while the frame navigation is in progress, so it must never throw: an exception
    /// here could abort the navigation and leave the outgoing page displayed with its render already
    /// cancelled. Each surface is torn down in isolation and all failures are swallowed.
    /// </summary>
    public static void TeardownCompositionSurfaces(DependencyObject? root)
    {
        if (root is null)
            return;

        try
        {
            foreach (DependencyObject descendant in EnumerateSelfAndDescendants(root))
            {
                try
                {
                    if (descendant is SceneCanvasView sceneCanvas)
                        sceneCanvas.TeardownForNavigation();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NavigationTeardown surface teardown failed: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigationTeardown visual-tree walk failed: {ex}");
        }
    }

    private static IEnumerable<DependencyObject> EnumerateSelfAndDescendants(DependencyObject node)
    {
        yield return node;

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            foreach (DependencyObject descendant in EnumerateSelfAndDescendants(VisualTreeHelper.GetChild(node, i)))
                yield return descendant;
        }
    }
}
