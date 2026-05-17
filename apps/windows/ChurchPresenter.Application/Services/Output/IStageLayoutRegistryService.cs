using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Read-only access to portable stage layout definitions and default logical screen assignments.
/// </summary>
public interface IStageLayoutRegistryService
{
    /// <summary>Gets stage layouts keyed by layout id.</summary>
    IReadOnlyDictionary<string, StageLayout> GetLayouts();

    /// <summary>Gets default layout assignments keyed by logical stage screen id.</summary>
    IReadOnlyDictionary<string, string> GetDefaultLayoutIdsByScreenId();
}