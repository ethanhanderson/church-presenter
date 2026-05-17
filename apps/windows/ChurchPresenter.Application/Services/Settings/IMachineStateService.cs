
namespace ChurchPresenter.Services.Settings;

/// <summary>
/// Reads and writes machine-local state files stored under
/// <c>%LocalAppData%/ChurchPresenter/MachineState/</c>.
/// These files hold bindings that are meaningless on a different machine (monitor IDs, etc.)
/// and must never be shared as part of the portable content root.
/// </summary>
public interface IMachineStateService
{
    /// <summary>Gets the current output monitor binding.</summary>
    OutputBinding OutputBinding { get; }

    /// <summary>Gets the current recent files state.</summary>
    RecentFilesState RecentFiles { get; }

    /// <summary>Gets the current updates state.</summary>
    UpdatesState Updates { get; }

    /// <summary>Gets machine-local device bindings.</summary>
    DeviceBindingsState DeviceBindings { get; }

    /// <summary>Gets machine-local credential references.</summary>
    CredentialsState Credentials { get; }

    /// <summary>Gets machine-local cache and relink hints.</summary>
    CacheState Caches { get; }

    /// <summary>Gets machine-local diagnostics state.</summary>
    DiagnosticsState Diagnostics { get; }

    /// <summary>Gets the last-written settings health snapshot.</summary>
    SettingsHealthSnapshot? SettingsHealth { get; }

    /// <summary>Loads all machine-state files from disk.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists all machine-state files to disk.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a mutator to the output binding.</summary>
    void UpdateOutputBinding(Action<OutputBinding> mutator);

    /// <summary>Applies a mutator to recent files.</summary>
    void UpdateRecentFiles(Action<RecentFilesState> mutator);

    /// <summary>Applies a mutator to the updates state.</summary>
    void UpdateUpdates(Action<UpdatesState> mutator);

    /// <summary>Applies a mutator to machine-local device bindings.</summary>
    void UpdateDeviceBindings(Action<DeviceBindingsState> mutator);

    /// <summary>Applies a mutator to machine-local credentials.</summary>
    void UpdateCredentials(Action<CredentialsState> mutator);

    /// <summary>Applies a mutator to machine-local caches.</summary>
    void UpdateCaches(Action<CacheState> mutator);

    /// <summary>Applies a mutator to machine-local diagnostics.</summary>
    void UpdateDiagnostics(Action<DiagnosticsState> mutator);

    /// <summary>Persists a new settings health snapshot, replacing the previous one.</summary>
    Task SaveHealthSnapshotAsync(SettingsHealthSnapshot snapshot, CancellationToken cancellationToken = default);
}