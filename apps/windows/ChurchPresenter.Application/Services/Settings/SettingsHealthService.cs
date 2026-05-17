using ChurchPresenter.Backend.Output;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Settings;

/// <inheritdoc />
public sealed class SettingsHealthService(
    IContentDirectoryService paths,
    ISettingsService settings,
    IOutputTopologyService topology,
    IMachineStateService machineState,
    ILogger<SettingsHealthService> logger) : ISettingsHealthService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IOutputTopologyService _topology = topology ?? throw new ArgumentNullException(nameof(topology));
    private readonly IMachineStateService _machineState = machineState ?? throw new ArgumentNullException(nameof(machineState));
    private readonly ILogger<SettingsHealthService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public SettingsHealthSnapshot CurrentHealth { get; private set; } = new()
    {
        GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
    };

    /// <inheritdoc />
    public async Task<SettingsHealthSnapshot> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<SettingsHealthIssue>();

        ValidateContentRoot(issues);
        ValidateOutputBinding(issues);
        ValidateIntegrations(issues);

        var snapshot = new SettingsHealthSnapshot
        {
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            Issues = issues,
        };

        CurrentHealth = snapshot;

        try
        {
            await _machineState.SaveHealthSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist settings health snapshot.");
        }

        if (issues.Count > 0)
        {
            _logger.LogInformation(
                "Settings health validation found {IssueCount} issue{Plural}.",
                issues.Count,
                issues.Count == 1 ? string.Empty : "s");
        }

        return snapshot;
    }

    // ── Validators ────────────────────────────────────────────────────────────

    private void ValidateContentRoot(List<SettingsHealthIssue> issues)
    {
        var contentRoot = _paths.GetDocumentsDataDirectory();
        if (!Directory.Exists(contentRoot))
        {
            issues.Add(new SettingsHealthIssue
            {
                Area = "libraryManagement",
                Severity = "warning",
                Code = "content-root-not-found",
                Message = $"The content root folder does not exist: {contentRoot}",
                Setting = "ContentRoot",
                DegradedGracefully = true,
            });
            _logger.LogWarning("Settings health: content root not found at {Path}.", contentRoot);
        }
        else
        {
            // Check write access
            try
            {
                var testFile = Path.Combine(contentRoot, ".healthcheck");
                File.WriteAllText(testFile, string.Empty);
                File.Delete(testFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new SettingsHealthIssue
                {
                    Area = "libraryManagement",
                    Severity = "error",
                    Code = "content-root-not-writable",
                    Message = $"The content root is not writable: {contentRoot}",
                    Setting = "ContentRoot",
                    DegradedGracefully = true,
                });
                _logger.LogWarning("Settings health: content root not writable at {Path}.", contentRoot);
            }
        }
    }

    private void ValidateOutputBinding(List<SettingsHealthIssue> issues)
    {
        OutputTopologySnapshot topology = _topology.GetSnapshot();
        ScreenMapping mainMapping = topology.ResolveMapping(OutputScreenIds.Main);
        ScreenMapping stageMapping = topology.ResolveMapping(OutputScreenIds.Stage);
        OutputScreenDiagnostics mainDiagnostics = topology.ResolveDiagnostics(OutputScreenIds.Main);
        OutputScreenDiagnostics stageDiagnostics = topology.ResolveDiagnostics(OutputScreenIds.Stage);

        if (mainMapping.EndpointIds.Count == 0)
        {
            issues.Add(new SettingsHealthIssue
            {
                Area = "output",
                Severity = "info",
                Code = "no-audience-monitor-configured",
                Message = "No audience output monitor is configured.",
                Setting = "AudienceMonitorIds",
                DegradedGracefully = true,
            });
        }

        if (mainDiagnostics.Health == EndpointHealth.Missing)
        {
            issues.Add(new SettingsHealthIssue
            {
                Area = "output",
                Severity = "warning",
                Code = "audience-endpoint-missing",
                Message = mainDiagnostics.Message,
                Setting = "AudienceMonitorIds",
                DegradedGracefully = true,
            });
        }

        if (stageDiagnostics.Health == EndpointHealth.Missing)
        {
            issues.Add(new SettingsHealthIssue
            {
                Area = "output",
                Severity = "warning",
                Code = "stage-endpoint-missing",
                Message = stageDiagnostics.Message,
                Setting = "StageMonitorIds",
                DegradedGracefully = true,
            });
        }

        // Warn when the same monitor is assigned to both roles simultaneously.
        var audienceIds = _settings.Settings.Output.AudienceMonitorIds;
        var stageIds = _settings.Settings.Output.StageMonitorIds;
        if (audienceIds.Count > 0 && stageIds.Count > 0)
        {
            var audienceSet = new HashSet<string>(audienceIds, StringComparer.OrdinalIgnoreCase);
            bool hasOverlap = stageIds.Any(id => audienceSet.Contains(id));
            if (hasOverlap)
            {
                issues.Add(new SettingsHealthIssue
                {
                    Area = "output",
                    Severity = "warning",
                    Code = "monitor-assigned-to-multiple-roles",
                    Message = "One or more monitors are assigned to both audience and stage output.",
                    Setting = "StageMonitorIds",
                    DegradedGracefully = true,
                });
            }
        }
    }

    private void ValidateIntegrations(List<SettingsHealthIssue> issues)
    {
        var musicManager = _settings.Settings.Integrations?.MusicManager;
        if (musicManager == null)
            return;

        var hasUrl = !string.IsNullOrWhiteSpace(musicManager.SupabaseUrl);
        var hasKey = !string.IsNullOrWhiteSpace(musicManager.PublishableKey);

        if (hasUrl != hasKey)
        {
            issues.Add(new SettingsHealthIssue
            {
                Area = "integrations",
                Severity = "warning",
                Code = "integrations-partial-credentials",
                Message = "Sunday Manager integration has a URL but no key (or vice versa). Both are required.",
                Setting = hasUrl ? "PublishableKey" : "SupabaseUrl",
                DegradedGracefully = true,
            });
        }
    }
}