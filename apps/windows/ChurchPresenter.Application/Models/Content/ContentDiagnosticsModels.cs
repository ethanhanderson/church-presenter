namespace ChurchPresenter.Models.Content;

/// <summary>
/// Operator-facing diagnostic for file access, stale cache, or content health issues.
/// </summary>
public sealed record ContentDiagnosticItem
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Severity { get; init; } = "info";

    public ContentAccessFailureKind? FailureKind { get; init; }

    public string? SubjectId { get; init; }
}

/// <summary>
/// Operator-facing recovery action for a content diagnostic.
/// </summary>
public sealed record ContentRecoveryActionQuery
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string ActionType { get; init; } = string.Empty;

    public string? SubjectId { get; init; }
}

/// <summary>
/// Aggregated content/file/cache health projection for operator and settings surfaces.
/// </summary>
public sealed record ContentDiagnosticsSnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<ContentDiagnosticItem> Diagnostics { get; init; } = Array.Empty<ContentDiagnosticItem>();

    public IReadOnlyList<ContentRecoveryActionQuery> RecoveryActions { get; init; } = Array.Empty<ContentRecoveryActionQuery>();
}
