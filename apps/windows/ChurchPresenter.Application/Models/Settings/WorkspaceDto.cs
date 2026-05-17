using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Settings;

/// <summary>
/// Persisted shell and content selection for the Windows desktop app.
/// </summary>
public sealed class WorkspaceDto
{
    /// <summary>Historical default width; reused as minimum resizable output preview width.</summary>
    public const double ShowOutputPanelMinWidthDpi = 360;

    public const double ShowOutputPanelMaxWidthDpi = 560;

    /// <summary>Default output preview column width stored in <see cref="ShowOutputPanelWidth"/> until the operator resizes.</summary>
    public const double ShowOutputPanelDefaultWidthDpi = 460;

    /// <summary>
    /// Clamps persisted output preview width into the operator range or falls back when unset/out of range legacy values.
    /// </summary>
    public static double NormalizeStoredShowOutputPanelWidth(double stored)
    {
        if (double.IsNaN(stored) || double.IsInfinity(stored) || stored <= 0)
            return ShowOutputPanelDefaultWidthDpi;

        return Math.Clamp(stored, ShowOutputPanelMinWidthDpi, ShowOutputPanelMaxWidthDpi);
    }

    [JsonPropertyName("activePage")]
    public string ActivePage { get; set; } = "show";

    [JsonPropertyName("selectedLibraryId")]
    public string? SelectedLibraryId { get; set; }

    [JsonPropertyName("selectedPlaylistId")]
    public string? SelectedPlaylistId { get; set; }

    [JsonPropertyName("selectedPresentationPath")]
    public string? SelectedPresentationPath { get; set; }

    /// <summary>Show page output preview column width in DIP (persisted workspace state).</summary>
    [JsonPropertyName("showOutputPanelWidth")]
    public double ShowOutputPanelWidth { get; set; }
}