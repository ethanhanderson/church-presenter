
namespace ChurchPresenter.Services.Settings;

/// <summary>
/// Reads and writes portable per-tab configuration files stored under
/// <c>Configurations/</c> in the managed content root.
/// These files travel with the content root and transfer cleanly to other machines.
/// </summary>
public interface ISharedConfigService
{
    /// <summary>Gets the current portable output configuration.</summary>
    OutputConfig Output { get; }

    /// <summary>Gets the current show configuration.</summary>
    ShowConfig Show { get; }

    /// <summary>Gets the current stage-screen configuration.</summary>
    StageConfig Stage { get; }

    /// <summary>Gets the current editor configuration.</summary>
    EditorConfig Editor { get; }

    /// <summary>Gets the current reflow configuration.</summary>
    ReflowConfig Reflow { get; }

    /// <summary>Gets the current integrations configuration.</summary>
    IntegrationsConfig Integrations { get; }

    /// <summary>Gets the current appearance configuration.</summary>
    AppearanceConfig Appearance { get; }

    /// <summary>Gets the current library management configuration.</summary>
    LibraryManagementConfig LibraryManagement { get; }

    /// <summary>Gets portable shared production support configuration.</summary>
    SupportConfig Support { get; }

    /// <summary>Loads all portable configuration files from disk, falling back to defaults when missing or invalid.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists all portable configuration files to disk.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a mutator to the output configuration.</summary>
    void UpdateOutput(Action<OutputConfig> mutator);

    /// <summary>Applies a mutator to the show configuration.</summary>
    void UpdateShow(Action<ShowConfig> mutator);

    /// <summary>Applies a mutator to the stage-screen configuration.</summary>
    void UpdateStage(Action<StageConfig> mutator);

    /// <summary>Applies a mutator to the editor configuration.</summary>
    void UpdateEditor(Action<EditorConfig> mutator);

    /// <summary>Applies a mutator to the reflow configuration.</summary>
    void UpdateReflow(Action<ReflowConfig> mutator);

    /// <summary>Applies a mutator to the integrations configuration.</summary>
    void UpdateIntegrations(Action<IntegrationsConfig> mutator);

    /// <summary>Applies a mutator to the appearance configuration.</summary>
    void UpdateAppearance(Action<AppearanceConfig> mutator);

    /// <summary>Applies a mutator to the library management configuration.</summary>
    void UpdateLibraryManagement(Action<LibraryManagementConfig> mutator);

    /// <summary>Applies a mutator to the shared support configuration.</summary>
    void UpdateSupport(Action<SupportConfig> mutator);
}