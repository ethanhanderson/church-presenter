using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Show;

/// <summary>
/// Query surface for Show diagnostics and recovery actions.
/// </summary>
public interface ILiveDiagnosticsRecoveryQueryService
{
    /// <summary>Raised when diagnostics or recovery actions change.</summary>
    event EventHandler<LiveDiagnosticsRecoveryChangedEventArgs>? Changed;

    /// <summary>Current diagnostics and recovery projection.</summary>
    LiveDiagnosticsRecoverySnapshot Current { get; }
}

/// <summary>
/// Event data for <see cref="ILiveDiagnosticsRecoveryQueryService.Changed"/>.
/// </summary>
public sealed class LiveDiagnosticsRecoveryChangedEventArgs : EventArgs
{
    /// <summary>Updated diagnostics and recovery snapshot.</summary>
    public LiveDiagnosticsRecoverySnapshot Snapshot { get; init; } = LiveDiagnosticsRecoverySnapshot.Empty;
}

/// <summary>
/// Query surface for active media/capture feedback.
/// </summary>
public interface ILiveMediaQueryService
{
    /// <summary>Raised when media state changes.</summary>
    event EventHandler<LiveMediaQueryChangedEventArgs>? Changed;

    /// <summary>Current media projection.</summary>
    LiveMediaQuerySnapshot Current { get; }
}

/// <summary>
/// Event data for <see cref="ILiveMediaQueryService.Changed"/>.
/// </summary>
public sealed class LiveMediaQueryChangedEventArgs : EventArgs
{
    /// <summary>Updated media snapshot.</summary>
    public LiveMediaQuerySnapshot Snapshot { get; init; } = LiveMediaQuerySnapshot.Empty;
}

/// <summary>
/// Query surface for output host health feedback.
/// </summary>
public interface IOutputHostFeedbackQueryService
{
    /// <summary>Raised when output host feedback changes.</summary>
    event EventHandler<OutputHostFeedbackChangedEventArgs>? Changed;

    /// <summary>Current output host feedback.</summary>
    OutputHostFeedbackSnapshot Current { get; }
}

/// <summary>
/// Event data for <see cref="IOutputHostFeedbackQueryService.Changed"/>.
/// </summary>
public sealed class OutputHostFeedbackChangedEventArgs : EventArgs
{
    /// <summary>Updated feedback snapshot.</summary>
    public OutputHostFeedbackSnapshot Snapshot { get; init; } = OutputHostFeedbackSnapshot.Empty;
}

/// <summary>
/// Query and recovery surface for content audits and support package imports.
/// </summary>
public interface IContentSupportQueryService
{
    /// <summary>Current audit projection.</summary>
    ContentAuditProjection CurrentAudit { get; }

    /// <summary>Current support package import preview.</summary>
    SupportPackageImportProjection CurrentSupportPreview { get; }

    /// <summary>Loads the last persisted audit result.</summary>
    Task<ContentAuditProjection> LoadLastAuditAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs and projects a full content audit.</summary>
    Task<ContentAuditProjection> RunAuditAsync(CancellationToken cancellationToken = default);

