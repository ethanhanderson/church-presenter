#nullable enable

namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Single canonical ProPresenter-style layer z-order shared by the backend render
/// engine and every WinUI surface engine. This is the one source of truth for how
/// the eight fixed output layers stack; do not redefine the order anywhere else.
/// </summary>
public static class OutputLayerStack
{
    /// <summary>
    /// The eight fixed layers ordered from bottom (drawn first) to top (drawn last).
    /// Live video sits at the back, the mask constrains everything above it, and the
    /// non-visual audio layer is kept last so its index is stable for diagnostics.
    /// </summary>
    public static IReadOnlyList<OutputLayerKind> BottomToTop { get; } =
    [
        OutputLayerKind.LiveVideo,
        OutputLayerKind.Media,
        OutputLayerKind.Slide,
        OutputLayerKind.Announcements,
        OutputLayerKind.Props,
        OutputLayerKind.Messages,
        OutputLayerKind.Mask,
        OutputLayerKind.Audio,
    ];

    /// <summary>The eight fixed layers ordered from top (drawn last) to bottom (drawn first).</summary>
    public static IReadOnlyList<OutputLayerKind> TopToBottom { get; } =
        BottomToTop.Reverse().ToArray();

    /// <summary>Gets the bottom-to-top z-index for a layer (0 = bottom-most).</summary>
    public static int ZIndexOf(OutputLayerKind kind)
    {
        for (int index = 0; index < BottomToTop.Count; index++)
        {
            if (BottomToTop[index] == kind)
                return index;
        }

        return BottomToTop.Count;
    }

    /// <summary>Whether a layer draws visible pixels (audio is the only non-visual layer).</summary>
    public static bool IsVisual(OutputLayerKind kind) => kind != OutputLayerKind.Audio;

    /// <summary>Orders a sequence of layers by their canonical bottom-to-top z-order.</summary>
    public static IEnumerable<TItem> OrderByStack<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, OutputLayerKind> selector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selector);
        return items.OrderBy(item => ZIndexOf(selector(item)));
    }
}
