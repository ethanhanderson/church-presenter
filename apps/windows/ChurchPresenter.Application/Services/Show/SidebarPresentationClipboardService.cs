namespace ChurchPresenter.Services.Show;

/// <summary>
/// Stores the most recently copied presentation for sidebar copy/paste duplication actions.
/// </summary>
public interface ISidebarPresentationClipboardService
{
    /// <summary>
    /// Gets the copied presentation path, if any.
    /// </summary>
    string? PresentationPath { get; }

    /// <summary>
    /// True when a presentation has been copied and can be pasted.
    /// </summary>
    bool HasPresentation { get; }

    /// <summary>
    /// Stores a presentation path for later paste operations.
    /// </summary>
    void SetPresentation(string presentationPath);

    /// <summary>
    /// Clears the current clipboard contents.
    /// </summary>
    void Clear();
}

/// <inheritdoc />
public sealed class SidebarPresentationClipboardService : ISidebarPresentationClipboardService
{
    /// <inheritdoc />
    public string? PresentationPath { get; private set; }

    /// <inheritdoc />
    public bool HasPresentation => !string.IsNullOrWhiteSpace(PresentationPath);

    /// <inheritdoc />
    public void SetPresentation(string presentationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        PresentationPath = presentationPath;
    }

    /// <inheritdoc />
    public void Clear()
    {
        PresentationPath = null;
    }
}