
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Snapshot of a text layer's style-only data for slide text-style paste operations.
/// </summary>
public sealed class TextLayerStyleClipboardEntry
{
    public required TextStyleModel Style { get; init; }

    public required List<LayerFillModel> Fills { get; init; }

    public required List<LayerStrokeModel> Strokes { get; init; }

    public required List<LayerEffectModel> Effects { get; init; }

    public double? Padding { get; init; }
}

/// <summary>
/// Stores copied text style data from a slide.
/// </summary>
public interface ISlideTextStyleClipboardService
{
    IReadOnlyList<TextLayerStyleClipboardEntry> Entries { get; }

    bool HasEntries { get; }

    void SetFromSlide(PresentationSlide slide);

    void Clear();
}

/// <inheritdoc />
public sealed class SlideTextStyleClipboardService : ISlideTextStyleClipboardService
{
    private readonly List<TextLayerStyleClipboardEntry> _entries = new();

    /// <inheritdoc />
    public IReadOnlyList<TextLayerStyleClipboardEntry> Entries => _entries;

    /// <inheritdoc />
    public bool HasEntries => _entries.Count > 0;

    /// <inheritdoc />
    public void SetFromSlide(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);

        _entries.Clear();
        foreach (var layer in slide.Layers.OfType<TextLayer>())
        {
            _entries.Add(new TextLayerStyleClipboardEntry
            {
                Style = PresentationModelUtilities.DeepClone(layer.Style ?? PresentationModelUtilities.CreateDefaultTextStyle())
                    ?? PresentationModelUtilities.CreateDefaultTextStyle(),
                Fills = layer.Fills?.Select(fill => PresentationModelUtilities.DeepClone(fill) ?? new LayerFillModel()).ToList()
                    ?? new List<LayerFillModel>(),
                Strokes = layer.Strokes?.Select(stroke => PresentationModelUtilities.DeepClone(stroke) ?? new LayerStrokeModel()).ToList()
                    ?? new List<LayerStrokeModel>(),
                Effects = layer.Effects?.Select(effect => PresentationModelUtilities.DeepClone(effect) ?? new LayerBlurEffectModel()).Cast<LayerEffectModel>().ToList()
                    ?? new List<LayerEffectModel>(),
                Padding = layer.Padding,
            });
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _entries.Clear();
    }
}