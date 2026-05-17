using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Show;

/// <summary>
/// Persisted Show-toolbar quick transition (cut, dissolve+duration, or custom catalog transition).
/// </summary>
public sealed class ShowToolbarTransitionDto
{
    /// <summary><c>cut</c>, <c>dissolve</c>, or <c>custom</c>.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>Duration in ms for dissolve mode (cross-fade).</summary>
    [JsonPropertyName("dissolveDurationMs")]
    public int DissolveDurationMs { get; set; } = 200;

    /// <summary>Full transition when <see cref="Mode"/> is <c>custom</c>.</summary>
    [JsonPropertyName("custom")]
    public SlideTransition? Custom { get; set; }
}