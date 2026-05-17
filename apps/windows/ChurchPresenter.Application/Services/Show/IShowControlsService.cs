namespace ChurchPresenter.Services.Show;

/// <summary>
/// Application-layer service behind the live Show Controls panels.
/// </summary>
public interface IShowControlsService
{
    Task<ShowControlsSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task<ShowAudioPlaylistDefinition> SaveAudioPlaylistAsync(ShowAudioPlaylistDefinition playlist, CancellationToken cancellationToken = default);

    Task<bool> TriggerAudioCueAsync(string cueId, CancellationToken cancellationToken = default);

    Task<ShowMessageDefinition> SaveMessageAsync(ShowMessageDefinition message, CancellationToken cancellationToken = default);

    Task<bool> ShowMessageAsync(string messageId, IEnumerable<ShowMessageRuntimeTokenValue>? tokens = null, CancellationToken cancellationToken = default);

    Task<bool> HideMessageAsync(string messageId, CancellationToken cancellationToken = default);

    Task<ShowPropDefinition> SavePropAsync(ShowPropDefinition prop, CancellationToken cancellationToken = default);

    Task<bool> TogglePropAsync(string propId, CancellationToken cancellationToken = default);

    Task<ShowMacroDefinition> SaveMacroAsync(ShowMacroDefinition macro, CancellationToken cancellationToken = default);

    Task<bool> ExecuteMacroAsync(string macroId, CancellationToken cancellationToken = default);

    Task<bool> SetStageLayoutAsync(string screenId, string layoutId, CancellationToken cancellationToken = default);

    Task<bool> SetStageMessageAsync(string text, bool visible, CancellationToken cancellationToken = default);
}
