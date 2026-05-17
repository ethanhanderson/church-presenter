using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Settings;

/// <inheritdoc />
public sealed class MachineStateService(
    IContentDirectoryService paths,
    ILogger<MachineStateService> logger) : IMachineStateService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<MachineStateService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public OutputBinding OutputBinding { get; private set; } = new();

    /// <inheritdoc />
    public RecentFilesState RecentFiles { get; private set; } = new();

    /// <inheritdoc />
    public UpdatesState Updates { get; private set; } = new();

    /// <inheritdoc />
    public DeviceBindingsState DeviceBindings { get; private set; } = new();

    /// <inheritdoc />
    public CredentialsState Credentials { get; private set; } = new();

    /// <inheritdoc />
    public CacheState Caches { get; private set; } = new();

    /// <inheritdoc />
    public DiagnosticsState Diagnostics { get; private set; } = new();

    /// <inheritdoc />
    public SettingsHealthSnapshot? SettingsHealth { get; private set; }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.GetMachineStateDirectory());

        OutputBinding = await LoadStateAsync<OutputBinding>("OutputBinding", cancellationToken).ConfigureAwait(false);
        RecentFiles = await LoadStateAsync<RecentFilesState>("RecentFiles", cancellationToken).ConfigureAwait(false);
        Updates = await LoadStateAsync<UpdatesState>("Updates", cancellationToken).ConfigureAwait(false);
        DeviceBindings = await LoadStateAsync<DeviceBindingsState>("DeviceBindings", cancellationToken).ConfigureAwait(false);
        Credentials = await LoadStateAsync<CredentialsState>("Credentials", cancellationToken).ConfigureAwait(false);
        Caches = await LoadStateAsync<CacheState>("Caches", cancellationToken).ConfigureAwait(false);
        Diagnostics = await LoadStateAsync<DiagnosticsState>("Diagnostics", cancellationToken).ConfigureAwait(false);

        var healthPath = _paths.GetMachineStatePath("SettingsHealth");
        if (File.Exists(healthPath))
            SettingsHealth = await LoadStateFileAsync<SettingsHealthSnapshot>(healthPath, cancellationToken).ConfigureAwait(false);

        Normalize();
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.GetMachineStateDirectory());

        await SaveOutputBindingAsync(cancellationToken).ConfigureAwait(false);
        await SaveStateAsync("RecentFiles", RecentFiles, cancellationToken).ConfigureAwait(false);
        await SaveStateAsync("Updates", Updates, cancellationToken).ConfigureAwait(false);
        await SaveStateAsync("DeviceBindings", DeviceBindings, cancellationToken).ConfigureAwait(false);
        await SaveStateAsync("Credentials", Credentials, cancellationToken).ConfigureAwait(false);
        await SaveStateAsync("Caches", Caches, cancellationToken).ConfigureAwait(false);
        await SaveStateAsync("Diagnostics", Diagnostics, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void UpdateOutputBinding(Action<OutputBinding> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(OutputBinding); }

    /// <inheritdoc />
    public void UpdateRecentFiles(Action<RecentFilesState> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(RecentFiles); }

    /// <inheritdoc />
    public void UpdateUpdates(Action<UpdatesState> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Updates); }

    /// <inheritdoc />
    public void UpdateDeviceBindings(Action<DeviceBindingsState> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(DeviceBindings); }

    /// <inheritdoc />
    public void UpdateCredentials(Action<CredentialsState> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Credentials); }

    /// <inheritdoc />
    public void UpdateCaches(Action<CacheState> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Caches); }

    /// <inheritdoc />
    public void UpdateDiagnostics(Action<DiagnosticsState> mutator) { ArgumentNullException.ThrowIfNull(mutator); mutator(Diagnostics); }

    /// <inheritdoc />
    public async Task SaveHealthSnapshotAsync(SettingsHealthSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SettingsHealth = snapshot;
        Directory.CreateDirectory(_paths.GetMachineStateDirectory());
        await SaveStateFileAsync(_paths.GetMachineStatePath("SettingsHealth"), snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> LoadStateAsync<T>(string name, CancellationToken cancellationToken) where T : class, new()
    {
        var path = _paths.GetMachineStatePath(name);
        return await LoadStateFileAsync<T>(path, cancellationToken).ConfigureAwait(false) ?? new T();
    }

    private async Task<T?> LoadStateFileAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(fs, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not load machine state from {Path}; using defaults.", path);
            return null;
        }
    }

    private async Task SaveStateAsync<T>(string name, T value, CancellationToken cancellationToken)
    {
        var path = _paths.GetMachineStatePath(name);
        await SaveStateFileAsync(path, value, cancellationToken).ConfigureAwait(false);
    }

    private Task SaveOutputBindingAsync(CancellationToken cancellationToken)
    {
        var machineLocal = new MachineLocalOutputBinding
        {
            AudienceMonitorIds = [.. OutputBinding.AudienceMonitorIds],
            StageMonitorIds = [.. OutputBinding.StageMonitorIds],
            ActiveLookId = OutputBinding.ActiveLookId,
            LastValidatedAt = OutputBinding.LastValidatedAt,
        };

        return SaveStateFileAsync(_paths.GetMachineStatePath("OutputBinding"), machineLocal, cancellationToken);
    }

    private async Task SaveStateFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        try
        {
            await using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save machine state to {Path}.", path);
        }
    }

    private void Normalize()
    {
        OutputBinding.AudienceMonitorIds ??= new List<string>();
        OutputBinding.StageMonitorIds ??= new List<string>();
        OutputBinding.Looks ??= new List<OutputLookDefinition>();
        OutputBinding.ActiveLookId = string.IsNullOrWhiteSpace(OutputBinding.ActiveLookId)
            ? OutputLookIds.Default
            : OutputBinding.ActiveLookId;

        // Migrate legacy monitorIds → audienceMonitorIds (one-way, clears the legacy field after promotion).
        if (OutputBinding.AudienceMonitorIds.Count == 0 && OutputBinding.LegacyMonitorIds?.Count > 0)
        {
            OutputBinding.AudienceMonitorIds = new List<string>(OutputBinding.LegacyMonitorIds);
            OutputBinding.LegacyMonitorIds = null;
        }

        RecentFiles.Entries ??= new List<PresentationRefDto>();
        DeviceBindings.AudioOutputDeviceIds ??= new List<string>();
        DeviceBindings.VideoInputDeviceIds ??= new List<string>();
        DeviceBindings.CommunicationDeviceBindings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Credentials.CredentialRefsByIntegration ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Caches.MediaSearchRoots ??= new List<string>();
        Caches.RecentRelinkHints ??= new List<string>();
        Diagnostics.LastKnownMonitorIds ??= new List<string>();
        Diagnostics.LastMessages ??= new List<string>();
    }

    private sealed class MachineLocalOutputBinding
    {
        [JsonPropertyName("audienceMonitorIds")]
        public List<string> AudienceMonitorIds { get; set; } = new();

        [JsonPropertyName("stageMonitorIds")]
        public List<string> StageMonitorIds { get; set; } = new();

        [JsonPropertyName("activeLookId")]
        public string ActiveLookId { get; set; } = OutputLookIds.Default;

        [JsonPropertyName("lastValidatedAt")]
        public string? LastValidatedAt { get; set; }
    }
}