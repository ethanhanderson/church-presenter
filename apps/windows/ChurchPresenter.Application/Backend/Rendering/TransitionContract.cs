#nullable enable

namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Validated transition type shared by the backend render contract and every WinUI surface engine,
/// so hosts consume a typed value instead of re-parsing loose <see cref="LayerTransitionState.Type"/>
/// strings. Mirrors the keys in <c>TransitionCatalog</c>.
/// </summary>
public enum TransitionKind
{
    /// <summary>No transition intent.</summary>
    None,

    /// <summary>Instant switch with no animation.</summary>
    Cut,

    /// <summary>Cross-dissolve between layers.</summary>
    Fade,

    /// <summary>Reveal by sweeping over the outgoing layer.</summary>
    Wipe,

    /// <summary>Incoming layer pushes in from a direction.</summary>
    Slide,

    /// <summary>Incoming layer scales up from the center.</summary>
    ZoomIn,

    /// <summary>Outgoing layer scales out as the incoming layer appears.</summary>
    ZoomOut,
}

/// <summary>
/// Validated direction for direction-bearing transitions (wipe/slide).
/// </summary>
public enum TransitionDirection
{
    /// <summary>No directional component.</summary>
    None,

    /// <summary>Motion originates from the left edge.</summary>
    FromLeft,

    /// <summary>Motion originates from the right edge.</summary>
    FromRight,

    /// <summary>Motion originates from the top edge.</summary>
    FromTop,

    /// <summary>Motion originates from the bottom edge.</summary>
    FromBottom,
}

/// <summary>
/// Canonical parser that maps catalog/document transition strings to the typed render contract.
/// One source of truth so the resolver and hosts agree on how a string maps to a typed value.
/// </summary>
public static class TransitionContract
{
    /// <summary>Maps a transition type string (catalog key or alias) to a typed kind.</summary>
    public static TransitionKind ParseKind(string? type)
    {
        string? normalized = type?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return TransitionKind.None;

        return normalized switch
        {
            "cut" or "none" => TransitionKind.Cut,
            "fade" or "dissolve" => TransitionKind.Fade,
            "wipe" => TransitionKind.Wipe,
            "slide" or "push" => TransitionKind.Slide,
            "zoom-in" or "zoomin" or "zoom" => TransitionKind.ZoomIn,
            "zoom-out" or "zoomout" => TransitionKind.ZoomOut,
            _ => TransitionKind.Fade,
        };
    }

    /// <summary>Maps a direction parameter value (catalog form or alias) to a typed direction.</summary>
    public static TransitionDirection ParseDirection(string? direction)
    {
        string? normalized = direction?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return TransitionDirection.None;

        return normalized switch
        {
            "fromleft" or "left" => TransitionDirection.FromLeft,
            "fromright" or "right" => TransitionDirection.FromRight,
            "fromtop" or "top" or "up" => TransitionDirection.FromTop,
            "frombottom" or "bottom" or "down" => TransitionDirection.FromBottom,
            _ => TransitionDirection.None,
        };
    }
}
