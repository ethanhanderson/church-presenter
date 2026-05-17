using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;

using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace ChurchPresenter.ViewModels;

public partial class ShowViewModel
{
    // ── Deck preference partial change handlers ──────────────────────────────

    partial void OnDeckViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(ShowThumbnailMode));
        OnPropertyChanged(nameof(ShowTextMode));
        OnPropertyChanged(nameof(ShowListMode));
        NotifyDeckVisibilityChanged();
        RefreshDeckViewModesOnSections();
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    partial void OnGroupBySectionChanged(bool value)
    {
        RebuildGroupedSections();
        RefreshDeckViewModesOnSections();
        NotifyDeckVisibilityChanged();
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    partial void OnDeckScaleStepChanged(int value)
    {
        OnPropertyChanged(nameof(DeckMinItemWidth));
        OnPropertyChanged(nameof(DeckListItemHeight));
        RefreshDeckLayoutMetrics();
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    partial void OnMediaPanelScaleStepChanged(int value)
    {
        OnPropertyChanged(nameof(MediaPanelGridMinItemWidth));
        OnPropertyChanged(nameof(MediaPanelGridMinItemHeight));
        OnPropertyChanged(nameof(MediaPanelListThumbWidth));
        OnPropertyChanged(nameof(MediaPanelListThumbHeight));
        OnPropertyChanged(nameof(MediaPanelListRowMinHeight));
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    partial void OnTransparentThumbnailColorChanged(string value)
    {
        OnPropertyChanged(nameof(TransparentThumbnailColorWinUI));
        ApplyThumbnailBgColorToAllItems();
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    partial void OnTransparentThumbnailOpacityChanged(int value)
    {
        ApplyThumbnailBgColorToAllItems();
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    partial void OnTransparentThumbnailBackgroundEnabledChanged(bool value)
    {
        ApplyThumbnailBgColorToAllItems();
        if (!_loadingDeckPreferences)
            _ = PersistDeckPreferencesAsync();
    }

    // ── Deck preference helpers ───────────────────────────────────────────────

    private void LoadDeckPreferences()
    {
        var s = _settings.Settings.Show;
        _loadingDeckPreferences = true;
        try
        {
            DeckViewMode = s.DeckViewMode switch
            {
                "text" => "text",
                "list" => "list",
                _ => "thumbnail"
            };
            GroupBySection = s.GroupBySection;
            TransparentThumbnailBackgroundEnabled = s.TransparentThumbnailBackgroundEnabled;
            TransparentThumbnailColor = string.IsNullOrWhiteSpace(s.TransparentThumbnailColor)
                ? "#000000"
                : s.TransparentThumbnailColor;
            TransparentThumbnailOpacity = Math.Clamp(s.TransparentThumbnailOpacity, 0, 100);
            DeckScaleStep = Math.Clamp(s.DeckScaleStep, 0, 4);
            MediaPanelScaleStep = Math.Clamp(s.MediaPanelScaleStep, 0, 7);
            MediaSeekSeconds = s.MediaSeekSeconds <= 0 ? 5 : Math.Clamp(s.MediaSeekSeconds, 1, 60);
            LoadTransitionToolbarFromSettings(s);
        }
        finally
        {
            _loadingDeckPreferences = false;
        }
    }

    private async Task PersistDeckPreferencesAsync()
    {
        _deckPrefDebounceCts?.Cancel();
        _deckPrefDebounceCts = new CancellationTokenSource();
        var token = _deckPrefDebounceCts.Token;
        try
        {
            await Task.Delay(400, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _settings.Update(s =>
        {
            s.Show.DeckViewMode = DeckViewMode;
            s.Show.GroupBySection = GroupBySection;
            s.Show.TransparentThumbnailBackgroundEnabled = TransparentThumbnailBackgroundEnabled;
            s.Show.TransparentThumbnailColor = TransparentThumbnailColor;
            s.Show.TransparentThumbnailOpacity = TransparentThumbnailOpacity;
            s.Show.DeckScaleStep = DeckScaleStep;
            s.Show.MediaPanelScaleStep = MediaPanelScaleStep;
            s.Show.MediaSeekSeconds = MediaSeekSeconds <= 0 ? 5 : Math.Clamp(MediaSeekSeconds, 1, 60);
            PersistTransitionToolbarToSettings(s);
        });
        await _settings.SaveAsync().ConfigureAwait(true);
    }

    private void RebuildGroupedSections()
    {
        SlideDeckGroupedSections.Clear();
        if (!GroupBySection || !ShowSingleDeckSlideGrid)
        {
            RefreshContiguousGroupFooterLabelsEverywhere();
            return;
        }

        PopulateGroupedSlideSections(SlideDeckGroupedSections, SlideDeckItems);
        foreach (var section in SlideDeckGroupedSections)
            ApplyDeckSettingsToSection(section);
    }

    /// <summary>
    /// Prefer the presentation's stored arrangement section-group membership when available.
    /// Fallback to contiguous SectionLabel/Section runs when older data has no arrangement groups.
    /// </summary>
    private static string BuildSectionRunKey(ShowSlideDeckItem item, IReadOnlyDictionary<string, SectionGroup> sectionGroupBySlideId)
    {
        if (sectionGroupBySlideId.TryGetValue(item.Slide.Id, out var sectionGroup)
            && !string.IsNullOrWhiteSpace(sectionGroup.Id))
        {
            return $"group:{sectionGroup.Id}";
        }

        if (!string.IsNullOrWhiteSpace(item.Slide.SectionLabel))
            return $"label:{item.Slide.SectionLabel.Trim().ToUpperInvariant()}";

        if (!string.IsNullOrWhiteSpace(item.Slide.Section))
            return $"section:{item.Slide.Section.Trim().ToUpperInvariant()}";

        return $"slide:{item.Slide.Id}";
    }

    /// <summary>
    /// Re-apply footer labels for the single deck and each browse-stack block when standard layout
    /// is active or grouped headers are cleared (first slide in each contiguous group only).
    /// </summary>
    private void RefreshContiguousGroupFooterLabelsEverywhere()
    {
        if (SlideDeckItems.Count > 0)
            ApplyContiguousGroupFooterLabels(SlideDeckItems.ToList());

        foreach (var section in BrowseStackSections)
        {
            if (section.SlideRows.Count > 0)
                ApplyContiguousGroupFooterLabels(section.SlideRows.ToList());
        }
    }

    /// <summary>
    /// First slide in each contiguous run (same arrangement group / section label) shows the group name in the footer;
    /// following slides keep the group tint only.
    /// </summary>
    private static void ApplyContiguousGroupFooterLabels(IReadOnlyList<ShowSlideDeckItem> items)
    {
        if (items.Count == 0)
            return;

        var lookup = BuildSectionGroupLookup(items);
        string? prevKey = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var key = BuildSectionRunKey(item, lookup);
            var showLabel = i == 0 || !string.Equals(key, prevKey, StringComparison.Ordinal);
            item.ShowFooterSectionLabel = showLabel;
            prevKey = key;
        }
    }

    private static string BuildSlideSectionLabel(PresentationSlide slide)
    {
        if (!string.IsNullOrWhiteSpace(slide.SectionLabel))
            return slide.SectionLabel.Trim();
        if (!string.IsNullOrWhiteSpace(slide.Section))
            return PresentationModelUtilities.FormatSectionLabel(slide.Section, slide.SectionIndex);
        return "Slides";
    }

    private static void PopulateGroupedSlideSections(
        ObservableCollection<ShowPresentationDeckSection> targetSections,
        IEnumerable<ShowSlideDeckItem> items)
    {
        var itemList = items.ToList();
        targetSections.Clear();
        var sectionGroupBySlideId = BuildSectionGroupLookup(itemList);

        ShowPresentationDeckSection? currentSection = null;
        string? currentSectionKey = null;

        foreach (var item in itemList)
        {
            var sectionKey = BuildSectionRunKey(item, sectionGroupBySlideId);
            var sectionLabel = BuildSlideSectionLabel(item.Slide);

            if (currentSection == null
                || !string.Equals(sectionKey, currentSectionKey, StringComparison.Ordinal))
            {
                currentSection = new ShowPresentationDeckSection(sectionLabel, null);
                targetSections.Add(currentSection);
                currentSectionKey = sectionKey;
            }

            currentSection.SlideRows.Add(item);
        }

        ApplyContiguousGroupFooterLabels(itemList);
    }

    private static Dictionary<string, SectionGroup> BuildSectionGroupLookup(IReadOnlyList<ShowSlideDeckItem> items)
    {
        var project = items.FirstOrDefault()?.ThumbnailProject;
        var sections = project?.Arrangement?.Sections;
        if (sections == null || sections.Count == 0)
            return new Dictionary<string, SectionGroup>(StringComparer.OrdinalIgnoreCase);

        var lookup = new Dictionary<string, SectionGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            foreach (var slideId in section.SlideIds)
            {
                if (!string.IsNullOrWhiteSpace(slideId))
                    lookup[slideId] = section;
            }
        }

        return lookup;
    }

    private void RefreshDeckViewModesOnSections()
    {
        var thumb = ShowThumbnailMode;
        var text = ShowTextMode;
        var list = ShowListMode;

        foreach (var s in BrowseStackSections)
        {
            s.ShowThumbnailView = thumb;
            s.ShowTextView = text;
            s.ShowListView = list;
            s.ShowLyricGroupHeaders = GroupBySection;
        }

        foreach (var s in SlideDeckGroupedSections)
        {
            s.ShowThumbnailView = thumb;
            s.ShowTextView = text;
            s.ShowListView = list;
        }
    }

    private void RefreshDeckLayoutMetrics()
    {
        var w = DeckMinItemWidth;
        var h = DeckListItemHeight;

        foreach (var item in SlideDeckItems)
            item.DeckListItemHeight = h;

        foreach (var s in BrowseStackSections)
        {
            s.DeckMinItemWidth = w;
            s.DeckListItemHeight = h;
            foreach (var item in s.SlideRows)
                item.DeckListItemHeight = h;
        }

        foreach (var s in SlideDeckGroupedSections)
        {
            s.DeckMinItemWidth = w;
            s.DeckListItemHeight = h;
            foreach (var item in s.SlideRows)
                item.DeckListItemHeight = h;
        }
    }

    private void ApplyThumbnailBgColorToAllItems()
    {
        var color = BuildThumbnailBgColor();
        foreach (var item in SlideDeckItems)
            item.ThumbnailBgColor = color;
        foreach (var s in BrowseStackSections)
            foreach (var item in s.SlideRows)
                item.ThumbnailBgColor = color;
        foreach (var s in SlideDeckGroupedSections)
            foreach (var item in s.SlideRows)
                item.ThumbnailBgColor = color;
    }

    private void ApplyDeckSettingsToItems(IEnumerable<ShowSlideDeckItem> items)
    {
        var color = BuildThumbnailBgColor();
        var h = DeckListItemHeight;
        var list = items.ToList();
        foreach (var item in list)
        {
            item.ThumbnailBgColor = color;
            item.DeckListItemHeight = h;
        }

        ApplyContiguousGroupFooterLabels(list);
    }

    private void ApplyDeckSettingsToSection(ShowPresentationDeckSection section)
    {
        section.DeckMinItemWidth = DeckMinItemWidth;
        section.DeckListItemHeight = DeckListItemHeight;
        section.ShowThumbnailView = ShowThumbnailMode;
        section.ShowTextView = ShowTextMode;
        section.ShowListView = ShowListMode;
        section.ShowLyricGroupHeaders = GroupBySection;
        ApplyDeckSettingsToItems(section.SlideRows);
    }

    /// <summary>Combines the stored RGB hex colour with the current opacity (0–100) into a Windows.UI.Color.</summary>
    private Color BuildThumbnailBgColor()
    {
        if (!TransparentThumbnailBackgroundEnabled)
            return Color.FromArgb(0, 0, 0, 0);

        var rgb = ParseHexToWinColor(TransparentThumbnailColor);
        var alpha = (byte)(Math.Clamp(TransparentThumbnailOpacity, 0, 100) * 255 / 100);
        return Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
    }

    private static Color ParseHexToWinColor(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6)
            return Color.FromArgb(255, 0, 0, 0);

        return Color.FromArgb(
            255,
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private void RebuildSidebarTree()
    {
        LibraryTreeItems.Clear();
        foreach (var lib in _catalog.Catalog.Libraries)
        {
            var row = new ShowLibraryTreeItem(lib);
            foreach (var p in lib.Presentations)
                row.PresentationRows.Add(new ShowPresentationTreeItem(p, libraryId: lib.Id));
            LibraryTreeItems.Add(row);
        }

        PlaylistTreeItems.Clear();
        foreach (var pl in _catalog.Catalog.Playlists)
        {
            var row = new ShowPlaylistTreeItem(pl);
            for (var index = 0; index < pl.Items.Count; index++)
            {
                var presentation = pl.Items[index];
                row.PresentationRows.Add(new ShowPresentationTreeItem(
                    presentation,
                    playlistId: pl.Id,
                    playlistIndex: index,
                    playlistCount: pl.Items.Count));
            }
            PlaylistTreeItems.Add(row);
        }

        RefreshTreeItemHighlights();
    }

    private void RefreshTreeItemHighlights()
    {
        foreach (var item in LibraryTreeItems)
        {
            item.IsHighlighted = LibraryRowShouldHighlight(item.Library);
            foreach (var row in item.PresentationRows)
                row.IsSelected = PresentationSidebarRowIsSelected(row);
        }

        foreach (var item in PlaylistTreeItems)
        {
            item.IsHighlighted = PlaylistRowShouldHighlight(item.Playlist);
            foreach (var row in item.PresentationRows)
                row.IsSelected = PresentationSidebarRowIsSelected(row);
        }

    }

    /// <summary>
    /// One sidebar selection: path matches only in the active source (library or playlist), not both.
    /// </summary>
    private bool PresentationSidebarRowIsSelected(ShowPresentationTreeItem row)
    {
        if (string.IsNullOrWhiteSpace(SelectedPresentationPath))
            return false;
        if (!PathsMatch(row.Presentation.Path, SelectedPresentationPath))
            return false;
        if (!string.IsNullOrWhiteSpace(row.LibraryId))
            return string.Equals(row.LibraryId, SelectedLibraryId, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(SelectedPlaylistId);
        if (!string.IsNullOrWhiteSpace(row.PlaylistId))
            return string.Equals(row.PlaylistId, SelectedPlaylistId, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(SelectedLibraryId);
        return false;
    }

    private bool LibraryRowShouldHighlight(LibraryDto lib)
    {
        if (!string.IsNullOrEmpty(SelectedPlaylistId))
            return false;
        if (string.IsNullOrEmpty(SelectedLibraryId)
            || !string.Equals(SelectedLibraryId, lib.Id, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(SelectedPresentationPath))
            return true;
        return !lib.Presentations.Exists(p => PathsMatch(p.Path, SelectedPresentationPath));
    }

    private bool PlaylistRowShouldHighlight(PlaylistDto pl)
    {
        if (!string.IsNullOrEmpty(SelectedLibraryId))
            return false;
        if (string.IsNullOrEmpty(SelectedPlaylistId)
            || !string.Equals(SelectedPlaylistId, pl.Id, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(SelectedPresentationPath))
            return true;
        return !pl.Items.Exists(p => PathsMatch(p.Path, SelectedPresentationPath));
    }

    private bool PathsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        try
        {
            var fullA = _content.ResolvePresentationPath(a);
            var fullB = _content.ResolvePresentationPath(b);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Path comparison for UI (e.g. scroll-to-section) — same rules as browse selection.</summary>
    public bool PresentationPathsMatch(string? a, string? b) => PathsMatch(a, b);

    private bool PathsMatchNullable(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return true;

        return PathsMatch(a, b);
    }

    private string? ResolveItemPresentationPath(ShowSlideDeckItem item) =>
        string.IsNullOrWhiteSpace(item.PresentationPath)
            ? GetCurrentOpenPresentationPath()
            : item.PresentationPath;

    private string? GetCurrentOpenPresentationPath() =>
        OpenDocument?.SourcePath ?? SelectedPresentationPath;

    private string? GetNavigationSlideId() =>
        !string.IsNullOrWhiteSpace(SelectedSlideId) ? SelectedSlideId : _engine.CurrentSlideId;

    private string? GetNavigationPresentationPath() =>
        !string.IsNullOrWhiteSpace(SelectedSlideId) ? SelectedSlidePresentationPath : _engine.PresentationPath;

    private string? GetNavigationSlideInstanceKey() =>
        !string.IsNullOrWhiteSpace(SelectedSlideId) ? _selectedSlideInstanceKey : _engine.CurrentSlideInstanceKey;

    private void ApplySlideSelectionState(
        string? slideId,
        string? presentationPath,
        string? instanceKey = null,
        bool? userOverride = null,
        IReadOnlyList<SlideDeckSelectionKey>? selectedKeys = null,
        SlideDeckSelectionKey? anchorKey = null)
    {
        var normalizedSlideId = string.IsNullOrWhiteSpace(slideId) ? null : slideId;
        var normalizedPath = normalizedSlideId == null
            ? null
            : string.IsNullOrWhiteSpace(presentationPath)
                ? GetCurrentOpenPresentationPath()
                : presentationPath;
        var normalizedInstanceKey = normalizedSlideId == null
            ? null
            : string.IsNullOrWhiteSpace(instanceKey)
                ? normalizedSlideId
                : instanceKey;
        var normalizedPrimaryKey = normalizedSlideId == null
            ? (SlideDeckSelectionKey?)null
            : new SlideDeckSelectionKey(normalizedPath, normalizedSlideId, normalizedInstanceKey!);
        var nextSelectionKeys = normalizedPrimaryKey == null
            ? Array.Empty<SlideDeckSelectionKey>()
            : NormalizeSelectionKeys(selectedKeys ?? new[] { normalizedPrimaryKey.Value });
        var nextAnchor = normalizedPrimaryKey == null
            ? (SlideDeckSelectionKey?)null
            : anchorKey ?? _selectionAnchor ?? normalizedPrimaryKey;

        var slideChanged = !string.Equals(_selectedSlideId, normalizedSlideId, StringComparison.OrdinalIgnoreCase);
        var pathChanged = !PathsMatchNullable(_selectedSlidePresentationPath, normalizedPath);
        var instanceChanged = !string.Equals(_selectedSlideInstanceKey, normalizedInstanceKey, StringComparison.OrdinalIgnoreCase);
        var keysChanged = !SelectionKeysEqual(_selectedSlideKeys, nextSelectionKeys);
        var anchorChanged = !NullableSelectionKeysEqual(_selectionAnchor, nextAnchor);
        if (slideChanged)
            _selectedSlideId = normalizedSlideId;
        if (pathChanged)
            _selectedSlidePresentationPath = normalizedPath;
        if (instanceChanged)
            _selectedSlideInstanceKey = normalizedInstanceKey;
        if (keysChanged)
        {
            _selectedSlideKeys.Clear();
            _selectedSlideKeys.AddRange(nextSelectionKeys);
        }
        if (anchorChanged)
            _selectionAnchor = nextAnchor;

        if (userOverride.HasValue && _userOverrideSlideSelection != userOverride.Value)
        {
            _userOverrideSlideSelection = userOverride.Value;
            OnPropertyChanged(nameof(UserOverrideSlideSelection));
            _engine.SetUserOverrideSelection(userOverride.Value);
        }

        if (!slideChanged && !pathChanged && !instanceChanged && !keysChanged && !anchorChanged)
            return;

        // Keep the engine's operator cursor in sync so backend adapters and other
        // consumers can access the operator selection without going through ShowViewModel.
        if (normalizedSlideId != null)
            _engine.SelectSlide(normalizedPath, normalizedSlideId, normalizedInstanceKey);
        else
            _engine.ClearSelection();

        _activePresentation.SetSelectedSlideId(normalizedSlideId);
        OnPropertyChanged(nameof(SelectedSlideId));
        OnPropertyChanged(nameof(SelectedSlidePresentationPath));
        RefreshAllSlideDeckState();
        OnPropertyChanged(nameof(SelectedSlide));
        OnPropertyChanged(nameof(SelectedDeckRowForView));
        NotifyPreviewState();
    }

    private IReadOnlyList<SlideDeckSelectionKey> NormalizeSelectionKeys(IEnumerable<SlideDeckSelectionKey> keys)
    {
        var normalized = new List<SlideDeckSelectionKey>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.SlideId))
                continue;

            var instanceKey = string.IsNullOrWhiteSpace(key.InstanceKey) ? key.SlideId : key.InstanceKey;
            var normalizedKey = new SlideDeckSelectionKey(key.PresentationPath, key.SlideId, instanceKey);
            if (!normalized.Any(existing => SelectionKeysEqual(existing, normalizedKey)))
                normalized.Add(normalizedKey);
        }

        return normalized;
    }

    private bool SelectionKeysEqual(IReadOnlyList<SlideDeckSelectionKey> left, IReadOnlyList<SlideDeckSelectionKey> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!SelectionKeysEqual(left[index], right[index]))
                return false;
        }

        return true;
    }

    private bool NullableSelectionKeysEqual(SlideDeckSelectionKey? left, SlideDeckSelectionKey? right)
    {
        if (left.HasValue != right.HasValue)
            return false;

        return !left.HasValue || SelectionKeysEqual(left.Value, right.GetValueOrDefault());
    }

    private bool SelectionKeysEqual(SlideDeckSelectionKey left, SlideDeckSelectionKey right) =>
        string.Equals(left.SlideId, right.SlideId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.InstanceKey, right.InstanceKey, StringComparison.OrdinalIgnoreCase)
        && PathsMatchNullable(left.PresentationPath, right.PresentationPath);

    /// <summary>Writes library/playlist selection and Show chrome layout (<see cref="OutputPanelWidth"/>) to persisted workspace.json.</summary>
    public Task SaveWorkspaceUiStateAsync() => PersistWorkspaceAsync();

    private async Task PersistWorkspaceAsync()
    {
        _workspace.Update(ws =>
        {
            ws.SelectedLibraryId = SelectedLibraryId;
            ws.SelectedPlaylistId = SelectedPlaylistId;
            ws.SelectedPresentationPath = SelectedPresentationPath;
            ws.ShowOutputPanelWidth = WorkspaceDto.NormalizeStoredShowOutputPanelWidth(OutputPanelWidth);
        });
        await _workspace.SaveAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void SelectLibrary(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        SelectedLibraryId = id;
        SelectedPlaylistId = null;
        NotifyCenterPanes();
        _ = PersistWorkspaceAsync();
    }

    [RelayCommand]
    private void SelectPlaylist(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        SelectedPlaylistId = id;
        SelectedLibraryId = null;
        NotifyCenterPanes();
        _ = PersistWorkspaceAsync();
    }

    [RelayCommand]
    private async Task SelectPresentationAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        await OpenPresentationFromPathAsync(path).ConfigureAwait(true);
    }

    /// <summary>Opens the owning presentation if needed and updates the operator selection.</summary>
    public Task OpenBrowseStackSlideAsync(string presentationPath, string slideId)
        => ActivateSlideSelectionAsync(presentationPath, slideId);

    /// <summary>Selects a slide using the visible show-deck path-aware flow without taking it live.</summary>
    public async Task ActivateSlideSelectionAsync(string? presentationPath, string slideId, string? instanceKey = null)
    {
        StopSlideSeek();
        ClearMediaPanelPreview();
        await ActivateSlideSelectionCoreAsync(presentationPath, slideId, instanceKey).ConfigureAwait(true);
    }

    /// <summary>Selects the visible range from the current selection anchor to the target slide without taking content live.</summary>
    public async Task SelectSlideRangeAsync(string? presentationPath, string slideId, string? instanceKey = null)
    {
        StopSlideSeek();
        ClearMediaPanelPreview();

        var anchor = _selectionAnchor ?? CreateSelectionKey(_selectedSlidePresentationPath, _selectedSlideId, _selectedSlideInstanceKey);
        await ActivateSlideSelectionCoreAsync(presentationPath, slideId, instanceKey).ConfigureAwait(true);

        var primary = CreateSelectionKey(_selectedSlidePresentationPath, _selectedSlideId, _selectedSlideInstanceKey);
        if (primary == null)
            return;

        anchor ??= primary;
        var visibleKeys = GetVisibleDeckSelectionKeys();
        var rangeKeys = BuildSelectionRange(visibleKeys, anchor.Value, primary.Value);
        ApplySlideSelectionState(
            primary.Value.SlideId,
            primary.Value.PresentationPath,
            primary.Value.InstanceKey,
            userOverride: true,
            selectedKeys: rangeKeys,
            anchorKey: anchor);
    }

    /// <summary>Clears only the operator slide-card selection. Live/program output remains unchanged.</summary>
    public void ClearSlideSelection()
    {
        if (string.IsNullOrWhiteSpace(SelectedSlideId) && _selectedSlideKeys.Count == 0)
            return;

        StopSlideSeek();
        ApplySlideSelectionState(null, null, userOverride: false);
    }

    private async Task ActivateSlideSelectionCoreAsync(string? presentationPath, string slideId, string? instanceKey = null)
    {
        if (string.IsNullOrWhiteSpace(slideId))
            return;

        var targetPresentationPath = string.IsNullOrWhiteSpace(presentationPath)
            ? GetCurrentOpenPresentationPath()
            : presentationPath;

        if (!string.IsNullOrWhiteSpace(targetPresentationPath)
            && (OpenDocument == null || !PathsMatch(targetPresentationPath, OpenDocument.SourcePath)))
        {
            // GetOrLoad guarantees the document is available even if the background prefetch
            // hasn't written to the cache yet.  TryGet can return null during the brief window
            // between when a browse-stack section is rendered and when the background Task.Run
            // completes its ConcurrentDictionary write, which would fall through to
            // OpenPresentationFromPathAsync — triggering GoLive (engine reset) before we
            // get to SwitchToPresentation, causing a blank-frame and UI flicker.
            var cached = _sessionCache.GetOrLoad(targetPresentationPath);
            if (cached != null)
                await SetFocusedDocumentAsync(cached).ConfigureAwait(true);
            else
            {
                var opened = await OpenPresentationFromPathAsync(targetPresentationPath).ConfigureAwait(true);
                if (!opened)
                    return;
            }
        }

        // Canonical path on the open document avoids string form mismatches in deck row paths.
        var normalizedPath = OpenDocument != null
            && !string.IsNullOrWhiteSpace(targetPresentationPath)
            && PathsMatch(targetPresentationPath, OpenDocument.SourcePath)
            ? OpenDocument.SourcePath
            : targetPresentationPath;

        // SetFocusedDocumentAsync rebuilds SlideDeckItems from the active arrangement; instance keys
        // then differ from browse-stack seed rows (which used slide.Id). Re-align before applying
        // selection so IsSlideItemSelected matches the visible rows on the first cross-presentation click.
        var resolvedInstanceKey = ResolveInstanceKeyForCurrentDeck(normalizedPath, slideId, instanceKey);
        SelectSlideInDeck(slideId, normalizedPath, resolvedInstanceKey);
        var prepared = await _cuePreparation.PrepareSlideCueAsync(normalizedPath, slideId, resolvedInstanceKey, OpenDocument).ConfigureAwait(true);
        if (prepared != null)
            PreWarmSlideCueMedia(prepared);
    }

    /// <summary>
    /// Maps a hinted instance key from pre-focus UI rows to the key used in <see cref="SlideDeckItems"/>
    /// after <see cref="SetFocusedDocumentAsync"/> rebuilds the deck from the playback sequence.
    /// </summary>
    private string? ResolveInstanceKeyForCurrentDeck(string? presentationPath, string slideId, string? hintedInstanceKey)
    {
        if (string.IsNullOrWhiteSpace(slideId) || SlideDeckItems.Count == 0)
            return hintedInstanceKey;

        var path = string.IsNullOrWhiteSpace(presentationPath)
            ? GetCurrentOpenPresentationPath()
            : presentationPath;

        var matches = SlideDeckItems
            .Where(item =>
                string.Equals(item.Slide.Id, slideId, StringComparison.OrdinalIgnoreCase)
                && PathsMatch(ResolveItemPresentationPath(item), path))
            .ToList();

        if (matches.Count == 0)
            return hintedInstanceKey;

        if (!string.IsNullOrWhiteSpace(hintedInstanceKey))
        {
            var hinted = matches.FirstOrDefault(m =>
                string.Equals(m.InstanceKey, hintedInstanceKey, StringComparison.OrdinalIgnoreCase));
            if (hinted != null)
                return hinted.InstanceKey;
        }

        var enabled = matches.FirstOrDefault(m => !m.Slide.Disabled);
        return (enabled ?? matches[0]).InstanceKey;
    }

    private static SlideDeckSelectionKey? CreateSelectionKey(string? presentationPath, string? slideId, string? instanceKey)
    {
        if (string.IsNullOrWhiteSpace(slideId))
            return null;

        return new SlideDeckSelectionKey(
            presentationPath,
            slideId,
            string.IsNullOrWhiteSpace(instanceKey) ? slideId : instanceKey);
    }

    private List<SlideDeckSelectionKey> GetVisibleDeckSelectionKeys()
    {
        var keys = new List<SlideDeckSelectionKey>();
        if (ShowBrowseStack && HasBrowseStackContent)
        {
            foreach (var section in BrowseStackSections)
            {
                foreach (var item in section.SlideRows)
                    keys.Add(new SlideDeckSelectionKey(section.PresentationPath, item.Slide.Id, item.InstanceKey));
            }

            return keys;
        }

        foreach (var item in SlideDeckItems)
            keys.Add(new SlideDeckSelectionKey(ResolveItemPresentationPath(item), item.Slide.Id, item.InstanceKey));

        return keys;
    }

    private IReadOnlyList<SlideDeckSelectionKey> BuildSelectionRange(
        IReadOnlyList<SlideDeckSelectionKey> visibleKeys,
        SlideDeckSelectionKey anchor,
        SlideDeckSelectionKey target)
    {
        var anchorIndex = FindSelectionKeyIndex(visibleKeys, anchor);
        var targetIndex = FindSelectionKeyIndex(visibleKeys, target);
        if (anchorIndex < 0 || targetIndex < 0)
            return new[] { target };

        var start = Math.Min(anchorIndex, targetIndex);
        var end = Math.Max(anchorIndex, targetIndex);
        return visibleKeys.Skip(start).Take(end - start + 1).ToList();
    }

    private int FindSelectionKeyIndex(IReadOnlyList<SlideDeckSelectionKey> keys, SlideDeckSelectionKey target)
    {
        for (var index = 0; index < keys.Count; index++)
        {
            if (SelectionKeysEqual(keys[index], target))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Updates <see cref="OpenDocument"/> to <paramref name="doc"/> and rebuilds the deck state
    /// without triggering a full <see cref="OpenPresentationPathAsync"/> round-trip.
    /// This is the lightweight path used when a session-cached document is already in memory.
    /// </summary>
    private Task SetFocusedDocumentAsync(PresentationDocument doc, bool clearSelection = true)
    {
        // Fast path: same cached instance already in focus.  The caller will call
        // SelectSlideInDeck immediately after, so no collection rebuild is needed —
        // the existing Slides/SlideDeckItems are already correct for this document.
        if (ReferenceEquals(OpenDocument, doc))
            return Task.CompletedTask;

        OpenDocument = doc;
        ApplyPresentationReferencePreferences(doc, ResolvePresentationReferenceForCurrentContext(doc.SourcePath));

        // Single-notification bulk replace: the UI sees one Reset event instead of
        // a Clear (all items vanish) followed by N individual Add events (items appear
        // one by one), eliminating the flash-blank-then-fill flicker.
        Slides.ReplaceAll(doc.Project?.Slides);

        RebuildPlaybackSequence();

        if (SelectedLibrary != null || SelectedPlaylist != null)
        {
            // When the browse stack already has sections (e.g. navigating within a playlist
            // or library), avoid clearing and rebuilding it.  Tearing BrowseStackSections down
            // momentarily makes HasBrowseStackContent false, causing the keyboard seek loop's
            // step-provider to fall back to MoveSlideSelection and lose cross-presentation
            // awareness for the next key repeat.
            // Instead, sync the active-section flag and refresh the header for the newly
            // focused presentation — all existing section slides remain intact.
            if (BrowseStackSections.Count > 0)
            {
                SyncActiveSectionData();
                var focusedSection = BrowseStackSections.FirstOrDefault(s =>
                    PathsMatch(s.PresentationPath, doc.SourcePath));
                if (focusedSection != null)
                    RefreshBrowseStackSectionHeaderState(focusedSection);
            }
            else
            {
                RefreshBrowseStackFromSelection();
            }

            RebuildSlideDeckItems();
        }
        else
        {
            RebuildSlideDeckItems();
        }

        if (clearSelection)
            ApplySlideSelectionState(null, null, userOverride: false);
        else
            RefreshAllSlideDeckState();
        NotifyCenterPanes();
        NotifyArrangementState();
        _activePresentation.SetCurrentPresentation(doc.Project, doc.SourcePath);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenPresentationPickerAsync(CancellationToken cancellationToken)
    {
        await PickAndOpenPresentationAsync().ConfigureAwait(true);
    }

    private async Task<bool> OpenPresentationPathAsync(string path)
    {
        try
        {
            // Load via cache so the document is shared with navigation paths.
            var doc = _sessionCache.GetOrLoad(path) ?? _presentationDocs.Open(path);
            _sessionCache.UpdateEntry(path, doc);
            OpenDocument = doc;
            ApplyPresentationReferencePreferences(doc, ResolvePresentationReferenceForCurrentContext(doc.SourcePath));
            Slides.ReplaceAll(doc.Project?.Slides);

            RebuildPlaybackSequence();

            if (SelectedLibrary != null || SelectedPlaylist != null)
            {
                RefreshBrowseStackFromSelection();
                RebuildSlideDeckItems();
            }
            else
            {
                RebuildSlideDeckItems();
            }

            ApplySlideSelectionState(null, null, userOverride: false);
            NotifyCenterPanes();
            NotifyArrangementState();
            StatusMessage = $"Opened {doc.Manifest.Title}";
            _activePresentation.SetCurrentPresentation(doc.Project, doc.SourcePath);

            await UpdateRecentFilesAsync(doc).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Open failed: {ex.Message}";
            return false;
        }
    }

    private void TryOpenAudienceWindows()
    {
        if (!_engine.IsAudienceEnabled)
        {
            _outputWindows.CloseAll();
            return;
        }

        _outputWindows.OpenAudience();
    }

    private void TryOpenStageWindows()
    {
        if (!_engine.IsStageEnabled)
        {
            _outputWindows.CloseStage();
            _engine.SetStageEnabled(false);
            return;
        }

        _outputWindows.OpenStage();
        _engine.SetStageEnabled(true);
    }

    private void SelectSlideInDeck(string? slideId, string? presentationPath = null, string? instanceKey = null)
    {
        if (string.IsNullOrWhiteSpace(slideId))
            return;

        ApplySlideSelectionState(slideId, presentationPath, instanceKey, userOverride: true);
    }

    /// <summary>Takes the current operator selection live without changing the browse/navigation model.</summary>
    public async Task<bool> TakeSelectedSlideLiveAsync()
    {
        StopSlideSeek();
        if (!string.IsNullOrWhiteSpace(SelectedSlideId))
        {
            await TakeSlideLiveAsync(SelectedSlidePresentationPath, SelectedSlideId!, _selectedSlideInstanceKey).ConfigureAwait(true);
            return true;
        }

        if (ShowBrowseStack && HasBrowseStackContent)
        {
            var firstEntry = GetBrowseStackEntries().FirstOrDefault(entry => !entry.Disabled);
            if (!string.IsNullOrWhiteSpace(firstEntry.SlideId))
            {
                await TakeSlideLiveAsync(firstEntry.PresentationPath, firstEntry.SlideId, firstEntry.InstanceKey).ConfigureAwait(true);
                return true;
            }
        }
        else if (SlideDeckItems.Count > 0)
        {
            var firstItem = SlideDeckItems.FirstOrDefault(item => !item.Slide.Disabled) ?? SlideDeckItems[0];
            await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), firstItem.Slide.Id, firstItem.InstanceKey).ConfigureAwait(true);
            return true;
        }

        return false;
    }

    /// <summary>Opens the owning presentation if needed and sends the selected slide to the live/output engine.</summary>
    public async Task TakeSlideLiveAsync(string? presentationPath, string slideId, string? instanceKey = null)
    {
        StopSlideSeek();
        if (string.IsNullOrWhiteSpace(slideId))
            return;

        ClearMediaPanelPreview();

        var targetPresentationPath = string.IsNullOrWhiteSpace(presentationPath)
            ? GetCurrentOpenPresentationPath()
            : presentationPath;

        // Resolve the target document — session cache first, then fall back to full open.
        PresentationDocument? targetDoc = null;
        if (!string.IsNullOrWhiteSpace(targetPresentationPath))
        {
            targetDoc = _sessionCache.TryGet(targetPresentationPath)
                        ?? _sessionCache.GetOrLoad(targetPresentationPath);
        }

        if (targetDoc == null)
        {
            if (string.IsNullOrWhiteSpace(targetPresentationPath))
            {
                targetDoc = OpenDocument;
            }
            else
            {
                try
                {
                    targetDoc = _presentationDocs.Open(targetPresentationPath);
                    _sessionCache.UpdateEntry(targetPresentationPath, targetDoc);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Open failed: {ex.Message}";
                    return;
                }
            }
        }

        if (targetDoc != null
            && (OpenDocument == null || !PathsMatch(targetDoc.SourcePath, OpenDocument.SourcePath)))
        {
            // Update focused document without converting the activation click into a selection change.
            await SetFocusedDocumentAsync(targetDoc, clearSelection: false).ConfigureAwait(true);
        }

        if (targetDoc == null)
            return;

        var normalizedPath = OpenDocument != null
            && !string.IsNullOrWhiteSpace(targetPresentationPath)
            && PathsMatch(targetPresentationPath, OpenDocument.SourcePath)
            ? OpenDocument.SourcePath
            : targetPresentationPath;

        var resolvedInstanceKey = ResolveInstanceKeyForCurrentDeck(normalizedPath, slideId, instanceKey);

        var preparedCue = _cuePreparation.GetPreparedSlideCue(normalizedPath, slideId, resolvedInstanceKey)
            ?? await _cuePreparation.PrepareSlideCueAsync(normalizedPath, slideId, resolvedInstanceKey, targetDoc).ConfigureAwait(true);
        if (preparedCue == null)
            return;

        _engine.EnterPreparedSlideCue(preparedCue with
        {
            LayerKind = ResolvePresentationDestinationLayer(normalizedPath),
        });
        _liveProduction.ReleaseClearedLayers([ResolvePresentationDestinationLayer(normalizedPath)]);

        RefreshAllSlideDeckState();
        NotifyPreviewState();

        _slideActions.ExecuteForSlide(
            targetDoc.Project?.Slides.FirstOrDefault(slide => string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase)));

        // Warm the next presentations in the session so they're ready when the operator advances.
        _sessionCache.SchedulePrefetch(normalizedPath ?? "");
    }

    private async Task UpdateRecentFilesAsync(PresentationDocument document)
    {
        var entry = new PresentationRefDto
        {
            Path = _content.ToContentRelativePath(document.SourcePath),
            Title = document.Manifest.Title,
            UpdatedAt = document.Manifest.UpdatedAt ?? DateTime.UtcNow.ToString("O"),
        };

        _settings.Update(settings =>
        {
            settings.RecentFiles.RemoveAll(existing =>
                string.Equals(existing.Path.Replace('\\', '/'), entry.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            settings.RecentFiles.Insert(0, entry);
            if (settings.RecentFiles.Count > settings.MaxRecentFiles)
                settings.RecentFiles = settings.RecentFiles.Take(settings.MaxRecentFiles).ToList();
        });

        await _settings.SaveAsync().ConfigureAwait(true);
    }

    private void OnLiveChanged()
    {
        NotifyPreviewState();
        OnPropertyChanged(nameof(AudienceOutputEnabled));
        RefreshAllSlideDeckState();
    }

    /// <summary>Keyboard navigation for slide selection and live advancement.</summary>
    public async Task<bool> HandleKeyAsync(VirtualKey key)
    {
        if (Slides.Count == 0 && BrowseStackSections.Count == 0)
            return false;

        if (await TryTakeHotKeySlideAsync(key).ConfigureAwait(true))
            return true;

        switch (key)
        {
            case VirtualKey.Space:
            case VirtualKey.Enter:
                return await TakeSelectedSlideLiveAsync().ConfigureAwait(true);
        }

        return false;
    }

    /// <summary>Starts or continues held-key slide seeking until <see cref="StopSlideSeek"/> is called.</summary>
    public Task<bool> StartSlideSeekAsync(VirtualKey key)
    {
        int direction;
        switch (key)
        {
            case VirtualKey.Right:
            case VirtualKey.PageDown:
                direction = 1;
                break;
            case VirtualKey.Left:
            case VirtualKey.PageUp:
            case VirtualKey.Back:
                direction = -1;
                break;
            default:
                return Task.FromResult(false);
        }

        if (Slides.Count == 0 && BrowseStackSections.Count == 0)
            return Task.FromResult(false);

        return _engine.StartSeekAsync(direction, async dir =>
        {
            var result = ShowBrowseStack && HasBrowseStackContent
                ? await MoveBrowseStackSelectionAsync(dir).ConfigureAwait(true)
                : await MoveSlideSelectionAsync(dir).ConfigureAwait(true);
            return result;
        });
    }

    /// <summary>Stops any in-progress held-key slide seeking.</summary>
    public void StopSlideSeek() => _engine.StopSeek();

    private async Task<SlideSeekStepResult> MoveBrowseStackSelectionAsync(int direction)
    {
        var entries = GetBrowseStackEntries();
        if (entries.Count == 0)
            return SlideSeekStepResult.None;

        var navigationSlideId = GetNavigationSlideId();
        var navigationPresentationPath = GetNavigationPresentationPath();
        var navigationInstanceKey = GetNavigationSlideInstanceKey();

        if (string.IsNullOrWhiteSpace(navigationSlideId))
        {
            var initialEntry = entries.FirstOrDefault(entry => !entry.Disabled);
            if (string.IsNullOrWhiteSpace(initialEntry.SlideId))
                initialEntry = entries[0];
            await TakeSlideLiveAsync(initialEntry.PresentationPath, initialEntry.SlideId, initialEntry.InstanceKey).ConfigureAwait(true);
            return SlideSeekStepResult.FromDelay(GetSelectionSeekDelay(initialEntry.PresentationPath, initialEntry.SlideId));
        }

        var currentIndex = entries.FindIndex(entry =>
            string.Equals(entry.InstanceKey, navigationInstanceKey ?? navigationSlideId, StringComparison.OrdinalIgnoreCase)
            && PathsMatch(entry.PresentationPath, navigationPresentationPath));

        if (currentIndex < 0)
        {
            var fallbackEntry = entries.FirstOrDefault(entry => !entry.Disabled);
            if (string.IsNullOrWhiteSpace(fallbackEntry.SlideId))
                fallbackEntry = entries[0];
            await TakeSlideLiveAsync(fallbackEntry.PresentationPath, fallbackEntry.SlideId, fallbackEntry.InstanceKey).ConfigureAwait(true);
            return SlideSeekStepResult.FromDelay(GetSelectionSeekDelay(fallbackEntry.PresentationPath, fallbackEntry.SlideId));
        }

        var nextIndex = currentIndex;
        while (true)
        {
            var candidateIndex = Math.Clamp(nextIndex + direction, 0, entries.Count - 1);
            if (candidateIndex == nextIndex)
                return SlideSeekStepResult.None;
            if (!entries[candidateIndex].Disabled)
            {
                nextIndex = candidateIndex;
                break;
            }

            nextIndex = candidateIndex;
        }

        if (nextIndex == currentIndex)
            return SlideSeekStepResult.None;

        var nextEntry = entries[nextIndex];
        await TakeSlideLiveAsync(nextEntry.PresentationPath, nextEntry.SlideId, nextEntry.InstanceKey).ConfigureAwait(true);
        return SlideSeekStepResult.FromDelay(GetSelectionSeekDelay(nextEntry.PresentationPath, nextEntry.SlideId));
    }

    private async Task<SlideSeekStepResult> MoveSlideSelectionAsync(int direction)
    {
        if (SlideDeckItems.Count == 0)
            return SlideSeekStepResult.None;

        var navigationSlideId = GetNavigationSlideId();
        var navigationInstanceKey = GetNavigationSlideInstanceKey();

        if (string.IsNullOrEmpty(navigationSlideId))
        {
            var firstEnabled = SlideDeckItems.FirstOrDefault(item => !item.Slide.Disabled) ?? SlideDeckItems[0];
            await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), firstEnabled.Slide.Id, firstEnabled.InstanceKey).ConfigureAwait(true);
            return SlideSeekStepResult.FromDelay(GetSelectionSeekDelay(GetCurrentOpenPresentationPath(), firstEnabled.Slide.Id));
        }

        var currentIndex = SlideDeckItems.ToList().FindIndex(item =>
            string.Equals(item.InstanceKey, navigationInstanceKey ?? navigationSlideId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            var firstEnabled = SlideDeckItems.FirstOrDefault(item => !item.Slide.Disabled) ?? SlideDeckItems[0];
            await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), firstEnabled.Slide.Id, firstEnabled.InstanceKey).ConfigureAwait(true);
            return SlideSeekStepResult.FromDelay(GetSelectionSeekDelay(GetCurrentOpenPresentationPath(), firstEnabled.Slide.Id));
        }

        var nextIndex = FindNextEnabledDeckItemIndex(SlideDeckItems, currentIndex, direction);
        if (nextIndex == currentIndex)
            return SlideSeekStepResult.None;

        var nextItem = SlideDeckItems[nextIndex];
        await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), nextItem.Slide.Id, nextItem.InstanceKey).ConfigureAwait(true);
        return SlideSeekStepResult.FromDelay(GetSelectionSeekDelay(GetCurrentOpenPresentationPath(), nextItem.Slide.Id));
    }

    private List<BrowseStackSlideEntry> GetBrowseStackEntries()
    {
        var entries = new List<BrowseStackSlideEntry>();
        foreach (var section in BrowseStackSections)
        {
            foreach (var item in section.SlideRows)
                entries.Add(new BrowseStackSlideEntry(section.PresentationPath, item.Slide.Id, item.InstanceKey, item.Slide.Disabled));
        }

        return entries;
    }

    private readonly record struct BrowseStackSlideEntry(string PresentationPath, string SlideId, string InstanceKey, bool Disabled);

    private static int FindNextEnabledDeckItemIndex(IReadOnlyList<ShowSlideDeckItem> items, int currentIndex, int direction)
    {
        if (items.Count == 0)
            return currentIndex;

        var index = currentIndex;
        while (true)
        {
            var nextIndex = Math.Clamp(index + direction, 0, items.Count - 1);
            if (nextIndex == index)
                return currentIndex;
            if (!items[nextIndex].Slide.Disabled)
                return nextIndex;

            index = nextIndex;
        }
    }

    private TimeSpan GetSelectionSeekDelay(string? presentationPath, string? slideId)
    {
        if (string.IsNullOrWhiteSpace(slideId))
            return TimeSpan.Zero;

        var project = ResolveProjectForSelection(presentationPath);
        var slide = project?.Slides.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, slideId, StringComparison.OrdinalIgnoreCase));
        var transition = TransitionResolver.Resolve(slide, project?.Arrangement, _transitionDefaults.GlobalSlideFallback);
        if (transition == null
            || string.Equals(transition.Type, "cut", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.Zero;
        }

        var durationMs = transition.Duration > 0
            ? transition.Duration
            : DefaultSelectionTransitionDurationMs;
        return TimeSpan.FromMilliseconds(durationMs + SelectionSeekTransitionBufferMs);
    }

    private PresentationProject? ResolveProjectForSelection(string? presentationPath)
    {
        if (string.IsNullOrWhiteSpace(presentationPath)
            || (OpenDocument != null && PathsMatch(presentationPath, OpenDocument.SourcePath)))
        {
            return OpenProject;
        }

        return GetProjectForPath(presentationPath);
    }

    private async Task<bool> TryTakeHotKeySlideAsync(VirtualKey key)
    {
        if (OpenProject == null)
            return false;

        var normalized = key.ToString();
        var slide = OpenProject.Slides.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.HotKey)
            && string.Equals(candidate.HotKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (slide == null)
            return false;

        await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), slide.Id, slide.Id).ConfigureAwait(true);
        return true;
    }

    public int GetSlideNumber(string? slideId)
    {
        if (string.IsNullOrWhiteSpace(slideId))
            return 0;

        for (var index = 0; index < Slides.Count; index++)
        {
            if (string.Equals(Slides[index].Id, slideId, StringComparison.OrdinalIgnoreCase))
                return index + 1;
        }

        return 0;
    }

    private void NotifyPreviewState()
    {
        OnPropertyChanged(nameof(SelectedSlide));
        OnPropertyChanged(nameof(LiveSlide));
        foreach (ShowClearActionViewModel action in OutputClearActions)
            action.ExecuteCommand.NotifyCanExecuteChanged();
    }

}