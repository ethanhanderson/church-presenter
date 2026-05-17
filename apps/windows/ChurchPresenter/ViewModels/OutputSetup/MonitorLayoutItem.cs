namespace ChurchPresenter.ViewModels;

/// <summary>
/// A compact card item for each monitor shown in the horizontal output selection strip
/// at the top of each output-settings section.
/// Immutable snapshot; recreated on every <c>RefreshMonitors</c> call.
/// </summary>
public sealed class MonitorCardItem
{
    /// <summary>Base card height used to derive scaled card dimensions from the monitor's aspect ratio.</summary>
    private const double CardBaseHeight = 72.0;

    /// <summary>Maximum card width after aspect-ratio scaling.</summary>
    private const double CardMaxWidth = 108.0;

    /// <summary>Minimum card width so very tall monitors still have a legible card.</summary>
    private const double CardMinWidth = 80.0;

    public MonitorCardItem(
        int index,
        string displayName,
        string resolutionText,
        int pixelWidth,
        int pixelHeight,
        bool isSelected,
        bool isExcluded)
    {
        Index = index;
        DisplayIndex = index + 1;
        DisplayName = displayName;
        ResolutionText = resolutionText;

        double aspectRatio = pixelHeight > 0 ? (double)pixelWidth / pixelHeight : 16.0 / 9.0;
        CardHeight = CardBaseHeight;
        CardWidth = Math.Max(CardMinWidth, Math.Min(CardMaxWidth, Math.Round(CardBaseHeight * aspectRatio, 0)));

        IsSelected = isSelected;
        IsExcluded = isExcluded;
        IsEnabled = !isExcluded;
        CheckmarkOpacity = isSelected ? 1.0 : 0.0;
        ContentOpacity = isExcluded ? 0.45 : 1.0;
        ExcludedLabelOpacity = isExcluded ? 1.0 : 0.0;
    }

    public int Index { get; }

    /// <summary>1-based display index for labelling.</summary>
    public int DisplayIndex { get; }

    public string DisplayName { get; }

    public string ResolutionText { get; }

    public double CardWidth { get; }

    public double CardHeight { get; }

    public bool IsSelected { get; }

    /// <summary>True when this monitor is already assigned to the opposite output role.</summary>
    public bool IsExcluded { get; }

    /// <summary>False when excluded — used to disable the card button without requiring a converter.</summary>
    public bool IsEnabled { get; }

    /// <summary>Opacity for the per-card checkmark glyph.</summary>
    public double CheckmarkOpacity { get; }

    /// <summary>Opacity applied to the card content when excluded (assigned to the other role).</summary>
    public double ContentOpacity { get; }

    /// <summary>Opacity for the "Used by …" label shown when this card is excluded.</summary>
    public double ExcludedLabelOpacity { get; }
}

/// <summary>One display rectangle for the settings monitor layout diagram (relative coordinates).</summary>
/// <remarks>
/// Immutable snapshot; item templates use <c>x:Bind</c> with <c>Mode=OneTime</c> because rows are recreated when the layout refreshes.
/// </remarks>
public sealed class MonitorLayoutItem
{
    public MonitorLayoutItem(
        int index,
        string displayName,
        string resolutionText,
        string positionText,
        string roleText,
        int pixelWidth,
        int pixelHeight,
        int positionX,
        int positionY,
        bool isPrimary,
        uint? refreshRate,
        double previewCanvasWidth,
        double previewCanvasHeight,
        IReadOnlyList<MonitorPreviewTile> previewTiles,
        bool isSelected)
    {
        Index = index;
        DisplayName = displayName;
        ResolutionText = resolutionText;
        PositionText = positionText;
        RoleText = roleText;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        PositionX = positionX;
        PositionY = positionY;
        IsPrimary = isPrimary;
        RefreshRate = refreshRate;
        PreviewCanvasWidth = previewCanvasWidth;
        PreviewCanvasHeight = previewCanvasHeight;
        PreviewTiles = previewTiles;
        IsSelected = isSelected;

        DisplayIndex = index + 1;
        CheckmarkOpacity = isSelected ? 1 : 0;
    }

    public int Index { get; }

    public string DisplayName { get; }

    public string ResolutionText { get; }

    public string PositionText { get; }

    public string RoleText { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int PositionX { get; }

    public int PositionY { get; }

    public bool IsPrimary { get; }

    public uint? RefreshRate { get; }

    public double PreviewCanvasWidth { get; }

    public double PreviewCanvasHeight { get; }

    public IReadOnlyList<MonitorPreviewTile> PreviewTiles { get; }

    public bool IsSelected { get; }

    /// <summary>1-based label shown on the diagram (matches legacy “display 1, 2…” naming).</summary>
    public int DisplayIndex { get; }

    /// <summary>Opacity for the per-row check glyph.</summary>
    public double CheckmarkOpacity { get; }
}

public sealed class MonitorPreviewTile
{
    public MonitorPreviewTile(
        double canvasLeft,
        double canvasTop,
        double width,
        double height,
        bool isHighlighted)
    {
        CanvasLeft = canvasLeft;
        CanvasTop = canvasTop;
        Width = width;
        Height = height;
        IsHighlighted = isHighlighted;
        HighlightOpacity = isHighlighted ? 1 : 0;
    }

    public double CanvasLeft { get; }

    public double CanvasTop { get; }

    public double Width { get; }

    public double Height { get; }

    public bool IsHighlighted { get; }

    public double HighlightOpacity { get; }
}