    /// <summary>Previews a support package import without modifying local files.</summary>
    Task<SupportPackageImportProjection> PreviewSupportPackageImportAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>Imports a support package after previewing and validating destructive-change policy.</summary>
    Task<SupportPackageImportProjection> ImportSupportPackageAsync(string packagePath, bool allowDestructiveReplace, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class LiveDiagnosticsRecoveryQueryService : ILiveDiagnosticsRecoveryQueryService
{
    private readonly ILiveProductionQueryService _liveProduction;

    /// <summary>Creates a diagnostics query service.</summary>
    public LiveDiagnosticsRecoveryQueryService(ILiveProductionQueryService liveProduction)
    {
        _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
        _liveProduction.Changed += HandleLiveProductionChanged;
        Current = BuildSnapshot(_liveProduction.Current);
    }

    /// <inheritdoc />
    public event EventHandler<LiveDiagnosticsRecoveryChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LiveDiagnosticsRecoverySnapshot Current { get; private set; }

    private void HandleLiveProductionChanged(object? sender, LiveProductionQueryChangedEventArgs args)
    {
        _ = sender;
        Current = BuildSnapshot(args.Snapshot);
        Changed?.Invoke(this, new LiveDiagnosticsRecoveryChangedEventArgs { Snapshot = Current });
    }

    internal static LiveDiagnosticsRecoverySnapshot BuildSnapshot(LiveProductionQuerySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = snapshot.Screens
            .Select(screen => new OperatorDiagnosticItem
            {
                Title = screen.ScreenName,
                Message = $"{screen.DiagnosticsMessage} {screen.FrameSummary} {screen.RoutingSummary}",
                Severity = ResolveSeverity(screen.Health, screen.HasResolvedFrame),
            })
            .Concat(BuildStateDiagnostics(snapshot))
            .ToArray();

        var recoveryActions = snapshot.Generated.ClearGroups
            .Select(group => new OperatorRecoveryAction
            {
                Id = $"clear-group:{group.Id}",
                Label = $"Clear {group.Name}",
                Description = $"Clears {FormatLayerList(group.Layers)} through the live command pipeline.",
                ActionType = "clear-group",
            })
            .Concat(BuildLayerRecoveryActions(snapshot.ActiveLayers))
            .Concat(BuildEndpointRecoveryActions(snapshot.Screens))
            .Concat(BuildFrameRecoveryActions(snapshot.FrameHealth))
            .Concat(BuildMediaRecoveryActions(snapshot.MediaIssues))
            .DistinctBy(static action => action.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LiveDiagnosticsRecoverySnapshot
        {
            Version = snapshot.Version,
            OutputSummary = BuildOutputSummary(snapshot.Screens),
            GeneratedSystemsSummary = BuildGeneratedSystemsSummary(snapshot.Generated),
            StageSummary = BuildStageSummary(snapshot.Generated.StageLayoutsByScreenId, snapshot.Generated.StageMessageText),
            Diagnostics = diagnostics,
            RecoveryActions = recoveryActions,
        };
    }

    private static IEnumerable<OperatorDiagnosticItem> BuildStateDiagnostics(LiveProductionQuerySnapshot snapshot)
    {
        yield return new OperatorDiagnosticItem
        {
            Title = "Selection",
            Message = snapshot.Selection.IsSelectionLive
                ? $"Selected slide {snapshot.Selection.SelectedSlideId} is live."
                : $"Selected slide {snapshot.Selection.SelectedSlideId ?? "<none>"} differs from live slide {snapshot.Selection.LiveSlideId ?? "<none>"}.",
            Severity = snapshot.Selection.IsSelectionLive || string.IsNullOrWhiteSpace(snapshot.Selection.SelectedSlideId)
                ? "healthy"
                : "info",
        };

        yield return new OperatorDiagnosticItem
        {
            Title = "Active Look",
            Message = string.IsNullOrWhiteSpace(snapshot.ActiveLookId)
                ? "No active Look is selected."
                : $"Active Look: {snapshot.ActiveLookId}.",
            Severity = string.IsNullOrWhiteSpace(snapshot.ActiveLookId) ? "warning" : "healthy",
        };

        foreach (LiveLayerStateQuery layer in snapshot.ActiveLayers)
        {
            yield return new OperatorDiagnosticItem
            {
                Title = $"{layer.DisplayName} layer",
                Message = layer.IsLive
                    ? $"Live payload: {layer.PayloadName ?? layer.PayloadId ?? "<unknown>"}."
                    : layer.IsCleared ? "Layer is cleared." : layer.IsSuppressed ? "Layer is suppressed." : "Layer is inactive.",
                Severity = string.IsNullOrWhiteSpace(layer.Diagnostics) ? "info" : "warning",
            };
        }

        foreach (LiveStageScreenQuery stageScreen in snapshot.StageScreens)
        {
            yield return new OperatorDiagnosticItem
            {
                Title = $"{stageScreen.ScreenName} stage layout",
                Message = string.IsNullOrWhiteSpace(stageScreen.StageLayoutId)
                    ? "No stage layout is assigned."
                    : $"Stage layout {stageScreen.StageLayoutId} ({stageScreen.CommandMode}) with {stageScreen.Payloads.Count} payload(s).",
                Severity = string.IsNullOrWhiteSpace(stageScreen.StageLayoutId) ? "warning" : "healthy",
            };
        }

        foreach (LiveFrameHealthQuery frame in snapshot.FrameHealth.Where(static frame => frame.IsStale || frame.DroppedFrameCount > 0))
        {
            yield return new OperatorDiagnosticItem
            {
                Title = $"{frame.ScreenId} frame feedback",
                Message = $"Resolved frame {frame.ResolvedSequence?.ToString() ?? "<none>"}, applied {frame.AppliedSequence?.ToString() ?? "<none>"}, dropped {frame.DroppedFrameCount}. {frame.Diagnostics}",
                Severity = frame.IsStale ? "warning" : "info",
            };
        }

        foreach (LiveMediaIssueQuery issue in snapshot.MediaIssues)
        {
            yield return new OperatorDiagnosticItem
            {
                Title = issue.Kind,
                Message = issue.Message,
                Severity = issue.Kind == "missing-media" || issue.Kind == "player-failure" ? "error" : "warning",
            };
        }
    }

    private static IEnumerable<OperatorRecoveryAction> BuildLayerRecoveryActions(IEnumerable<LiveLayerStateQuery> layers)
    {
        foreach (LiveLayerStateQuery layer in layers.Where(static layer => layer.IsLive))
        {
            yield return new OperatorRecoveryAction
            {
                Id = $"clear-layer:{layer.Id}",
                Label = $"Clear {layer.DisplayName}",
                Description = $"Clears only the active {layer.DisplayName} layer through the live command pipeline.",
                ActionType = "clear-layer",
            };
        }
    }

    private static IEnumerable<OperatorRecoveryAction> BuildEndpointRecoveryActions(IEnumerable<LiveOutputScreenQuery> screens)
    {
        foreach (LiveOutputScreenQuery screen in screens.Where(static screen => screen.Health == EndpointHealth.Missing))
        {
            yield return new OperatorRecoveryAction
            {
                Id = $"reconnect-endpoint:{screen.ScreenId}",
                Label = $"Reconnect {screen.ScreenName}",
                Description = "Attempts to recover the mapped output endpoint without changing live content.",
                ActionType = "reconnect-endpoint",
            };
        }
    }

    private static IEnumerable<OperatorRecoveryAction> BuildFrameRecoveryActions(IEnumerable<LiveFrameHealthQuery> frames)
    {
        foreach (LiveFrameHealthQuery frame in frames.Where(static frame => frame.IsStale))
        {
            yield return new OperatorRecoveryAction
            {
                Id = $"resync-host:{frame.ScreenId}",
                Label = $"Resync {frame.ScreenId}",
                Description = "Requests the output host to reapply the latest resolved backend frame.",
                ActionType = "resync-host",
            };
        }
    }

    private static IEnumerable<OperatorRecoveryAction> BuildMediaRecoveryActions(IEnumerable<LiveMediaIssueQuery> issues)
    {
        foreach (LiveMediaIssueQuery issue in issues.Where(static issue => !string.IsNullOrWhiteSpace(issue.RecoveryActionType)))
        {
            yield return new OperatorRecoveryAction
            {
                Id = $"{issue.RecoveryActionType}:{issue.SubjectId ?? issue.Id}",
                Label = BuildMediaRecoveryLabel(issue),
                Description = issue.Message,
                ActionType = issue.RecoveryActionType!,
            };
        }
    }

    private static string BuildMediaRecoveryLabel(LiveMediaIssueQuery issue) =>
        issue.RecoveryActionType switch
        {
            "relink-media" => "Relink media",
            "reset-player" => "Reset player",
            "restart-capture" => "Restart capture",
            "clear-layer" => issue.LayerKind.HasValue ? $"Clear {issue.LayerKind.Value}" : "Clear layer",
            _ => "Recover",
        };

    private static string BuildOutputSummary(IReadOnlyList<LiveOutputScreenQuery> screens)
    {
        if (screens.Count == 0)
            return "No output screens are configured.";

        int connected = screens.Count(static screen => screen.Health == EndpointHealth.Connected);
        int unresolved = screens.Count(static screen => !screen.HasResolvedFrame);
        return $"{connected}/{screens.Count} output screen(s) connected; {unresolved} unresolved frame(s).";
    }

    private static string BuildGeneratedSystemsSummary(LiveGeneratedSystemsQuery generated)
    {
        int activeTimerCount = generated.Timers.Count(static timer => timer.Status != GeneratedTimerStatus.Stopped);
        return $"Timers: {activeTimerCount} active, messages: {generated.VisibleMessages.Count}, props: {generated.VisibleProps.Count}, announcements: {generated.VisibleAnnouncements.Count}, masks: {generated.VisibleMasks.Count}, captures: {generated.ActiveCaptureSessions.Count}, clear groups: {generated.ClearGroups.Count}.";
    }

    private static string FormatLayerList(IReadOnlyList<BackendOutputLayerKind> layers) =>
        layers.Count == 0
            ? "configured layers"
            : string.Join(", ", layers);

    private static string BuildStageSummary(IReadOnlyDictionary<string, string> stageLayoutsByScreenId, string? stageMessageText)
    {
        if (stageLayoutsByScreenId.Count == 0 && string.IsNullOrWhiteSpace(stageMessageText))
            return "No stage layouts or stage messages are currently active.";

        string layoutSummary = stageLayoutsByScreenId.Count == 0
            ? "no stage layouts assigned"
            : $"{stageLayoutsByScreenId.Count} stage layout assignment(s)";

        return string.IsNullOrWhiteSpace(stageMessageText)
            ? $"Stage status: {layoutSummary}."
            : $"Stage status: {layoutSummary}. Message: {stageMessageText}";
    }

    private static string ResolveSeverity(EndpointHealth health, bool hasResolvedFrame) =>
        health == EndpointHealth.Missing ? "warning"
        : !hasResolvedFrame ? "info"
        : health == EndpointHealth.Connected ? "healthy"
        : "info";
}

/// <inheritdoc />
public sealed class LiveMediaQueryService : ILiveMediaQueryService
{
    private readonly ILiveProductionQueryService _liveProduction;

    /// <summary>Creates a media query service.</summary>
    public LiveMediaQueryService(ILiveProductionQueryService liveProduction)
    {
        _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
        _liveProduction.Changed += HandleLiveProductionChanged;
        Current = BuildSnapshot(_liveProduction.Current);
    }

    /// <inheritdoc />
    public event EventHandler<LiveMediaQueryChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LiveMediaQuerySnapshot Current { get; private set; }

    private void HandleLiveProductionChanged(object? sender, LiveProductionQueryChangedEventArgs args)
    {
        _ = sender;
        Current = BuildSnapshot(args.Snapshot);
        Changed?.Invoke(this, new LiveMediaQueryChangedEventArgs { Snapshot = Current });
    }

    internal static LiveMediaQuerySnapshot BuildSnapshot(LiveProductionQuerySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var activeCaptures = snapshot.Generated.ActiveCaptureSessions;
        return new LiveMediaQuerySnapshot
        {
            ActiveCaptureSessions = activeCaptures,
            Summary = activeCaptures.Count == 0
                ? "No active capture sessions."
                : $"{activeCaptures.Count} active capture session(s): {string.Join(", ", activeCaptures.Select(static session => session.Metadata.Name))}.",
        };
    }
}

/// <inheritdoc />
public sealed class OutputHostFeedbackQueryService : IOutputHostFeedbackQueryService
{
    private readonly ILiveProductionQueryService _liveProduction;

    /// <summary>Creates an output host feedback query service.</summary>
    public OutputHostFeedbackQueryService(ILiveProductionQueryService liveProduction)
    {
        _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
        _liveProduction.Changed += HandleLiveProductionChanged;
        Current = BuildSnapshot(_liveProduction.Current);
    }

    /// <inheritdoc />
    public event EventHandler<OutputHostFeedbackChangedEventArgs>? Changed;

    /// <inheritdoc />
    public OutputHostFeedbackSnapshot Current { get; private set; }

    private void HandleLiveProductionChanged(object? sender, LiveProductionQueryChangedEventArgs args)
    {
        _ = sender;
        Current = BuildSnapshot(args.Snapshot);
        Changed?.Invoke(this, new OutputHostFeedbackChangedEventArgs { Snapshot = Current });
    }

    internal static OutputHostFeedbackSnapshot BuildSnapshot(LiveProductionQuerySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool hasWarnings = snapshot.Screens.Any(static screen => screen.Health == EndpointHealth.Missing || !screen.HasResolvedFrame);
        int connected = snapshot.Screens.Count(static screen => screen.Health == EndpointHealth.Connected);
        return new OutputHostFeedbackSnapshot
        {
            Screens = snapshot.Screens,
            HasWarnings = hasWarnings,
            Summary = snapshot.Screens.Count == 0
                ? "No output hosts are configured."
                : $"{connected}/{snapshot.Screens.Count} output host(s) connected under Look {snapshot.ActiveLookId}.",
        };
    }
}

/// <inheritdoc />
public sealed class ContentSupportQueryService(
    IContentAuditService contentAudit,
    ISupportPackageService supportPackage) : IContentSupportQueryService
{
    private readonly IContentAuditService _contentAudit = contentAudit ?? throw new ArgumentNullException(nameof(contentAudit));
    private readonly ISupportPackageService _supportPackage = supportPackage ?? throw new ArgumentNullException(nameof(supportPackage));

    /// <inheritdoc />
    public ContentAuditProjection CurrentAudit { get; private set; } = ContentAuditProjection.Empty;

    /// <inheritdoc />
    public SupportPackageImportProjection CurrentSupportPreview { get; private set; } = SupportPackageImportProjection.Empty;

    /// <inheritdoc />
    public async Task<ContentAuditProjection> LoadLastAuditAsync(CancellationToken cancellationToken = default)
    {
        CurrentAudit = ProjectAudit(await _contentAudit.LoadLastAuditResultAsync(cancellationToken).ConfigureAwait(false));
        return CurrentAudit;
    }

    /// <inheritdoc />
    public async Task<ContentAuditProjection> RunAuditAsync(CancellationToken cancellationToken = default)
    {
        CurrentAudit = ProjectAudit(await _contentAudit.RunAuditAsync(cancellationToken).ConfigureAwait(false));
        return CurrentAudit;
    }

    /// <inheritdoc />
    public async Task<SupportPackageImportProjection> PreviewSupportPackageImportAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var preview = await _supportPackage.PreviewImportAsync(packagePath, cancellationToken).ConfigureAwait(false);
        CurrentSupportPreview = ProjectSupportPreview(packagePath, preview);
        return CurrentSupportPreview;
    }

    /// <inheritdoc />
    public async Task<SupportPackageImportProjection> ImportSupportPackageAsync(string packagePath, bool allowDestructiveReplace, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var preview = await _supportPackage.ImportAsync(
                packagePath,
                new SupportPackageImportOptions { AllowDestructiveReplace = allowDestructiveReplace },
                cancellationToken)
            .ConfigureAwait(false);
        CurrentSupportPreview = ProjectSupportPreview(packagePath, preview) with
        {
            Summary = $"Imported support package. {BuildSupportChangeSummary(preview.Changes)}",
        };
        return CurrentSupportPreview;
    }

    private static ContentAuditProjection ProjectAudit(ContentAuditResult? audit)
    {
        if (audit == null)
            return ContentAuditProjection.Empty;

        int cleanupCount = audit.CleanupCandidates.Count(static candidate => candidate.EligibleForCleanup);
        return new ContentAuditProjection
        {
            ContentRootPath = audit.ContentRootPath,
            AuditedAt = audit.AuditedAt,
            IssueCount = audit.Issues.Count,
            BrokenReferenceCount = audit.BrokenReferences.Count,
            RecoveryActionCount = audit.RecoveryActions.Count,
            CleanupCandidateCount = cleanupCount,
            Summary = audit.Issues.Count == 0
                ? $"Audit passed for {audit.ContentRootPath}. {audit.PresentationCount} presentation(s), {audit.MediaLibraryItemCount} media item(s)."
                : $"Audit found {audit.Issues.Count} issue(s), {audit.BrokenReferences.Count} broken reference(s), and {cleanupCount} cleanup candidate(s).",
        };
    }

    private static SupportPackageImportProjection ProjectSupportPreview(string packagePath, SupportPackagePreview preview) =>
        new()
        {
            PackagePath = packagePath,
            Changes = preview.Changes,
            HasDestructiveChanges = preview.HasDestructiveChanges,
            Summary = $"Previewed support package. {BuildSupportChangeSummary(preview.Changes)}",
        };

    private static string BuildSupportChangeSummary(IReadOnlyList<SupportPackagePreviewChange> changes)
    {
        int add = changes.Count(static change => change.Kind == SupportPackageChangeKind.Add);
        int replace = changes.Count(static change => change.Kind == SupportPackageChangeKind.Replace);
        int delete = changes.Count(static change => change.Kind == SupportPackageChangeKind.Delete);
        int unchanged = changes.Count(static change => change.Kind == SupportPackageChangeKind.Unchanged);
        int warning = changes.Count(static change => change.Kind == SupportPackageChangeKind.Warning);
        return $"{add} add, {replace} replace, {delete} delete, {unchanged} unchanged, {warning} warning.";
    }
}