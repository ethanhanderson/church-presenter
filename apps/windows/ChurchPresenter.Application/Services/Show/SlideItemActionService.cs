using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Show;

/// <summary>
/// Shared slide-level mutation pipeline used by Show, Edit, and Reflow.
/// </summary>
public interface ISlideItemActionService
{
    /// <summary>
    /// Duplicates a slide within a presentation and selects the duplicate.
    /// </summary>
    Task<SlideItemMutationResult> DuplicateSlideAsync(string presentationPath, string slideId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a slide and selects the most appropriate remaining slide.
    /// </summary>
    Task<SlideItemMutationResult> DeleteSlideAsync(string presentationPath, string slideId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a slide by the requested delta and keeps it selected.
    /// </summary>
    Task<SlideItemMutationResult> MoveSlideAsync(string presentationPath, string slideId, int delta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a single slide in place and keeps the updated slide selected.
    /// </summary>
    Task<SlideItemMutationResult> UpdateSlideAsync(
        string presentationPath,
        string slideId,
        Action<PresentationSlide, PresentationProject> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pastes a copied or cut slide before or after a target slide.
    /// </summary>
    Task<SlideItemMutationResult> PasteSlideAsync(
        string targetPresentationPath,
        string targetSlideId,
        SlideClipboardEntry clipboard,
        SlidePastePosition position = SlidePastePosition.After,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result metadata from mutating a slide collection.
/// </summary>
public sealed class SlideItemMutationResult
{
    public required PresentationProject Project { get; init; }

    public required string PresentationPath { get; init; }

    public string? SelectedSlideId { get; init; }
}

/// <summary>
/// Placement for pasted slides relative to the clicked target.
/// </summary>
public enum SlidePastePosition
{
    Before,
    After,
}

/// <inheritdoc />
public sealed class SlideItemActionService(
    IContentDirectoryService content,
    IPresentationProjectService projects,
    IPresentationDocumentService presentationDocuments,
    IShowSessionCache sessionCache,
    ICuePreparationService cuePreparation,
    IWorkspaceService workspace,
    IActivePresentationService activePresentation,
    ILogger<SlideItemActionService> logger) : ISlideItemActionService
{
    private static readonly JsonSerializerOptions JsonOptions = PresentationJsonSerialization.CreateOptions();

    private readonly IContentDirectoryService _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly IPresentationProjectService _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly IPresentationDocumentService _presentationDocuments = presentationDocuments ?? throw new ArgumentNullException(nameof(presentationDocuments));
    private readonly IShowSessionCache _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly IWorkspaceService _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly IActivePresentationService _activePresentation = activePresentation ?? throw new ArgumentNullException(nameof(activePresentation));
    private readonly ILogger<SlideItemActionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<SlideItemMutationResult> DuplicateSlideAsync(string presentationPath, string slideId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);

        var project = OpenProject(presentationPath);
        var slideIndex = FindSlideIndex(project, slideId);
        var duplicate = PresentationModelUtilities.CloneSlide(project.Slides[slideIndex]);
        RegenerateSlideIds(duplicate);
        duplicate.SectionLabel = string.IsNullOrWhiteSpace(duplicate.SectionLabel)
            ? "Copy"
            : $"{duplicate.SectionLabel} Copy";
        duplicate.UpdatedAt = DateTime.UtcNow.ToString("O");
        project.Slides.Insert(slideIndex + 1, duplicate);

        return await SaveMutationAsync(project, presentationPath, duplicate.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SlideItemMutationResult> DeleteSlideAsync(string presentationPath, string slideId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);

        var project = OpenProject(presentationPath);
        var slideIndex = FindSlideIndex(project, slideId);
        project.Slides.RemoveAt(slideIndex);
        var selectedSlideId = project.Slides.Count == 0
            ? null
            : project.Slides[Math.Clamp(slideIndex, 0, project.Slides.Count - 1)].Id;
        return await SaveMutationAsync(project, presentationPath, selectedSlideId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SlideItemMutationResult> MoveSlideAsync(string presentationPath, string slideId, int delta, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);

        var project = OpenProject(presentationPath);
        var slideIndex = FindSlideIndex(project, slideId);
        var targetIndex = Math.Clamp(slideIndex + delta, 0, project.Slides.Count - 1);
        if (targetIndex == slideIndex)
            return await SaveMutationAsync(project, presentationPath, slideId, cancellationToken).ConfigureAwait(false);

        var slide = project.Slides[slideIndex];
        project.Slides.RemoveAt(slideIndex);
        project.Slides.Insert(targetIndex, slide);

        return await SaveMutationAsync(project, presentationPath, slide.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SlideItemMutationResult> UpdateSlideAsync(
        string presentationPath,
        string slideId,
        Action<PresentationSlide, PresentationProject> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);
        ArgumentNullException.ThrowIfNull(mutation);

        var project = OpenProject(presentationPath);
        var slideIndex = FindSlideIndex(project, slideId);
        var currentSlide = project.Slides[slideIndex];
        var updatedSlide = PresentationModelUtilities.CloneSlide(currentSlide);
        mutation(updatedSlide, project);
        PresentationModelUtilities.NormalizeSlide(updatedSlide, project.Manifest.SlideSize);
        if (AreEquivalent(currentSlide, updatedSlide))
            return await SaveMutationAsync(project, presentationPath, currentSlide.Id, cancellationToken).ConfigureAwait(false);

        updatedSlide.UpdatedAt = DateTime.UtcNow.ToString("O");
        project.Slides[slideIndex] = updatedSlide;
        return await SaveMutationAsync(project, presentationPath, updatedSlide.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SlideItemMutationResult> PasteSlideAsync(
        string targetPresentationPath,
        string targetSlideId,
        SlideClipboardEntry clipboard,
        SlidePastePosition position = SlidePastePosition.After,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPresentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSlideId);
        ArgumentNullException.ThrowIfNull(clipboard);

        var targetProject = OpenProject(targetPresentationPath);
        var targetIndex = FindSlideIndex(targetProject, targetSlideId);
        var insertIndex = position == SlidePastePosition.Before ? targetIndex : targetIndex + 1;

        var slideToInsert = PresentationModelUtilities.CloneSlide(clipboard.Slide);
        var samePresentation = PathsEqual(targetPresentationPath, clipboard.SourcePresentationPath);
        if (!clipboard.IsCut || !samePresentation)
            RegenerateSlideIds(slideToInsert);

        if (clipboard.IsCut)
        {
            var sourceProject = samePresentation ? targetProject : OpenProject(clipboard.SourcePresentationPath);
            var sourceIndex = sourceProject.Slides.FindIndex(slide => string.Equals(slide.Id, clipboard.OriginalSlideId, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex >= 0)
            {
                sourceProject.Slides.RemoveAt(sourceIndex);
                if (ReferenceEquals(sourceProject, targetProject) && sourceIndex < insertIndex)
                    insertIndex--;

                if (!ReferenceEquals(sourceProject, targetProject))
                {
                    var sourceSelectedSlideId = sourceProject.Slides.Count == 0 ? null : sourceProject.Slides.First().Id;
                    await SaveMutationAsync(sourceProject, clipboard.SourcePresentationPath, sourceSelectedSlideId, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        insertIndex = Math.Clamp(insertIndex, 0, targetProject.Slides.Count);
        targetProject.Slides.Insert(insertIndex, slideToInsert);
        return await SaveMutationAsync(targetProject, targetPresentationPath, slideToInsert.Id, cancellationToken).ConfigureAwait(false);
    }

    private PresentationProject OpenProject(string presentationPath)
    {
        var project = _projects.Open(presentationPath);
        PresentationModelUtilities.NormalizeProject(project);
        return project;
    }

    private async Task<SlideItemMutationResult> SaveMutationAsync(
        PresentationProject project,
        string presentationPath,
        string? selectedSlideId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
        PresentationModelUtilities.ReconcileArrangement(project);
        _projects.Save(project, presentationPath);
        var refreshedDocument = _presentationDocuments.Open(presentationPath);
        var refreshedProject = refreshedDocument.Project ?? project;
        _sessionCache.UpdateEntry(presentationPath, refreshedDocument);
        _cuePreparation.InvalidatePresentationCues(presentationPath);
        _activePresentation.SetCurrentPresentation(refreshedProject, presentationPath);
        _activePresentation.SetSelectedSlideId(selectedSlideId);
        _workspace.Update(item => item.SelectedPresentationPath = presentationPath);
        await _workspace.SaveAsync().ConfigureAwait(false);

        _logger.LogInformation("Saved slide mutation to {PresentationPath} with selected slide {SlideId}.", presentationPath, selectedSlideId);

        return new SlideItemMutationResult
        {
            Project = refreshedProject,
            PresentationPath = presentationPath,
            SelectedSlideId = selectedSlideId,
        };
    }

    private static int FindSlideIndex(PresentationProject project, string slideId)
    {
        var slideIndex = project.Slides.FindIndex(slide => string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
        if (slideIndex < 0)
            throw new InvalidOperationException($"Could not find slide '{slideId}'.");

        return slideIndex;
    }

    private static void RegenerateSlideIds(PresentationSlide slide)
    {
        slide.Id = Guid.NewGuid().ToString("N");
        slide.CreatedAt = DateTime.UtcNow.ToString("O");
        slide.UpdatedAt = slide.CreatedAt;

        foreach (var layer in slide.Layers)
        {
            layer.Id = Guid.NewGuid().ToString("N");

            if (layer.Fills != null)
            {
                foreach (var fill in layer.Fills)
                    fill.Id = Guid.NewGuid().ToString("N");
            }

            if (layer.Strokes != null)
            {
                foreach (var stroke in layer.Strokes)
                    stroke.Id = Guid.NewGuid().ToString("N");
            }

            if (layer.Effects != null)
            {
                foreach (var effect in layer.Effects)
                    effect.Id = Guid.NewGuid().ToString("N");
            }
        }

        foreach (var cue in slide.MediaCues)
            cue.Id = Guid.NewGuid().ToString("N");
        foreach (var action in slide.Actions)
            action.Id = Guid.NewGuid().ToString("N");

        if (slide.Animations != null)
        {
            foreach (var buildStep in slide.Animations.BuildIn)
                buildStep.Id = Guid.NewGuid().ToString("N");
            foreach (var buildStep in slide.Animations.BuildOut)
                buildStep.Id = Guid.NewGuid().ToString("N");
        }
    }

    private static bool AreEquivalent<T>(T current, T updated)
    {
        return string.Equals(
            JsonSerializer.Serialize(current, JsonOptions),
            JsonSerializer.Serialize(updated, JsonOptions),
            StringComparison.Ordinal);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        return string.Equals(
            left?.Replace('\\', '/'),
            right?.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }
}