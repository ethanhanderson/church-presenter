namespace ChurchPresenter.Models.Themes;

/// <summary>A parameter definition for a transition type (e.g. direction for wipe/slide).</summary>
public sealed class TransitionParamDef
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    /// <summary>"dropdown" or "slider".</summary>
    public string ControlType { get; init; } = "dropdown";

    public IReadOnlyList<TransitionParamOption>? Options { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 1;
    public double DefaultValue { get; init; }
    public string DefaultOption { get; init; } = string.Empty;
}

public sealed class TransitionParamOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

/// <summary>Descriptor for a named transition type in the catalog.</summary>
public sealed class TransitionDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<TransitionParamDef> Params { get; init; } = Array.Empty<TransitionParamDef>();

    /// <summary>Creates a default <see cref="SlideTransition"/> for this definition.</summary>
    public SlideTransition CreateDefault(int durationMs = 400) => new()
    {
        Type = Key,
        Duration = durationMs,
        Parameters = Params
            .Where(p => !string.IsNullOrEmpty(p.DefaultOption))
            .ToDictionary(p => p.Key, p => p.DefaultOption),
    };
}

/// <summary>Static catalog of all available presentation transition types.</summary>
public static class TransitionCatalog
{
    private static readonly TransitionParamDef DirectionParam = new()
    {
        Key = "direction",
        Label = "Direction",
        ControlType = "dropdown",
        DefaultOption = "fromLeft",
        Options = new[]
        {
            new TransitionParamOption { Value = "fromLeft",  Label = "From Left"  },
            new TransitionParamOption { Value = "fromRight", Label = "From Right" },
            new TransitionParamOption { Value = "fromTop",   Label = "From Top"   },
            new TransitionParamOption { Value = "fromBottom",Label = "From Bottom"},
        },
    };

    public static readonly IReadOnlyList<TransitionDefinition> All = new[]
    {
        new TransitionDefinition
        {
            Key = "cut",
            Name = "Cut",
            Category = "Basic",
            Description = "Instant switch with no animation.",
        },
        new TransitionDefinition
        {
            Key = "fade",
            Name = "Fade",
            Category = "Dissolve",
            Description = "Cross-dissolve between slides.",
        },
        new TransitionDefinition
        {
            Key = "wipe",
            Name = "Wipe",
            Category = "Motion",
            Description = "Reveals the next slide by sweeping over the current one.",
            Params = new[] { DirectionParam },
        },
        new TransitionDefinition
        {
            Key = "slide",
            Name = "Slide",
            Category = "Motion",
            Description = "Next slide pushes in from the selected direction.",
            Params = new[] { DirectionParam },
        },
        new TransitionDefinition
        {
            Key = "zoom-in",
            Name = "Zoom In",
            Category = "Scale",
            Description = "Next slide scales up from the center.",
        },
        new TransitionDefinition
        {
            Key = "zoom-out",
            Name = "Zoom Out",
            Category = "Scale",
            Description = "Current slide scales out as the next appears.",
        },
    };

    private static readonly Dictionary<string, TransitionDefinition> ByKey =
        All.ToDictionary(t => t.Key, t => t, StringComparer.OrdinalIgnoreCase);

    public static TransitionDefinition? Find(string? key) =>
        string.IsNullOrEmpty(key) ? null : ByKey.GetValueOrDefault(key);

    public static TransitionDefinition FindOrDefault(string? key) =>
        Find(key) ?? All[1]; // default = fade
}