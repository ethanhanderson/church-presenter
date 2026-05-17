using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ChurchPresenter.Backend.Rendering;

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
    // ── Media panel ──────────────────────────────────────────────────────────

    /// <summary>Loads or reloads media playlists into <see cref="MediaPlaylists"/>.</summary>
    public async Task LoadMediaPlaylistsAsync(CancellationToken ct = default)
    {
        var playlists = await _mediaLibrary.GetPlaylistsAsync(ct).ConfigureAwait(true);
        var rootItems = await _mediaLibrary.GetRootItemsAsync(ct).ConfigureAwait(true);

        _mediaRootItems.Clear();
        _mediaRootItems.AddRange(rootItems);

        MediaPlaylists.Clear();
        foreach (var pl in playlists)
            MediaPlaylists.Add(pl);

        RefreshMediaPanelItems();
        PrimeDrawerCache();
    }

    /// <summary>
    /// Schedules a background pass through all currently visible media panel items so their
    /// files are opened once by a temporary <see cref="Windows.Media.Playback.MediaPlayer"/>.
    /// After that first open the OS file cache and media-framework decoder state remain warm,
    /// cutting subsequent real loads from ~200 ms to ~20 ms.
    /// Only video and audio items are primed; image thumbnails open near-instantly anyway.
    /// </summary>
    private void PrimeDrawerCache()
    {
        var items = new List<(Uri uri, bool loop)>(MediaPanelItems.Count);
        foreach (var vm in MediaPanelItems)
        {
            try
            {
                var path = _mediaLibrary.ResolveStoredMediaPath(vm.MediaItem.Path);
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    continue;

                var type = MediaInference.ResolveEffectiveMediaType(vm.MediaItem.Type, path);
                if (!string.Equals(type, "video", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
                    continue;

                items.Add((new Uri(path), vm.MediaItem.CueDefaults.Loop));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "PrimeDrawerCache: skipped item {Id}.", vm.Id);
            }
        }

        _cachePrimer.PrimeItems(items);
    }

    /// <summary>Refilters <see cref="MediaPanelItems"/> from the current playlist selection and search text.</summary>
    public void RefreshMediaPanelItems()
    {
        MediaPanelItems.Clear();

        IEnumerable<MediaLibraryItem> source;
        if (!string.IsNullOrWhiteSpace(MediaPanelSelectedPlaylistId))
        {
            var playlist = MediaPlaylists.FirstOrDefault(p =>
                string.Equals(p.Id, MediaPanelSelectedPlaylistId, StringComparison.OrdinalIgnoreCase));
            source = playlist?.Items ?? Enumerable.Empty<MediaLibraryItem>();
        }
        else
        {
            source = _mediaRootItems.Concat(MediaPlaylists.SelectMany(p => p.Items));
        }

        source = source.Where(i => i is not null);

        var searchText = MediaPanelSearchText?.Trim() ?? "";
        if (searchText.Length > 0)
        {
            source = source.Where(i =>
                (i.Name ?? "").Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || (i.Path ?? "").Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        var displayIndex = 1;
        foreach (var item in source)
        {
            var vm = new MediaPanelItemViewModel(item, _mediaLibrary) { DisplayIndex = displayIndex };
            displayIndex++;
            MediaPanelItems.Add(vm);
        }
    }

    /// <summary>True when the media panel is in grid browsing mode.</summary>
    public bool MediaPanelIsGridMode => string.Equals(MediaPanelLayoutMode, "grid", StringComparison.Ordinal);

    /// <summary>True when the media panel is in list browsing mode.</summary>
    public bool MediaPanelIsListMode => string.Equals(MediaPanelLayoutMode, "list", StringComparison.Ordinal);

    partial void OnMediaPanelLayoutModeChanged(string value)
    {
        OnPropertyChanged(nameof(MediaPanelIsGridMode));
        OnPropertyChanged(nameof(MediaPanelIsListMode));
    }

    partial void OnMediaPanelSelectedPlaylistIdChanged(string? value)
    {
        RefreshMediaPanelItems();
        PrimeDrawerCache();
    }

    partial void OnMediaPanelSearchTextChanged(string value) => RefreshMediaPanelItems();

    /// <summary>Toggles the media panel open/closed, preserving the last panel height.</summary>
    public void ToggleMediaPanel()
    {
        if (!MediaPanelOpen)
        {
            if (MediaPlaylists.Count == 0)
                _ = LoadMediaPlaylistsAsync();
            else
                PrimeDrawerCache();
        }
        MediaPanelOpen = !MediaPanelOpen;
    }

    /// <summary>
    /// Creates a new playlist with the given name, reloads the panel, and selects the new playlist.
    /// </summary>
    public async Task CreateMediaPlaylistAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        var pl = await _mediaLibrary.CreatePlaylistAsync(name.Trim(), ct).ConfigureAwait(true);
        await LoadMediaPlaylistsAsync(ct).ConfigureAwait(true);
        MediaPanelSelectedPlaylistId = pl.Id;
    }

    /// <summary>Deletes the specified playlist after confirmation (called from view).</summary>
    public async Task DeleteMediaPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
            return;
        await _mediaLibrary.DeletePlaylistAsync(playlistId, ct).ConfigureAwait(true);
        if (string.Equals(MediaPanelSelectedPlaylistId, playlistId, StringComparison.OrdinalIgnoreCase))
            MediaPanelSelectedPlaylistId = null;
        await LoadMediaPlaylistsAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Adds a file path to the selected playlist, or to root All Media when no playlist id is supplied.</summary>
    public async Task AddMediaFileAsync(
        string? playlistId,
        string filePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        if (string.IsNullOrWhiteSpace(playlistId))
            await _mediaLibrary.AddRootItemAsync(filePath, ct).ConfigureAwait(true);
        else
            await _mediaLibrary.AddItemAsync(playlistId, filePath, ct).ConfigureAwait(true);

        await LoadMediaPlaylistsAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Renames a media item and reloads the media drawer contents.</summary>
    public async Task<bool> RenameMediaItemAsync(
        string? playlistId,
        string itemId,
        string newName,
        CancellationToken ct = default)
    {
        var renamed = await _mediaLibrary.RenameItemAsync(playlistId, itemId, newName, ct).ConfigureAwait(true);
        if (renamed)
            await LoadMediaPlaylistsAsync(ct).ConfigureAwait(true);
        return renamed;
    }

    /// <summary>Duplicates a media item in-place and reloads the media drawer contents.</summary>
    public async Task<MediaLibraryItem?> DuplicateMediaItemAsync(
        string? playlistId,
        string itemId,
        CancellationToken ct = default)
    {
        var duplicate = await _mediaLibrary.DuplicateItemAsync(playlistId, itemId, ct).ConfigureAwait(true);
        if (duplicate != null)
            await LoadMediaPlaylistsAsync(ct).ConfigureAwait(true);
        return duplicate;
    }

    /// <summary>Persists updated cue defaults for a specific media item.</summary>
    public async Task<bool> UpdateMediaItemCueDefaultsAsync(
        string? playlistId,
        string itemId,
        MediaCueDefaults defaults,
        CancellationToken ct = default) =>
        await _mediaLibrary.UpdateItemCueDefaultsAsync(playlistId, itemId, defaults, ct).ConfigureAwait(true);

    /// <summary>Finds a playlist that contains the given item id, or null if not found.</summary>
    public MediaPlaylistManifest? FindPlaylistForItem(string itemId) =>
        MediaPlaylists.FirstOrDefault(p =>
            p.Items.Any(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Creates a <see cref="SlideMediaCue"/> seeded from the given media library item's cue defaults.
    /// The <see cref="SlideMediaCue.MediaId"/> is set to the item's file path for direct resolution.
    /// </summary>
    public SlideMediaCue CreateCueFromMediaItem(MediaLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var mediaType = MediaInference.ResolveEffectiveMediaType(item.Type, item.Path);
        return new SlideMediaCue
        {
            Id = Guid.NewGuid().ToString("N"),
            MediaId = item.Path,
            MediaType = mediaType,
            DisplayName = MediaCueDisplayNameResolver.Normalize(item.Name),
            Target = item.CueDefaults.Target,
            Fit = item.CueDefaults.Fit,
            Loop = item.CueDefaults.Loop,
            Muted = item.CueDefaults.Muted,
            Autoplay = item.CueDefaults.Autoplay,
            Transition = CloneTransition(item.CueDefaults.Transition),
        };
    }

    /// <summary>
    /// Creates a direct-to-program cue from a media library item.
    /// The cue reuses the item's persisted playback defaults so direct playback matches slide-cued behavior,
    /// but uses the resolved absolute file path so the live output host reads the exact managed content file.
    /// </summary>
    private SlideMediaCue CreateProgramPlaybackCueFromMediaItem(MediaLibraryItem item, string resolvedPath)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedPath);

        var mediaType = MediaInference.ResolveEffectiveMediaType(item.Type, resolvedPath);

        return new SlideMediaCue
        {
            Id = Guid.NewGuid().ToString("N"),
            MediaId = resolvedPath,
            MediaType = mediaType,
            DisplayName = MediaCueDisplayNameResolver.Normalize(item.Name),
            Target = item.CueDefaults.Target,
            Fit = item.CueDefaults.Fit,
            Loop = item.CueDefaults.Loop,
            Muted = item.CueDefaults.Muted,
            Autoplay = item.CueDefaults.Autoplay,
            Transition = CloneTransition(item.CueDefaults.Transition),
        };
    }

    private static SlideTransition? CloneTransition(SlideTransition? transition)
    {
        if (transition == null)
            return null;

        return new SlideTransition
        {
            Type = transition.Type,
            Duration = transition.Duration,
            Easing = transition.Easing,
            Parameters = transition.Parameters == null
                ? null
                : new Dictionary<string, string>(transition.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Pre-warms the output back-buffer slot for the given media item so that when the operator
    /// triggers it via <see cref="PreviewMediaItem"/> the source is already open and playback
    /// begins without the file-open / MediaOpened wait.  Best-effort: failures are swallowed.
    /// </summary>
    public void PreWarmMediaItem(MediaLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            var prepared = _cuePreparation.PrepareMediaCue(item);
            if (prepared?.Media == null)
                return;

            _preWarmService.RequestPreWarm(prepared.Media, prepared.Target);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-warm failed for media item {Id}; will load on demand.", item.Id);
        }
    }

    /// <summary>
    /// Pre-warms all media slots carried by a prepared slide cue so that when the slide is taken
    /// live the media layers start immediately without waiting for sources to open.
    /// </summary>
    internal void PreWarmSlideCueMedia(PreparedSlideCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        try
        {
            if (cue.MediaLayers.MediaUnderlay is { } underlay)
                _preWarmService.RequestPreWarm(underlay, "mediaUnderlay");
            if (cue.MediaLayers.MediaOverlay is { } overlay)
                _preWarmService.RequestPreWarm(overlay, "mediaOverlay");
            if (cue.MediaLayers.Audio is { } audio)
                _preWarmService.RequestPreWarm(audio, "audio");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-warm failed for slide cue {SlideId}.", cue.SlideId);
        }
    }

    /// <summary>
    /// Sends the selected media library asset to the program output media layer (same engine path as slide media cues).
    /// </summary>
    /// <param name="item">The media item to play on the program layer.</param>
    public void PreviewMediaItem(MediaLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            var preparedCue = _cuePreparation.PrepareMediaCue(item);
            if (preparedCue == null)
            {
                StatusMessage = "Media file is missing or could not be resolved for playback.";
                _logger.LogWarning("Media item {ItemId} could not be resolved (path: {Path}).", item.Id, item.Path);
                return;
            }

            _engine.EnterPreparedMediaCue(preparedCue);
            _liveProduction.ReleaseClearedLayers([OutputLayerKind.Media, OutputLayerKind.Audio]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview media item {MediaId}.", item.Id);
            StatusMessage = $"Could not load media: {ex.Message}";
        }
    }

    private bool TryResolveMediaItemPlaybackPath(MediaLibraryItem item, out string resolvedPath)
    {
        ArgumentNullException.ThrowIfNull(item);

        resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
        return !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath);
    }

    /// <summary>
    /// Retained for callers that previously dismissed the media-panel preview overlay.
    /// Media-panel triggers now play through the engine, so there is no separate preview state to clear here.
    /// </summary>
    public void ClearMediaPanelPreview()
    {
    }

    /// <summary>
    /// Adds a media library item as a cue on the currently selected slide using the item's cue defaults.
    /// The newly created cue is independent of the asset defaults and can be edited separately.
    /// </summary>
    public async Task<bool> AddMediaItemToSelectedSlideAsync(
        MediaLibraryItem item,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var presentationPath = SelectedSlidePresentationPath ?? SelectedPresentationPath;
        var slideId = SelectedSlideId;
        if (string.IsNullOrWhiteSpace(presentationPath) || string.IsNullOrWhiteSpace(slideId))
            return false;

        return await AddMediaItemToSlideAsync(presentationPath, slideId, item, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Adds a media library item as a cue on the specified slide, seeded from the item's cue defaults.
    /// </summary>
    public async Task<bool> AddMediaItemToSlideAsync(
        string presentationPath,
        string slideId,
        MediaLibraryItem item,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(presentationPath) || string.IsNullOrWhiteSpace(slideId))
            return false;

        var cue = CreateCueFromMediaItem(item);

        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            slideId,
            (slide, _) =>
            {
                slide.MediaCues ??= new List<SlideMediaCue>();
                slide.MediaCues.RemoveAll(c => string.Equals(c.Target, cue.Target, StringComparison.OrdinalIgnoreCase));
                slide.MediaCues.Add(cue);
            },
            ct).ConfigureAwait(true);

        ApplyUpdatedMediaCueState(result.PresentationPath, slideId, result.Project);
        RefreshMediaCueVisuals(presentationPath, slideId);
        return true;
    }

    private void ApplyUpdatedMediaCueState(string presentationPath, string slideId, PresentationProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);
        ArgumentNullException.ThrowIfNull(project);

        var updatedSlide = project.Slides.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, slideId, StringComparison.OrdinalIgnoreCase));
        if (updatedSlide == null)
            return;

        if (OpenDocument != null && PathsMatchNullable(OpenDocument.SourcePath, presentationPath))
        {
            var existingSlide = Slides.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, slideId, StringComparison.OrdinalIgnoreCase));
            if (existingSlide != null)
            {
                var slideIndex = Slides.IndexOf(existingSlide);
                Slides[slideIndex] = updatedSlide;
                RebuildPlaybackSequence();
                RebuildSlideDeckItems();
                return;
            }
        }

        foreach (var section in BrowseStackSections.Where(section => PathsMatchNullable(section.PresentationPath, presentationPath)))
        {
            foreach (var row in section.SlideRows.Where(row =>
                         string.Equals(row.Slide.Id, slideId, StringComparison.OrdinalIgnoreCase)))
            {
                SyncMediaCueCollection(row.Slide, updatedSlide);
                row.NotifyMediaCueChanged();
            }

            PopulateGroupedSlideSections(section.GroupedSlideSections, section.SlideRows);
            ApplyDeckSettingsToSection(section);
        }
    }

    private static void SyncMediaCueCollection(PresentationSlide target, PresentationSlide source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        target.MediaCues.Clear();
        foreach (var cue in source.MediaCues)
        {
            target.MediaCues.Add(new SlideMediaCue
            {
                Id = cue.Id,
                MediaId = cue.MediaId,
                MediaType = cue.MediaType,
                Target = cue.Target,
                Fit = cue.Fit,
                Loop = cue.Loop,
                Muted = cue.Muted,
                Autoplay = cue.Autoplay,
            });
        }
    }

    private void RefreshMediaCueVisuals(string? presentationPath, string slideId)
    {
        var targetPresentationPath = string.IsNullOrWhiteSpace(presentationPath)
            ? GetCurrentOpenPresentationPath()
            : presentationPath;

        foreach (var deckItem in SlideDeckItems.Where(item =>
                     string.Equals(item.Slide.Id, slideId, StringComparison.OrdinalIgnoreCase)
                     && PathsMatchNullable(item.PresentationPath ?? OpenDocument?.SourcePath, targetPresentationPath)))
        {
            deckItem.NotifyMediaCueChanged();
        }

        foreach (var section in BrowseStackSections)
        {
            foreach (var row in section.SlideRows.Where(item =>
                         string.Equals(item.Slide.Id, slideId, StringComparison.OrdinalIgnoreCase)
                         && PathsMatchNullable(item.PresentationPath ?? OpenDocument?.SourcePath, targetPresentationPath)))
            {
                row.NotifyMediaCueChanged();
            }
        }

        NotifyPreviewState();
    }

}