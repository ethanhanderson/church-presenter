
namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Testable text-editing workflow used by Editor and Reflow WinUI shells.
/// </summary>
public interface IPresentationTextWorkflowService
{
    /// <summary>Opens a presentation and projects each slide into editable text.</summary>
    Task<PresentationTextDocument> OpenAsync(string presentationPath, CancellationToken cancellationToken = default);

    /// <summary>Updates the primary text and notes for one slide, then saves the presentation.</summary>
    Task<PresentationTextDocument> SaveSlideTextAsync(
        string presentationPath,
        string slideId,
        string text,
        string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the presentation slide list with slides generated from blank-line separated text blocks.
    /// </summary>
    Task<PresentationTextDocument> ReflowAsync(
        string presentationPath,
        string reflowText,
        CancellationToken cancellationToken = default);
}