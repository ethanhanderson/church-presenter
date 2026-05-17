namespace ChurchPresenter.Models.Output;

/// <summary>Metadata for one display used when choosing output targets.</summary>
/// <param name="Index">Zero-based index in stable sort order.</param>
/// <param name="Name">Human-readable label.</param>
/// <param name="Width">Full display width in pixels.</param>
/// <param name="Height">Full display height in pixels.</param>
/// <param name="X">Full display origin X.</param>
/// <param name="Y">Full display origin Y.</param>
/// <param name="IsPrimary">Whether this is the primary display.</param>
/// <param name="RefreshRate">Optional refresh rate in Hz.</param>
public sealed record MonitorInfoDto(
    int Index,
    string Name,
    int Width,
    int Height,
    int X,
    int Y,
    bool IsPrimary,
    uint? RefreshRate);