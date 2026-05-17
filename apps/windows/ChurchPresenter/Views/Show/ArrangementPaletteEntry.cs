using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Views;

/// <summary>One section group in the arrangement manager palette (drag onto the order list to add an instance).</summary>
public sealed class ArrangementPaletteEntry
{
    public string SectionGroupId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public SolidColorBrush? BackgroundBrush { get; set; }

    public SolidColorBrush? ForegroundBrush { get; set; }

    /// <summary>When true, the chip can be dragged onto the arrangement list (custom arrangements only).</summary>
    public bool CanDrag { get; set; }
}