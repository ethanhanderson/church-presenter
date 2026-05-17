namespace ChurchPresenter.Models.Content;

/// <summary>
/// High-level phase for the startup content maintenance workflow.
/// </summary>
public enum ContentStartupMaintenancePhase
{
    NotStarted,
    LoadingSettings,
    BootstrappingContent,
    RepairingCatalog,
    ManagingCaches,
    CollectingDiagnostics,
    Completed,
    Failed,
}

/// <summary>
/// Current state of the background startup content maintenance workflow.
/// </summary>
public sealed record ContentStartupMaintenanceSnapshot
{
    public bool IsRunning { get; init; }

    public ContentStartupMaintenancePhase Phase { get; init; } = ContentStartupMaintenancePhase.NotStarted;

    public string StatusMessage { get; init; } = "Content maintenance has not started.";

    public string? ErrorMessage { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public ContentDiagnosticsSnapshot? Diagnostics { get; init; }

    public IReadOnlyList<string> PrunedCachePaths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Event payload raised when startup content maintenance status changes.
/// </summary>
public sealed class ContentStartupMaintenanceChangedEventArgs(ContentStartupMaintenanceSnapshot snapshot) : EventArgs
{
    public ContentStartupMaintenanceSnapshot Snapshot { get; } =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}
