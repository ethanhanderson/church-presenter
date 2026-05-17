
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Tracks the presentation currently opened in the WinUI operator session so other workspaces can initialize from it.
/// </summary>
public interface IActivePresentationService
{
    /// <summary>
    /// Gets the currently open typed presentation project, if one is active.
    /// </summary>
    PresentationProject? CurrentProject { get; }

    /// <summary>
    /// Gets the current presentation path, if one is active.
    /// </summary>
    string? CurrentPath { get; }

    /// <summary>
    /// Gets the currently selected slide for the active presentation surface.
    /// </summary>
    string? SelectedSlideId { get; }

    /// <summary>
    /// Updates the active presentation snapshot used by editor and theme workspaces.
    /// </summary>
    void SetCurrentPresentation(PresentationProject? project, string? path);

    /// <summary>
    /// Updates the currently selected slide for editor, reflow, and show handoff.
    /// </summary>
    void SetSelectedSlideId(string? slideId);
}

/// <inheritdoc />
public sealed class ActivePresentationService : IActivePresentationService
{
    /// <inheritdoc />
    public PresentationProject? CurrentProject { get; private set; }

    /// <inheritdoc />
    public string? CurrentPath { get; private set; }

    /// <inheritdoc />
    public string? SelectedSlideId { get; private set; }

    /// <inheritdoc />
    public void SetCurrentPresentation(PresentationProject? project, string? path)
    {
        CurrentProject = project;
        CurrentPath = path;
        if (project == null || string.IsNullOrWhiteSpace(path))
            SelectedSlideId = null;
    }

    /// <inheritdoc />
    public void SetSelectedSlideId(string? slideId)
    {
        SelectedSlideId = string.IsNullOrWhiteSpace(slideId) ? null : slideId;
    }
}