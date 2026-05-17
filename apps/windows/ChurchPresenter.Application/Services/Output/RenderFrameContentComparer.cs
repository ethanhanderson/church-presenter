using System.Collections.Generic;
using System.Linq;


namespace ChurchPresenter.Services.Output;

/// <summary>
/// Value equality for <see cref="RenderFrame"/> content so output view-models can skip redundant UI notifications.
/// </summary>
public static class RenderFrameContentComparer
{
    /// <summary>True when both frames would paint the same program output (ignores reference identity of project/slide objects).</summary>
    public static bool AreEquivalent(RenderFrame a, RenderFrame b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (!string.Equals(
                PresentationModelUtilities.StablePresentationKey(a.Project),
                PresentationModelUtilities.StablePresentationKey(b.Project),
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(NormalizeSlideId(a.ProgramSlideId), NormalizeSlideId(b.ProgramSlideId), StringComparison.OrdinalIgnoreCase))
            return false;

        if (a.BuildIndex != b.BuildIndex)
            return false;

        if (!string.Equals(a.Slide?.Id, b.Slide?.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!VisibleLayerIdsEqual(a.VisibleLayerIds, b.VisibleLayerIds))
            return false;

        if (!SlideMediaLayerBuilder.MediaLayersStateEquals(a.MediaLayers, b.MediaLayers))
            return false;

        if (!TransitionsEqual(a.Transition, b.Transition))
            return false;

        if (!TransitionsEqual(a.MediaTransition, b.MediaTransition))
            return false;

        if (a.SuppressPresentation != b.SuppressPresentation
            || a.SuppressMedia != b.SuppressMedia
            || a.IsBlackout != b.IsBlackout
            || a.IsClear != b.IsClear)
            return false;

        if (!string.Equals(a.OutputAspectRatioOverride, b.OutputAspectRatioOverride, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(
            PresentationModelUtilities.NormalizeOutputScaleMode(a.OutputScaleMode),
            PresentationModelUtilities.NormalizeOutputScaleMode(b.OutputScaleMode),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeSlideId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    private static bool VisibleLayerIdsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        var aEmpty = a == null || a.Count == 0;
        var bEmpty = b == null || b.Count == 0;
        if (aEmpty && bEmpty)
            return true;
        if (aEmpty || bEmpty)
            return false;
        if (a!.Count != b!.Count)
            return false;

        // Order-insensitive: SlideStageView sorts layers for rendering, so {A,B} and {B,A} produce identical output.
        var aSorted = a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var bSorted = b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < aSorted.Length; i++)
        {
            if (!string.Equals(aSorted[i], bSorted[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool TransitionsEqual(SlideTransition? x, SlideTransition? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x == null || y == null)
            return x == null && y == null;

        if (!string.Equals(x.Type?.Trim(), y.Type?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (x.Duration != y.Duration)
            return false;
        if (!string.Equals(x.Easing?.Trim(), y.Easing?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var xd = x.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var yd = y.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (xd.Count != yd.Count)
            return false;
        foreach (var pair in xd)
        {
            if (!yd.TryGetValue(pair.Key, out var yv))
                return false;
            if (!string.Equals(pair.Value?.Trim(), yv?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}