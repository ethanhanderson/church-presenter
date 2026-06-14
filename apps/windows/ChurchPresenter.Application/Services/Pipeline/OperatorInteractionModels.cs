namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Describes what initiated an operator slide selection change.
/// </summary>
public enum SelectionSource
{
    /// <summary>The local operator explicitly selected the slide.</summary>
    Operator,

    /// <summary>Selection followed a live cue or program output change.</summary>
    Program,

    /// <summary>Selection changed through automation, macro, timer, or remote control.</summary>
    Automation,
}

/// <summary>
/// Current operator selection cursor independent from the live output layer slots.
/// </summary>
public sealed record SelectionCursor
{
    /// <summary>Empty selection cursor.</summary>
    public static SelectionCursor Empty { get; } = new();

    /// <summary>Presentation path or id that owns the selected slide.</summary>
    public string? PresentationPath { get; init; }

    /// <summary>Selected slide id.</summary>
    public string? SlideId { get; init; }

    /// <summary>Selected arrangement or playlist occurrence key.</summary>
    public string? InstanceKey { get; init; }

    /// <summary>Source that produced the selection.</summary>
    public SelectionSource Source { get; init; }

    /// <summary>Gets whether the cursor currently references a slide.</summary>
    public bool HasSelection => !string.IsNullOrWhiteSpace(SlideId);
}

/// <summary>
/// Result of one rapid slide seek step.
/// </summary>
public sealed record SlideSeekStepResult
{
    /// <summary>Result used when no slide movement occurred.</summary>
    public static SlideSeekStepResult None { get; } = new();

    /// <summary>Whether the step moved to another slide.</summary>
    public bool Moved { get; init; }

    /// <summary>Delay before the next repeated seek step.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>
    /// Creates a moved seek result with the requested repeat delay.
    /// </summary>
    /// <param name="delay">Delay before the next repeated seek step.</param>
    /// <returns>A moved seek step result.</returns>
    public static SlideSeekStepResult FromDelay(TimeSpan delay) =>
        new()
        {
            Moved = true,
            Delay = delay,
        };
}
