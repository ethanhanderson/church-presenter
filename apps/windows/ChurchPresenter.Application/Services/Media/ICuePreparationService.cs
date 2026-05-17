
namespace ChurchPresenter.Services.Media;

/// <summary>
/// Prepares slide and media cues so the live output engine can enter them without resolving source state on demand.
/// </summary>
public interface ICuePreparationService
{
    /// <summary>
    /// Resolves and caches a slide cue for the requested presentation and slide.
    /// </summary>
    Task<PreparedSlideCue?> PrepareSlideCueAsync(
        string? presentationPath,
        string slideId,
        string? instanceKey = null,
        PresentationDocument? fallbackDocument = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the cached prepared slide cue when it matches the requested identity.</summary>
    PreparedSlideCue? GetPreparedSlideCue(string? presentationPath, string slideId, string? instanceKey = null);

    /// <summary>
    /// Invalidates all prepared slide cues for the specified presentation so future take-live operations
    /// rebuild them from the latest document state.
    /// </summary>
    void InvalidatePresentationCues(string? presentationPath);

    /// <summary>Resolves a media library item into a prepared media cue.</summary>
    PreparedMediaCue? PrepareMediaCue(MediaLibraryItem item);
}