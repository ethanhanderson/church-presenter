
using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
public sealed class ContentStartupMaintenanceService(
    ISettingsService settings,
    IContentBootstrapService bootstrap,
    ICatalogService catalog,
    IContentDiagnosticsQueryService diagnostics,
    IShowSessionCache sessionCache,
    ICuePreparationService cuePreparation,
    IContentChangeBus contentChanges,
    ILogger<ContentStartupMaintenanceService> logger) : IContentStartupMaintenanceService
{
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IContentBootstrapService _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IContentDiagnosticsQueryService _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly IShowSessionCache _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly IContentChangeBus _contentChanges = contentChanges ?? throw new ArgumentNullException(nameof(contentChanges));
    private readonly ILogger<ContentStartupMaintenanceService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public event EventHandler<ContentStartupMaintenanceChangedEventArgs>? Changed;

    /// <inheritdoc />
    public ContentStartupMaintenanceSnapshot Current { get; private set; } = new();

    /// <inheritdoc />
    public async Task StartAsync(
        ContentMaintenanceTrigger trigger = ContentMaintenanceTrigger.Startup,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            await RunCoreAsync(trigger, startedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(new ContentStartupMaintenanceSnapshot
            {
                IsRunning = false,
                Phase = ContentStartupMaintenancePhase.Failed,
                StatusMessage = "Content maintenance was canceled.",
                ErrorMessage = "Canceled",
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Diagnostics = Current.Diagnostics,
                PrunedCachePaths = Current.PrunedCachePaths,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup content maintenance failed.");
            Publish(new ContentStartupMaintenanceSnapshot
            {
                IsRunning = false,
                Phase = ContentStartupMaintenancePhase.Failed,
                StatusMessage = $"Content maintenance failed: {ex.Message}",
                ErrorMessage = ex.Message,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Diagnostics = Current.Diagnostics,
                PrunedCachePaths = Current.PrunedCachePaths,
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunCoreAsync(
        ContentMaintenanceTrigger trigger,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        PublishRunning(ContentStartupMaintenancePhase.LoadingSettings, "Loading settings...", startedAt);
        await _settings.LoadAsync().ConfigureAwait(false);

        PublishRunning(ContentStartupMaintenancePhase.BootstrappingContent, "Preparing content folders in the background...", startedAt);
        await _bootstrap.InitializeAsync(cancellationToken).ConfigureAwait(false);

        PublishRunning(ContentStartupMaintenancePhase.RepairingCatalog, "Scanning and repairing the content catalog...", startedAt);
        await _catalog.LoadAsync(trigger).ConfigureAwait(false);

        PublishRunning(ContentStartupMaintenancePhase.ManagingCaches, "Checking content caches...", startedAt);
        var prunedCachePaths = _sessionCache.PruneMissingFiles();
        foreach (var prunedPath in prunedCachePaths)
            _cuePreparation.InvalidatePresentationCues(prunedPath);

        if (prunedCachePaths.Count > 0)
        {
            _contentChanges.Publish(new ContentChangeEvent
            {
                Kind = ContentChangeKind.RepairCompleted,
                Source = nameof(ContentStartupMaintenanceService),
            });
        }

        Publish(new ContentStartupMaintenanceSnapshot
        {
            IsRunning = true,
            Phase = ContentStartupMaintenancePhase.CollectingDiagnostics,
            StatusMessage = "Collecting content diagnostics...",
            StartedAtUtc = startedAt,
            PrunedCachePaths = prunedCachePaths,
        });
        var diagnosticSnapshot = await _diagnostics.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        diagnosticSnapshot = IncludePrunedCacheDiagnostics(diagnosticSnapshot, prunedCachePaths);

        Publish(new ContentStartupMaintenanceSnapshot
        {
            IsRunning = false,
            Phase = ContentStartupMaintenancePhase.Completed,
            StatusMessage = diagnosticSnapshot.Diagnostics.Count == 0
                ? "Content maintenance complete. No issues found."
                : $"Content maintenance complete. {diagnosticSnapshot.Diagnostics.Count} issue{(diagnosticSnapshot.Diagnostics.Count == 1 ? string.Empty : "s")} found.",
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Diagnostics = diagnosticSnapshot,
            PrunedCachePaths = prunedCachePaths,
        });
    }

    private void PublishRunning(ContentStartupMaintenancePhase phase, string statusMessage, DateTimeOffset startedAt)
    {
        Publish(new ContentStartupMaintenanceSnapshot
        {
            IsRunning = true,
            Phase = phase,
            StatusMessage = statusMessage,
            StartedAtUtc = startedAt,
            Diagnostics = Current.Diagnostics,
            PrunedCachePaths = Current.PrunedCachePaths,
        });
    }

    private void Publish(ContentStartupMaintenanceSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, new ContentStartupMaintenanceChangedEventArgs(snapshot));
    }

    private static ContentDiagnosticsSnapshot IncludePrunedCacheDiagnostics(
        ContentDiagnosticsSnapshot snapshot,
        IReadOnlyList<string> prunedCachePaths)
    {
        if (prunedCachePaths.Count == 0)
            return snapshot;

        var diagnostics = snapshot.Diagnostics.ToList();
        var actions = snapshot.RecoveryActions.ToList();
        foreach (var path in prunedCachePaths)
        {
            var id = $"presentation-cache-pruned:{path}";
            if (diagnostics.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
                continue;

            diagnostics.Add(new ContentDiagnosticItem
            {
                Id = id,
                Title = "Presentation cache pruned",
                Message = $"Removed stale cached presentation '{path}'.",
                Severity = "warning",
                FailureKind = ContentAccessFailureKind.Outdated,
                SubjectId = path,
            });
            actions.Add(new ContentRecoveryActionQuery
            {
                Id = "clear-affected-caches",
                Label = "Clear affected caches",
                ActionType = "clear-affected-caches",
                SubjectId = path,
            });
        }

        return snapshot with
        {
            Diagnostics = diagnostics,
            RecoveryActions = actions
                .DistinctBy(static action => action.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }
}
