using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;

namespace ChurchPresenter.Models.Show;

/// <summary>
/// One operator-facing diagnostic item projected from live output or generated-system state.
/// </summary>
public sealed record OperatorDiagnosticItem
{
    /// <summary>Short title for the diagnostic row.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Detailed operator-facing diagnostic text.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Severity string: <c>healthy</c>, <c>info</c>, <c>warning</c>, or <c>error</c>.</summary>
    public string Severity { get; init; } = "info";
}

/// <summary>
/// One recovery action exposed to the Show operator.
/// </summary>
public sealed record OperatorRecoveryAction
{
    /// <summary>Stable action id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display label for the action.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Operator-facing action description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Action kind used by the UI to route execution.</summary>
    public string ActionType { get; init; } = string.Empty;
}

/// <summary>
/// Live diagnostics and recovery projection for Show surfaces.
/// </summary>
public sealed record LiveDiagnosticsRecoverySnapshot
{
    /// <summary>An empty diagnostics snapshot.</summary>
    public static readonly LiveDiagnosticsRecoverySnapshot Empty = new();

    /// <summary>Current live-production version.</summary>
    public long Version { get; init; }

    /// <summary>Output health rollup.</summary>
    public string OutputSummary { get; init; } = "No output diagnostics available yet.";

    /// <summary>Generated-system rollup.</summary>
    public string GeneratedSystemsSummary { get; init; } = "No generated systems are currently active.";

    /// <summary>Stage layout/message rollup.</summary>
    public string StageSummary { get; init; } = "No stage layout is currently assigned.";

    /// <summary>Projected diagnostic rows for screens and generated systems.</summary>
    public IReadOnlyList<OperatorDiagnosticItem> Diagnostics { get; init; } = Array.Empty<OperatorDiagnosticItem>();

    /// <summary>Recovery actions currently available to the operator.</summary>
    public IReadOnlyList<OperatorRecoveryAction> RecoveryActions { get; init; } = Array.Empty<OperatorRecoveryAction>();
}

/// <summary>
/// Media/capture projection for Show surfaces.
/// </summary>
public sealed record LiveMediaQuerySnapshot
{
    /// <summary>An empty media snapshot.</summary>
    public static readonly LiveMediaQuerySnapshot Empty = new();

    /// <summary>Operator-facing media summary.</summary>
    public string Summary { get; init; } = "No active capture sessions.";

    /// <summary>Active capture sessions projected from generated-system state.</summary>
    public IReadOnlyList<CaptureSessionState> ActiveCaptureSessions { get; init; } = Array.Empty<CaptureSessionState>();
}

/// <summary>
/// Output-host feedback projection separated from the full live-production query snapshot.
/// </summary>
public sealed record OutputHostFeedbackSnapshot
{
    /// <summary>An empty feedback snapshot.</summary>
    public static readonly OutputHostFeedbackSnapshot Empty = new();

    /// <summary>Human-readable output host summary.</summary>
    public string Summary { get; init; } = "No output hosts are configured.";

    /// <summary>Per-screen output summaries.</summary>
    public IReadOnlyList<LiveOutputScreenQuery> Screens { get; init; } = Array.Empty<LiveOutputScreenQuery>();

    /// <summary>Whether any output screen is missing, degraded, or unresolved.</summary>
    public bool HasWarnings { get; init; }
}

/// <summary>
/// Compact content audit projection for Settings and Show recovery entry points.
/// </summary>
public sealed record ContentAuditProjection
{
    /// <summary>Audit state when no audit has been loaded yet.</summary>
    public static readonly ContentAuditProjection Empty = new()
    {
        Summary = "No content audit has been loaded yet.",
    };

    /// <summary>Portable content root audited.</summary>
    public string ContentRootPath { get; init; } = string.Empty;

    /// <summary>Audit timestamp as stored by the audit service.</summary>
    public string AuditedAt { get; init; } = string.Empty;

    /// <summary>Operator-facing summary.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Total issues detected.</summary>
    public int IssueCount { get; init; }

    /// <summary>Total broken content references detected.</summary>
    public int BrokenReferenceCount { get; init; }

    /// <summary>Total suggested recovery actions detected.</summary>
    public int RecoveryActionCount { get; init; }

    /// <summary>Total eligible cleanup candidates detected.</summary>
    public int CleanupCandidateCount { get; init; }
}

/// <summary>
/// Support package import preview projection for operator recovery surfaces.
/// </summary>
public sealed record SupportPackageImportProjection
{
    /// <summary>Empty preview state.</summary>
    public static readonly SupportPackageImportProjection Empty = new()
    {
        Summary = "Enter a support package path to preview portable configuration changes.",
    };

    /// <summary>Support package path previewed.</summary>
    public string PackagePath { get; init; } = string.Empty;

    /// <summary>Human-readable summary.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Previewed changes.</summary>
    public IReadOnlyList<SupportPackagePreviewChange> Changes { get; init; } = Array.Empty<SupportPackagePreviewChange>();

    /// <summary>Whether the preview includes destructive changes.</summary>
    public bool HasDestructiveChanges { get; init; }
}