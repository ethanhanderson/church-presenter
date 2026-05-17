
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Persists operator workspace selection (library/playlist/presentation path and active shell page).
/// </summary>
public interface IWorkspaceService
{
    WorkspaceDto Workspace { get; }

    Task LoadAsync();

    Task SaveAsync();

    void Update(Action<WorkspaceDto> mutator);
}