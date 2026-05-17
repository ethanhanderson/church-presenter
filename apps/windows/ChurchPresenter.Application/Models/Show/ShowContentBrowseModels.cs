namespace ChurchPresenter.Models.Show;

/// <summary>
/// Complete browse projection for the active Show shell content slice.
/// </summary>
public sealed class ShowContentBrowseSnapshot
{
    /// <summary>Available catalog roots, including libraries and playlists.</summary>
    public IReadOnlyList<ShowCatalogSource> Sources { get; init; } = Array.Empty<ShowCatalogSource>();

    /// <summary>Presentations available in the selected catalog source.</summary>
    public IReadOnlyList<ShowPresentationBrowseItem> Presentations { get; init; } = Array.Empty<ShowPresentationBrowseItem>();

    /// <summary>Slides available in the selected presentation.</summary>
    public IReadOnlyList<ShowSlideBrowseItem> Slides { get; init; } = Array.Empty<ShowSlideBrowseItem>();

    /// <summary>Stable key of the selected catalog source.</summary>
    public string? SelectedSourceKey { get; init; }

    /// <summary>Path of the selected presentation, relative or absolute as stored by the catalog.</summary>
    public string? SelectedPresentationPath { get; init; }

    /// <summary>Title for the selected presentation, if one is open.</summary>
    public string SelectedPresentationTitle { get; init; } = string.Empty;

    /// <summary>Operator-facing status text for the browse surface.</summary>
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>True when the selected presentation was loaded successfully.</summary>
    public bool HasSelectedPresentation => !string.IsNullOrWhiteSpace(SelectedPresentationPath);

    /// <summary>Presentation path currently showing in the program output, when known.</summary>
    public string? LivePresentationPath { get; init; }

    /// <summary>Slide id currently showing in the program output, when known.</summary>
    public string? LiveSlideId { get; init; }
}

/// <summary>
/// A selectable library or playlist root in the Show catalog rail.
/// </summary>
public sealed class ShowCatalogSource
{
    /// <summary>Stable UI key. Uses prefixes such as <c>library:</c> and <c>playlist:</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Catalog item display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Catalog item type shown to the operator.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Number of presentation references under this source.</summary>
    public int PresentationCount { get; init; }

    /// <summary>True when this source is selected in the current snapshot.</summary>
    public bool IsSelected { get; init; }
}

/// <summary>
/// A presentation reference shown under the selected source.
/// </summary>
public sealed class ShowPresentationBrowseItem
{
    /// <summary>Stored presentation path.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Presentation title from the catalog reference.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Small operator-facing source/path summary.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>True when this presentation is selected.</summary>
    public bool IsSelected { get; init; }
}

/// <summary>
/// A slide entry that can be sent live by the active Show shell.
/// </summary>
public sealed class ShowSlideBrowseItem
{
    /// <summary>Slide identifier in the presentation bundle.</summary>
    public string SlideId { get; init; } = string.Empty;

    /// <summary>Owning presentation path.</summary>
    public string PresentationPath { get; init; } = string.Empty;

    /// <summary>One-based slide ordinal.</summary>
    public int Ordinal { get; init; }

    /// <summary>Operator-facing slide label.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Compact group/footer label, usually the slide section label.</summary>
    public string FooterLabel { get; init; } = string.Empty;

    /// <summary>Visible text-layer content used by the text grid and list view.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Operator-facing section/group key for filtering, grouping, and visual badges.</summary>
    public string SectionKey { get; init; } = string.Empty;

    /// <summary>True when the slide has a per-slide transition override.</summary>
    public bool HasTransitionOverride { get; init; }

    /// <summary>Operator-facing transition override label.</summary>
    public string TransitionLabel { get; init; } = string.Empty;

    /// <summary>True when this slide is currently live/program.</summary>
    public bool IsLive { get; init; }

    /// <summary>True when there is visible raw text for text-first deck views.</summary>
    public bool HasRawText => !string.IsNullOrWhiteSpace(RawText);

    /// <summary>Best available slide text for preview surfaces.</summary>
    public string DisplayText => HasRawText ? RawText : Title;

    /// <summary>True when the slide is disabled in the typed project.</summary>
    public bool Disabled { get; init; }
}