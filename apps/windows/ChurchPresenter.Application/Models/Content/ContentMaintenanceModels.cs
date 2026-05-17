using System.Text.Json.Serialization;

using ChurchPresenter.Backend.Media;

namespace ChurchPresenter.Models.Content;

/// <summary>
/// Identifies why a content maintenance pass ran.
/// </summary>
public enum ContentMaintenanceTrigger
{
    Default,
    Startup,
    ManualScan,
    LocationChanged,
}

/// <summary>
/// Severity of an audit issue.
/// </summary>
public enum AuditIssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One issue detected during a content audit pass.
/// </summary>
public sealed class AuditIssue
{
    [JsonPropertyName("severity")]
    public AuditIssueSeverity Severity { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("autoRepaired")]
    public bool AutoRepaired { get; set; }
}

/// <summary>
/// Full audit snapshot written to <c>Audits/ContentAudit.json</c> after each audit pass.
/// Portable; travels with the content root.
/// </summary>
public sealed class ContentAuditResult
{
    [JsonPropertyName("auditedAt")]
    public string AuditedAt { get; set; } = "";

    [JsonPropertyName("contentRootPath")]
    public string ContentRootPath { get; set; } = "";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("issues")]
    public List<AuditIssue> Issues { get; set; } = new();

    [JsonPropertyName("presentationCount")]
    public int PresentationCount { get; set; }

    [JsonPropertyName("libraryCount")]
    public int LibraryCount { get; set; }

    [JsonPropertyName("playlistCount")]
    public int PlaylistCount { get; set; }

    [JsonPropertyName("themeCount")]
    public int ThemeCount { get; set; }

    [JsonPropertyName("mediaLibraryItemCount")]
    public int MediaLibraryItemCount { get; set; }

    [JsonPropertyName("mediaMissingFileCount")]
    public int MediaMissingFileCount { get; set; }

    [JsonPropertyName("mediaExternalPathCount")]
    public int MediaExternalPathCount { get; set; }

    [JsonPropertyName("referenceGraphNodes")]
    public List<MediaReferenceNode> ReferenceGraphNodes { get; set; } = new();

    [JsonPropertyName("cleanupCandidates")]
    public List<MediaCleanupCandidate> CleanupCandidates { get; set; } = new();

    [JsonPropertyName("cleanupPreview")]
    public ContentCleanupPreview CleanupPreview { get; set; } = new();

    [JsonPropertyName("brokenReferences")]
    public List<BrokenContentReference> BrokenReferences { get; set; } = new();

    [JsonPropertyName("recoveryActions")]
    public List<ContentRecoveryAction> RecoveryActions { get; set; } = new();

    [JsonIgnore]
    public bool HasErrors => Issues.Any(i => i.Severity == AuditIssueSeverity.Error);

    [JsonIgnore]
    public bool HasWarnings => Issues.Any(i => i.Severity == AuditIssueSeverity.Warning);
}

/// <summary>
/// Destructive cleanup preview summary derived from the full content reference graph.
/// </summary>
public sealed class ContentCleanupPreview
{
    [JsonPropertyName("candidateCount")]
    public int CandidateCount { get; set; }

    [JsonPropertyName("eligibleForCleanupCount")]
    public int EligibleForCleanupCount { get; set; }

    [JsonPropertyName("protectedReferenceCount")]
    public int ProtectedReferenceCount { get; set; }

    [JsonPropertyName("requiresDestructiveConfirmation")]
    public bool RequiresDestructiveConfirmation { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "No cleanup preview has been generated.";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("referenceGraphFingerprint")]
    public string ReferenceGraphFingerprint { get; set; } = "";
}

/// <summary>
/// One broken content reference discovered while traversing the canonical content graph.
/// </summary>
public sealed class BrokenContentReference
{
    [JsonPropertyName("surface")]
    public string Surface { get; set; } = "";

    [JsonPropertyName("ownerId")]
    public string OwnerId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("referenceKind")]
    public string ReferenceKind { get; set; } = "";

    [JsonPropertyName("referenceValue")]
    public string ReferenceValue { get; set; } = "";

    [JsonPropertyName("resolvedPath")]
    public string? ResolvedPath { get; set; }

    [JsonPropertyName("suggestedRepairPath")]
    public string? SuggestedRepairPath { get; set; }
}

/// <summary>
/// Suggested recovery action for a broken content reference.
/// </summary>
public sealed class ContentRecoveryAction
{
    [JsonPropertyName("actionId")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "suggested";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("targetPath")]
    public string? TargetPath { get; set; }
}

/// <summary>
/// One entry in the migration history log (<c>Audits/MigrationHistory.json</c>).
/// </summary>
public sealed class MigrationHistoryEntry
{
    [JsonPropertyName("runAt")]
    public string RunAt { get; set; } = "";

    [JsonPropertyName("fromSchemaVersion")]
    public int FromSchemaVersion { get; set; }

    [JsonPropertyName("toSchemaVersion")]
    public int ToSchemaVersion { get; set; } = 1;

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Migration history stored as <c>Audits/MigrationHistory.json</c>.
/// </summary>
public sealed class MigrationHistory
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<MigrationHistoryEntry> Entries { get; set; } = new();
}

/// <summary>
/// One persisted maintenance event for the managed content root.
/// </summary>
public sealed class ContentMaintenanceLogEntry
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = ContentMaintenanceTrigger.Default.ToString();

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonIgnore]
    public string TimestampDisplay
    {
        get
        {
            if (!DateTimeOffset.TryParse(Timestamp, out var timestamp))
                return Timestamp;

            return timestamp.ToLocalTime().ToString("g");
        }
    }
}

/// <summary>
/// A summary health card displayed at the top of the Library storage settings page.
/// </summary>
public sealed class ContentHealthSummaryCard
{
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string Description { get; init; }
    /// <summary>"healthy", "warning", "error", or "info".</summary>
    public required string StatusLevel { get; init; }
}