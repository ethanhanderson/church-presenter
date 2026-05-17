namespace ChurchPresenter.Backend.Commands;

/// <summary>
/// Serialized, testable macro definition built from shared live commands.
/// </summary>
public sealed record LiveMacroDefinition
{
    /// <summary>Stable macro id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing macro name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional collection/group id.</summary>
    public string? CollectionId { get; init; }

    /// <summary>Optional icon hint for future UI surfaces.</summary>
    public string? IconKey { get; init; }

    /// <summary>Optional accent color for future UI surfaces.</summary>
    public string? AccentColor { get; init; }

    /// <summary>Commands executed when the macro runs.</summary>
    public IReadOnlyList<LiveCommand> Commands { get; init; } = Array.Empty<LiveCommand>();
}