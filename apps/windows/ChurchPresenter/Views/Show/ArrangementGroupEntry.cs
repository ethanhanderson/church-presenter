using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Views;

/// <summary>Wrapper model for each group row in the arrangement editor UI.</summary>
public sealed class ArrangementGroupEntry
{
    public string SectionGroupId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public SolidColorBrush? BackgroundBrush { get; set; }

    public SolidColorBrush? ForegroundBrush { get; set; }

    /// <summary>True when the arrangement is Master — order is fixed (section list in file).</summary>
    public bool IsReorderLocked { get; set; }
}