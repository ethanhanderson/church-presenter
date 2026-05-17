
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Slide clipboard payload for Show/Edit/Reflow cut-copy-paste operations.
/// </summary>
public sealed class SlideClipboardEntry
{
    public required string SourcePresentationPath { get; init; }

    public required string OriginalSlideId { get; init; }

    public required PresentationSlide Slide { get; init; }

    public bool IsCut { get; init; }
}

/// <summary>
/// Stores a copied or cut slide for later paste operations.
/// </summary>
public interface ISlideClipboardService
{
    SlideClipboardEntry? Entry { get; }

    bool HasSlide { get; }

    void SetCopy(string sourcePresentationPath, PresentationSlide slide);

    void SetCut(string sourcePresentationPath, PresentationSlide slide);

    void Clear();
}

/// <inheritdoc />
public sealed class SlideClipboardService : ISlideClipboardService
{
    /// <inheritdoc />
    public SlideClipboardEntry? Entry { get; private set; }

    /// <inheritdoc />
    public bool HasSlide => Entry != null;

    /// <inheritdoc />
    public void SetCopy(string sourcePresentationPath, PresentationSlide slide)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);
        ArgumentNullException.ThrowIfNull(slide);

        Entry = new SlideClipboardEntry
        {
            SourcePresentationPath = sourcePresentationPath,
            OriginalSlideId = slide.Id,
            Slide = PresentationModelUtilities.CloneSlide(slide),
            IsCut = false,
        };
    }

    /// <inheritdoc />
    public void SetCut(string sourcePresentationPath, PresentationSlide slide)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);
        ArgumentNullException.ThrowIfNull(slide);

        Entry = new SlideClipboardEntry
        {
            SourcePresentationPath = sourcePresentationPath,
            OriginalSlideId = slide.Id,
            Slide = PresentationModelUtilities.CloneSlide(slide),
            IsCut = true,
        };
    }

    /// <inheritdoc />
    public void Clear()
    {
        Entry = null;
    }
}