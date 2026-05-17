using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Projects portable stage-screen configuration into runtime-ready lookup dictionaries.
/// </summary>
public sealed class StageLayoutRegistryService(ISharedConfigService sharedConfig) : IStageLayoutRegistryService
{
    private readonly ISharedConfigService _sharedConfig = sharedConfig ?? throw new ArgumentNullException(nameof(sharedConfig));

    /// <inheritdoc />
    public IReadOnlyDictionary<string, StageLayout> GetLayouts()
    {
        return _sharedConfig.Stage.Layouts
            .Where(static layout => !string.IsNullOrWhiteSpace(layout.Id))
            .GroupBy(static layout => layout.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetDefaultLayoutIdsByScreenId()
    {
        return _sharedConfig.Stage.DefaultLayoutIdsByScreenId
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }
}