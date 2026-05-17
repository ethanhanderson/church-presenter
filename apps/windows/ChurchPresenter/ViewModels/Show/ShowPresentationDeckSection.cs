using System.Collections.ObjectModel;


using CommunityToolkit.Mvvm.ComponentModel;

namespace ChurchPresenter.ViewModels;
/// <summary>
/// One presentation block in the Show browse stack: header plus slide thumbnails (ProPresenter-style).
/// Carries deck view-mode and scale signals so DataTemplates can bind without knowing the page ViewModel.
/// Arrangement strip and header actions use per-section state; <see cref="IsActive"/> only reflects
/// slide/workspace selection for highlighting. Section-group chips use <see cref="ArrangementGroupChips"/>.
/// </summary>
public sealed partial class ShowPresentationDeckSection : ObservableObject
{
    public ShowPresentationDeckSection(string title, string? presentationPath)
    {
        Title = title;
        PresentationPath = presentationPath ?? string.Empty;
    }

    /// <summary>Display title (typically manifest title or section name).</summary>
    public string Title { get; }

    /// <summary>Resolved path used to open the document and match the active deck. May be empty for section groups.</summary>
    public string PresentationPath { get; }

    public BulkObservableCollection<ShowSlideDeckItem> SlideRows { get; } = new();
    public ObservableCollection<ShowPresentationDeckSection> GroupedSlideSections { get; } = new();

    // ── Active-presentation controls ─────────────────────────────────────────

    /// <summary>True when this section represents the currently open presentation.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Name of the active arrangement (e.g. Master); set when <see cref="IsActive"/>.</summary>
    [ObservableProperty]
    private string _activeArrangementName = string.Empty;

    /// <summary>Colored section-group chips for this section’s active arrangement (browse-stack bar).</summary>
    public ObservableCollection<SectionGroupChipDisplay> ArrangementGroupChips { get; } = new();

    /// <summary>Named arrangements for this presentation (browse-stack arrangement picker).</summary>
    public ObservableCollection<NamedArrangement> Arrangements { get; } = new();

    /// <summary>Two-way target for the browse-stack arrangement ComboBox for this presentation only.</summary>
    [ObservableProperty]
    private NamedArrangement? _arrangementPickerSelectedItem;

    /// <summary>When true, the full-width section-group strip card is visible below the header (browse stack, active row).</summary>
    [ObservableProperty]
    private bool _arrangementSectionExpanded;

    /// <summary>Auto-advance interval in seconds (0 = disabled). Propagated from the active presentation.</summary>
    [ObservableProperty]
    private int _autoAdvanceSeconds;

    partial void OnAutoAdvanceSecondsChanged(int value) =>
        OnPropertyChanged(nameof(IsAutoAdvanceEnabled));

    /// <summary>True when auto-advance is enabled for this section's presentation.</summary>
    public bool IsAutoAdvanceEnabled => AutoAdvanceSeconds > 0;

    /// <summary>True when this presentation has a saved default slide transition (not relying on implicit defaults).</summary>
    [ObservableProperty]
    private bool _hasDefaultSlideTransition;

    /// <summary>Duration estimate (e.g. "4:10") next to the clock icon when auto-advance is on.</summary>
    [ObservableProperty]
    private string _presentationDurationLabel = string.Empty;

    // ── View-mode signals propagated from ShowViewModel ──────────────────────

    [ObservableProperty]
    private bool _showThumbnailView = true;

    [ObservableProperty]
    private bool _showTextView;

    [ObservableProperty]
    private bool _showListView;

    [ObservableProperty]
    private bool _showLyricGroupHeaders;

    public bool ShowStandardThumbnailView => ShowThumbnailView && !ShowLyricGroupHeaders;
    public bool ShowStandardTextView => ShowTextView && !ShowLyricGroupHeaders;
    public bool ShowStandardListView => ShowListView && !ShowLyricGroupHeaders;
    public bool ShowGroupedThumbnailView => ShowThumbnailView && ShowLyricGroupHeaders;
    public bool ShowGroupedTextView => ShowTextView && ShowLyricGroupHeaders;
    public bool ShowGroupedListView => ShowListView && ShowLyricGroupHeaders;

    partial void OnShowThumbnailViewChanged(bool value) => NotifyDeckLayoutVisibilityChanged();
    partial void OnShowTextViewChanged(bool value) => NotifyDeckLayoutVisibilityChanged();
    partial void OnShowListViewChanged(bool value) => NotifyDeckLayoutVisibilityChanged();
    partial void OnShowLyricGroupHeadersChanged(bool value) => NotifyDeckLayoutVisibilityChanged();

    /// <summary>Minimum item width in px for the thumbnail/text grid, driven by DeckScaleStep.</summary>
    [ObservableProperty]
    private double _deckMinItemWidth = 220;

    /// <summary>Height of each list-view row (thumbnail height), driven by DeckScaleStep.</summary>
    [ObservableProperty]
    private double _deckListItemHeight = 70;

    private void NotifyDeckLayoutVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowStandardThumbnailView));
        OnPropertyChanged(nameof(ShowStandardTextView));
        OnPropertyChanged(nameof(ShowStandardListView));
        OnPropertyChanged(nameof(ShowGroupedThumbnailView));
        OnPropertyChanged(nameof(ShowGroupedTextView));
        OnPropertyChanged(nameof(ShowGroupedListView));
    }
}