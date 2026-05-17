namespace ChurchPresenter.Models.Documents;

/// <summary>
/// Text-oriented projection of a presentation used by the Editor and Reflow shell surfaces.
/// </summary>
public sealed class PresentationTextDocument
{
    /// <summary>Absolute source path for the opened presentation.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Presentation title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Presentation-level theme identifier, when the document assigns one.</summary>
    public string ThemeId { get; init; } = string.Empty;

    /// <summary>Count of theme templates embedded in the presentation bundle.</summary>
    public int EmbeddedThemeCount { get; init; }

    /// <summary>Human-readable summary of groups and arrangements preserved by the workflow.</summary>
    public string ArrangementSummary { get; init; } = string.Empty;

    /// <summary>Human-readable summary of slide actions and media cues preserved by the workflow.</summary>
    public string CueSummary { get; init; } = string.Empty;

    /// <summary>Human-readable summary of generated-template or layout variants in the document.</summary>
    public string TemplateVariantSummary { get; init; } = string.Empty;

    /// <summary>Slides projected as editable text blocks.</summary>
    public IReadOnlyList<PresentationTextSlide> Slides { get; init; } = Array.Empty<PresentationTextSlide>();

    /// <summary>All slide text joined using blank lines, suitable for reflow editing.</summary>
    public string ReflowText => string.Join($"{Environment.NewLine}{Environment.NewLine}", Slides.Select(static slide => slide.Text));
}

/// <summary>
/// Text projection for a single slide.
/// </summary>
public sealed class PresentationTextSlide
{
    /// <summary>Slide id in the presentation bundle.</summary>
    public string SlideId { get; init; } = string.Empty;

    /// <summary>One-based slide number.</summary>
    public int Ordinal { get; init; }

    /// <summary>Operator-facing section label.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Document section or group label for the slide.</summary>
    public string Section { get; init; } = string.Empty;

    /// <summary>Slide layout or generated-template variant key, when present.</summary>
    public string LayoutType { get; init; } = string.Empty;

    /// <summary>Editable primary slide text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional speaker/operator notes.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Number of slide layers preserved in the source document.</summary>
    public int LayerCount { get; init; }

    /// <summary>Number of slide actions preserved in the source document.</summary>
    public int ActionCount { get; init; }

    /// <summary>Number of media cues preserved in the source document.</summary>
    public int MediaCueCount { get; init; }

    /// <summary>Per-slide transition summary, when a slide override exists.</summary>
    public string TransitionSummary { get; init; } = string.Empty;

    /// <summary>Compact metadata used by WinUI inspector panels.</summary>
    public string MetadataSummary
    {
        get
        {
            string section = string.IsNullOrWhiteSpace(Section) ? "No section" : Section;
            string layout = string.IsNullOrWhiteSpace(LayoutType) ? "default layout" : LayoutType;
            return $"{section}; {layout}; {LayerCount} layer(s), {ActionCount} action(s), {MediaCueCount} media cue(s).";
        }
    }
}