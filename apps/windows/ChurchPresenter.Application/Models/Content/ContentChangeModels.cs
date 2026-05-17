namespace ChurchPresenter.Models.Content;

/// <summary>
/// Identifies the type of content mutation or refresh that can invalidate caches.
/// </summary>
public enum ContentChangeKind
{
    ContentRootChanged,
    CatalogRefreshed,
    SharedConfigChanged,
    MachineBindingChanged,
    PresentationAdded,
    PresentationUpdated,
    PresentationRenamed,
    PresentationDeleted,
    PresentationReplaced,
    MediaAssetAdded,
    MediaAssetUpdated,
    MediaAssetDeleted,
    MediaAssetRelinked,
    MediaAssetMissing,
    PackageImportCompleted,
    AuditCompleted,
    RepairCompleted,
}

/// <summary>
/// Describes an application-owned content change so caches and diagnostics can react coherently.
/// </summary>
public sealed record ContentChangeEvent
{
    /// <summary>The type of change that occurred.</summary>
    public ContentChangeKind Kind { get; init; }

    /// <summary>Primary content path, asset id, or configuration id affected by the change.</summary>
    public string? SubjectId { get; init; }

    /// <summary>Previous path or identity for rename/replace events.</summary>
    public string? PreviousSubjectId { get; init; }

    /// <summary>Resource stamp captured after the change, when relevant.</summary>
    public ContentResourceStamp? Stamp { get; init; }

    /// <summary>Optional source component that published the change.</summary>
    public string? Source { get; init; }

    /// <summary>UTC timestamp when the change was published.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
