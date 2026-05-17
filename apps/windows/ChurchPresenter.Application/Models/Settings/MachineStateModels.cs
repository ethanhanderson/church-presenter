using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Settings;

/// <summary>
/// Stored as <c>MachineState/ContentRootBinding.json</c> under local app data.
/// Holds the machine-specific content root override; <c>null</c> means use the default Documents path.
/// </summary>
public sealed class ContentRootBinding
{
    [JsonPropertyName("contentRootPath")]
    public string? ContentRootPath { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = true;

    [JsonPropertyName("lastValidatedAt")]
    public string? LastValidatedAt { get; set; }
}

/// <summary>
/// Stored as <c>MachineState/OutputBinding.json</c> under local app data.
/// Holds the machine-specific monitor selection which is meaningless on a different machine.
/// </summary>
public sealed class OutputBinding
{
    /// <summary>
    /// Legacy field preserved for migration. Promoted to <see cref="AudienceMonitorIds"/> by
    /// <c>MachineStateService.Normalize()</c> when <see cref="AudienceMonitorIds"/> is empty.
    /// </summary>
    [JsonPropertyName("monitorIds")]
    public List<string>? LegacyMonitorIds { get; set; }

    /// <summary>Monitor index strings assigned to audience (fullscreen) output on this machine.</summary>
    [JsonPropertyName("audienceMonitorIds")]
    public List<string> AudienceMonitorIds { get; set; } = new();

    /// <summary>Monitor index strings assigned to stage (operator) output on this machine.</summary>
    [JsonPropertyName("stageMonitorIds")]
    public List<string> StageMonitorIds { get; set; } = new();

    /// <summary>Identifier of the currently selected title-bar Look preset.</summary>
    [JsonPropertyName("activeLookId")]
    public string ActiveLookId { get; set; } = OutputLookIds.Default;

    /// <summary>
    /// Legacy field preserved for migration from earlier builds. Writable Looks now live in
    /// <c>Configurations/Output.json</c> so support files can travel between machines.
    /// </summary>
    [JsonPropertyName("looks")]
    public List<OutputLookDefinition> Looks { get; set; } = new();

    [JsonPropertyName("lastValidatedAt")]
    public string? LastValidatedAt { get; set; }
}

/// <summary>
/// Stored as <c>MachineState/RecentFiles.json</c> under local app data.
/// Tracks recently opened presentation paths on this machine.
/// </summary>
public sealed class RecentFilesState
{
    [JsonPropertyName("maxRecentFiles")]
    public int MaxRecentFiles { get; set; } = 10;

    [JsonPropertyName("entries")]
    public List<PresentationRefDto> Entries { get; set; } = new();
}

/// <summary>
/// Stored as <c>MachineState/Updates.json</c> under local app data.
/// Tracks update check state for this machine.
/// </summary>
public sealed class UpdatesState
{
    [JsonPropertyName("autoCheck")]
    public bool AutoCheck { get; set; } = true;

    [JsonPropertyName("lastCheckedAt")]
    public string? LastCheckedAt { get; set; }
}

/// <summary>
/// Stored as <c>MachineState/DeviceBindings.json</c>. Holds concrete device selections for this computer only.
/// </summary>
public sealed class DeviceBindingsState
{
    [JsonPropertyName("audioOutputDeviceIds")]
    public List<string> AudioOutputDeviceIds { get; set; } = new();

    [JsonPropertyName("videoInputDeviceIds")]
    public List<string> VideoInputDeviceIds { get; set; } = new();

    [JsonPropertyName("communicationDeviceBindings")]
    public Dictionary<string, string> CommunicationDeviceBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Stored as <c>MachineState/Credentials.json</c>. Keeps integration credential presence local to this machine.
/// </summary>
public sealed class CredentialsState
{
    [JsonPropertyName("credentialRefsByIntegration")]
    public Dictionary<string, string> CredentialRefsByIntegration { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("lastValidatedAt")]
    public string? LastValidatedAt { get; set; }
}

/// <summary>
/// Stored as <c>MachineState/Caches.json</c>. Tracks machine-local caches and relink hints.
/// </summary>
public sealed class CacheState
{
    [JsonPropertyName("mediaSearchRoots")]
    public List<string> MediaSearchRoots { get; set; } = new();

    [JsonPropertyName("thumbnailCacheVersion")]
    public string? ThumbnailCacheVersion { get; set; }

    [JsonPropertyName("recentRelinkHints")]
    public List<string> RecentRelinkHints { get; set; } = new();
}

/// <summary>
/// Stored as <c>MachineState/Diagnostics.json</c>. Captures local hardware and host diagnostics.
/// </summary>
public sealed class DiagnosticsState
{
    [JsonPropertyName("lastSnapshotAt")]
    public string? LastSnapshotAt { get; set; }

    [JsonPropertyName("lastKnownMonitorIds")]
    public List<string> LastKnownMonitorIds { get; set; } = new();

    [JsonPropertyName("lastMessages")]
    public List<string> LastMessages { get; set; } = new();
}

/// <summary>
/// A snapshot of detected settings health issues, stored as <c>MachineState/SettingsHealth.json</c>.
/// Drives the warning badges on the settings hub and inline hints on individual pages.
/// </summary>
public sealed class SettingsHealthSnapshot
{
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("issues")]
    public List<SettingsHealthIssue> Issues { get; set; } = new();
}

/// <summary>
/// One detected health issue for a settings area. Severity drives badge color.
/// </summary>
public sealed class SettingsHealthIssue
{
    /// <summary>Settings area tag: "output", "show", "editor", "reflow", "integrations", "appearance", "libraryManagement".</summary>
    [JsonPropertyName("area")]
    public string Area { get; set; } = "";

    /// <summary>"error", "warning", or "info".</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "warning";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>Optional name of the specific setting that has the issue.</summary>
    [JsonPropertyName("setting")]
    public string? Setting { get; set; }

    /// <summary>Whether the app fell back to a safe neutral state.</summary>
    [JsonPropertyName("degradedGracefully")]
    public bool DegradedGracefully { get; set; }
}

/// <summary>
/// Written to <c>%LocalAppData%/ChurchPresenter/Migration/LastRun.json</c>
/// to make the migration engine idempotent and resumable.
/// </summary>
public sealed class MigrationLastRun
{
    [JsonPropertyName("runAt")]
    public string RunAt { get; set; } = "";

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("fromSchemaVersion")]
    public int FromSchemaVersion { get; set; }

    [JsonPropertyName("toSchemaVersion")]
    public int ToSchemaVersion { get; set; } = 1;

    [JsonPropertyName("completedSteps")]
    public List<string> CompletedSteps { get; set; } = new();

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}