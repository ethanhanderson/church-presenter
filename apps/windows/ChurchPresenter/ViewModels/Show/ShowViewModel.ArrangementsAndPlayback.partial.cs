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
    // ── Arrangement + auto-advance helpers ──────────────────────────────────

    private void NotifyArrangementState()
    {
        _arrangementUpdateDepth++;
        try
        {
            OnPropertyChanged(nameof(Arrangements));
            OnPropertyChanged(nameof(ActiveArrangement));
            OnPropertyChanged(nameof(ActiveArrangementDisplayName));
            OnPropertyChanged(nameof(ArrangementPickerSelectedItem));
            OnPropertyChanged(nameof(ActiveArrangementGroups));
            OnPropertyChanged(nameof(AutoAdvanceSeconds));
            OnPropertyChanged(nameof(IsAutoAdvanceEnabled));
            OnPropertyChanged(nameof(PresentationDurationLabel));
            OnPropertyChanged(nameof(HasPresentationDefaultSlideTransition));
            OnPropertyChanged(nameof(PlaybackSequence));
            RebuildActiveArrangementGroupChips();
            SyncActiveSectionData();
            foreach (var section in BrowseStackSections)
                RefreshBrowseStackSectionHeaderState(section);
        }
        finally
        {
            _arrangementUpdateDepth--;
        }
    }

    private void RebuildActiveArrangementGroupChips()
    {
        _activeArrangementGroupChips.Clear();
        var p = OpenProject;
        var fixedLayout = ActiveArrangement?.IsNatural == true;
        foreach (var g in ActiveArrangementGroups)
            _activeArrangementGroupChips.Add(SectionGroupChipDisplay.Create(g, p, fixedLayout));
    }

    /// <summary>Updates <see cref="ShowPresentationDeckSection.IsActive"/> from workspace selection.</summary>
    private void SyncActiveSectionData()
    {
        var activePath = SelectedPresentationPath;
        foreach (var section in BrowseStackSections)
        {
            var isActive = !string.IsNullOrEmpty(activePath)
                && string.Equals(section.PresentationPath, activePath, StringComparison.OrdinalIgnoreCase);
            section.IsActive = isActive;
        }
    }

    private void ApplyBrowseStackHeaderState(ShowPresentationDeckSection section)
    {
        if (string.IsNullOrWhiteSpace(section.PresentationPath))
            return;

        section.ArrangementSectionExpanded = GetOrCreateBrowseStackHeaderState(section.PresentationPath).ArrangementSectionExpanded;
    }

    private ShowPresentationHeaderState GetOrCreateBrowseStackHeaderState(string presentationPath)
    {
        if (!_browseStackHeaderStates.TryGetValue(presentationPath, out var state))
        {
            state = new ShowPresentationHeaderState();
            _browseStackHeaderStates[presentationPath] = state;
        }

        return state;
    }

    /// <summary>Persists the inline arrangement-strip visibility for one browse-stack presentation header.</summary>
    public void SetBrowseStackArrangementExpanded(string presentationPath, bool isExpanded)
    {
        if (string.IsNullOrWhiteSpace(presentationPath))
            return;

        GetOrCreateBrowseStackHeaderState(presentationPath).ArrangementSectionExpanded = isExpanded;
    }

    /// <summary>Gets the presentation project for a path without changing the current slide/presentation selection.</summary>
    public PresentationProject? GetProjectForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return OpenProject;

        try
        {
            if (OpenDocument != null && PathsMatch(OpenDocument.SourcePath, path))
                return OpenProject;

            // Session cache first to avoid redundant disk reads.
            var cached = _sessionCache.TryGet(path);
            if (cached?.Project != null)
                return cached.Project;

            return _projects.Open(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Loads arrangement + timing header fields for one playlist/library deck section from disk or the open document.</summary>
    public void RefreshBrowseStackSectionHeaderState(ShowPresentationDeckSection section)
    {
        var path = section.PresentationPath;
        if (string.IsNullOrEmpty(path))
            return;

        PresentationProject? project = null;
        try
        {
            if (OpenDocument != null && PathsMatch(OpenDocument.SourcePath, path))
                project = OpenProject;
            else
            {
                // Session cache avoids re-reading from disk for playlist presentations.
                var cached = _sessionCache.TryGet(path);
                project = cached?.Project ?? _projects.Open(path);
            }
        }
        catch
        {
            return;
        }

        if (project?.Arrangement == null)
        {
            section.Arrangements.Clear();
            section.ArrangementPickerSelectedItem = null;
            section.ActiveArrangementName = string.Empty;
            section.ArrangementGroupChips.Clear();
            section.AutoAdvanceSeconds = 0;
            section.PresentationDurationLabel = string.Empty;
            section.HasDefaultSlideTransition = false;
            return;
        }

        var autoSecs = project.Arrangement.AutoAdvanceSeconds;
        section.AutoAdvanceSeconds = autoSecs;
        section.PresentationDurationLabel = BuildDurationLabel(autoSecs, project);
        section.HasDefaultSlideTransition =
            TransitionPresentationHelper.HasPresentationTransitionConfigured(project);

        section.Arrangements.Clear();
        foreach (var a in project.Arrangement.Arrangements)
            section.Arrangements.Add(a);

        var activeArr = ResolveActiveNamedArrangement(project);
        section.ArrangementPickerSelectedItem = activeArr;
        section.ActiveArrangementName = activeArr?.Name ?? string.Empty;

        section.ArrangementGroupChips.Clear();
        var fixedLayout = activeArr?.IsNatural == true;
        foreach (var g in GetSectionGroupsInArrangementOrder(project, activeArr))
            section.ArrangementGroupChips.Add(SectionGroupChipDisplay.Create(g, project, fixedLayout));

        ApplyBrowseStackHeaderState(section);
    }

    private static NamedArrangement? ResolveActiveNamedArrangement(PresentationProject project)
    {
        var list = project.Arrangement?.Arrangements;
        if (list == null || list.Count == 0)
            return null;
        var id = project.Arrangement!.ActiveArrangementId;
        return list.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(a => a.IsNatural);
    }

    private static List<SectionGroup> GetSectionGroupsInArrangementOrder(PresentationProject project, NamedArrangement? arr)
    {
        var sections = project.Arrangement?.Sections;
        if (arr == null || sections == null)
            return new List<SectionGroup>();
        return arr.Groups
            .Select(r => sections.FirstOrDefault(g => string.Equals(g.Id, r.SectionGroupId, StringComparison.OrdinalIgnoreCase)))
            .Where(g => g != null)
            .Select(g => g!)
            .ToList();
    }

    /// <summary>Called when the user picks an arrangement in a browse-stack header; opens that file if needed and persists.</summary>
    public async Task SetBrowseStackArrangementAsync(ShowPresentationDeckSection section, NamedArrangement? selected)
    {
        if (_arrangementUpdateDepth > 0) return;
        if (section == null || selected == null)
            return;

        var path = section.PresentationPath;
        if (string.IsNullOrEmpty(path))
            return;

        await SetActiveArrangementForPathAsync(path, selected.Id).ConfigureAwait(true);
    }

    private void RebuildPlaybackSequence()
    {
        _playbackSequence = OpenProject != null
            ? PresentationModelUtilities.BuildPlaybackSequence(OpenProject)
            : PlaybackSequence.Empty;
    }

    /// <summary>
    /// Activates the named arrangement with the given ID, rebuilds the playback sequence, persists,
    /// and refreshes the deck UI.
    /// </summary>
    public async Task SetActiveArrangementAsync(string arrangementId)
    {
        if (OpenProject?.Arrangement == null)
            return;
        if (string.Equals(OpenProject.Arrangement.ActiveArrangementId, arrangementId, StringComparison.OrdinalIgnoreCase))
            return;

        OpenProject.Arrangement.ActiveArrangementId = arrangementId;
        RebuildPlaybackSequence();
        RebuildSlideDeckItems();
        NotifyArrangementState();
        RearmAutoAdvance();
        await PersistOpenProjectAsync().ConfigureAwait(true);
    }

    /// <summary>Activates an arrangement for a specific presentation path without changing browse-stack selection.</summary>
    public async Task SetActiveArrangementForPathAsync(string path, string arrangementId)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (PathsMatch(path, SelectedPresentationPath))
        {
            await SetActiveArrangementAsync(arrangementId).ConfigureAwait(true);
            return;
        }

        var project = GetProjectForPath(path);
        if (project?.Arrangement == null)
            return;
        if (string.Equals(project.Arrangement.ActiveArrangementId, arrangementId, StringComparison.OrdinalIgnoreCase))
            return;

        project.Arrangement.ActiveArrangementId = arrangementId;
        _projects.Save(project, path);

        var section = BrowseStackSections.FirstOrDefault(s => PathsMatch(s.PresentationPath, path));
        if (section != null)
            RefreshBrowseStackSectionHeaderState(section);
    }

    /// <summary>
    /// Persists a new or updated custom arrangement, sets it as the active arrangement, and
    /// rebuilds the deck.
    /// </summary>
    public async Task SaveArrangementAsync(NamedArrangement arrangement)
    {
        if (OpenProject?.Arrangement == null || arrangement == null)
            return;

        var existing = OpenProject.Arrangement.Arrangements
            .FirstOrDefault(a => string.Equals(a.Id, arrangement.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var idx = OpenProject.Arrangement.Arrangements.IndexOf(existing);
            OpenProject.Arrangement.Arrangements[idx] = arrangement;
        }
        else
        {
            OpenProject.Arrangement.Arrangements.Add(arrangement);
        }

        await SetActiveArrangementAsync(arrangement.Id).ConfigureAwait(true);
    }

    /// <summary>Saves a custom arrangement for a specific presentation path without changing slide selection.</summary>
    public async Task SaveArrangementForPathAsync(string path, NamedArrangement arrangement)
    {
        if (string.IsNullOrWhiteSpace(path) || arrangement == null)
            return;

        if (PathsMatch(path, SelectedPresentationPath))
        {
            await SaveArrangementAsync(arrangement).ConfigureAwait(true);
            return;
        }

        var project = GetProjectForPath(path);
        if (project?.Arrangement == null)
            return;

        var existing = project.Arrangement.Arrangements
            .FirstOrDefault(a => string.Equals(a.Id, arrangement.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var idx = project.Arrangement.Arrangements.IndexOf(existing);
            project.Arrangement.Arrangements[idx] = arrangement;
        }
        else
        {
            project.Arrangement.Arrangements.Add(arrangement);
        }

        project.Arrangement.ActiveArrangementId = arrangement.Id;
        _projects.Save(project, path);

        var section = BrowseStackSections.FirstOrDefault(s => PathsMatch(s.PresentationPath, path));
        if (section != null)
            RefreshBrowseStackSectionHeaderState(section);
    }

    /// <summary>Deletes a custom arrangement (natural arrangement cannot be deleted).</summary>
    public async Task DeleteArrangementAsync(string arrangementId)
    {
        if (OpenProject?.Arrangement == null)
            return;
        var arr = OpenProject.Arrangement.Arrangements
            .FirstOrDefault(a => string.Equals(a.Id, arrangementId, StringComparison.OrdinalIgnoreCase));
        if (arr == null || arr.IsNatural)
            return;

        OpenProject.Arrangement.Arrangements.Remove(arr);

        // If the deleted arrangement was active, fall back to natural.
        if (string.Equals(OpenProject.Arrangement.ActiveArrangementId, arrangementId, StringComparison.OrdinalIgnoreCase))
        {
            var natural = OpenProject.Arrangement.Arrangements.FirstOrDefault(a => a.IsNatural);
            OpenProject.Arrangement.ActiveArrangementId = natural?.Id;
        }

        RebuildPlaybackSequence();
        RebuildSlideDeckItems();
        NotifyArrangementState();
        RearmAutoAdvance();
        await PersistOpenProjectAsync().ConfigureAwait(true);
    }

    /// <summary>Deletes a custom arrangement for a presentation path without changing browse-stack slide selection.</summary>
    public async Task DeleteArrangementForPathAsync(string path, string arrangementId)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (PathsMatch(path, SelectedPresentationPath))
        {
            await DeleteArrangementAsync(arrangementId).ConfigureAwait(true);
            return;
        }

        var project = GetProjectForPath(path);
        if (project?.Arrangement == null)
            return;

        var arr = project.Arrangement.Arrangements
            .FirstOrDefault(a => string.Equals(a.Id, arrangementId, StringComparison.OrdinalIgnoreCase));
        if (arr == null || arr.IsNatural)
            return;

        project.Arrangement.Arrangements.Remove(arr);

        if (string.Equals(project.Arrangement.ActiveArrangementId, arrangementId, StringComparison.OrdinalIgnoreCase))
        {
            var natural = project.Arrangement.Arrangements.FirstOrDefault(a => a.IsNatural);
            project.Arrangement.ActiveArrangementId = natural?.Id;
        }

        _projects.Save(project, path);

        var section = BrowseStackSections.FirstOrDefault(s => PathsMatch(s.PresentationPath, path));
        if (section != null)
            RefreshBrowseStackSectionHeaderState(section);
    }

    /// <summary>Sets the presentation-wide auto-advance interval in seconds (0 = off).</summary>
    public async Task SetAutoAdvanceAsync(int seconds)
    {
        if (OpenProject?.Arrangement == null)
            return;

        OpenProject.Arrangement.AutoAdvanceSeconds = seconds;
        NotifyArrangementState();
        RearmAutoAdvance();
        await PersistOpenProjectAsync().ConfigureAwait(true);
    }

    /// <summary>Sets the presentation-wide default transition and persists.</summary>
    public async Task SetDefaultTransitionAsync(SlideTransition? transition)
    {
        if (OpenProject?.Arrangement == null)
            return;

        OpenProject.Arrangement.DefaultTransition = transition;

        NotifyArrangementState();
        await PersistOpenProjectAsync().ConfigureAwait(true);
    }

    /// <summary>Gets the transition to show in the default-transition flyout for a path (open doc or disk).</summary>
    public SlideTransition? GetDefaultTransitionForPath(string? path) =>
        TransitionPresentationHelper.GetDefaultTransitionForDisplay(GetProjectForPath(path));

    /// <summary>Sets the saved default transition for a specific presentation path without changing slide selection.</summary>
    public async Task SetDefaultTransitionForPathAsync(string path, SlideTransition? transition)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (PathsMatch(path, SelectedPresentationPath))
        {
            await SetDefaultTransitionAsync(transition).ConfigureAwait(true);
            return;
        }

        var project = GetProjectForPath(path);
        if (project?.Arrangement == null)
            return;

        project.Arrangement.DefaultTransition = transition;

        _projects.Save(project, path);

        var section = BrowseStackSections.FirstOrDefault(s => PathsMatch(s.PresentationPath, path));
        if (section != null)
            RefreshBrowseStackSectionHeaderState(section);
    }

    // ── Auto-advance timer ───────────────────────────────────────────────────

    private void RearmAutoAdvance()
    {
        StopAutoAdvance();
        if (OpenProject?.Arrangement == null)
            return;
        var secs = OpenProject.Arrangement.AutoAdvanceSeconds;
        if (secs <= 0)
            return;

        _autoAdvanceCts = new CancellationTokenSource();
        _autoAdvanceTimer = new System.Timers.Timer(secs * 1000.0);
        _autoAdvanceTimer.AutoReset = true;
        _autoAdvanceTimer.Elapsed += OnAutoAdvanceElapsed;
        _autoAdvanceTimer.Start();
    }

    private void StopAutoAdvance()
    {
        _autoAdvanceCts?.Cancel();
        _autoAdvanceCts?.Dispose();
        _autoAdvanceCts = null;

        if (_autoAdvanceTimer != null)
        {
            _autoAdvanceTimer.Stop();
            _autoAdvanceTimer.Dispose();
            _autoAdvanceTimer = null;
        }
    }

    private void OnAutoAdvanceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Marshal to the UI thread.
        _ = App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
        {
            if (OpenDocument == null || _autoAdvanceCts?.IsCancellationRequested == true)
                return;

            // Use the playback sequence if available; otherwise flat slide list.
            if (_playbackSequence.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(_engine.CurrentSlideId))
                    return;

                var current = _playbackSequence.FindCurrentProgramInstance(_engine.CurrentSlideInstanceKey, _engine.CurrentSlideId);
                var currentIdx = current != null ? _playbackSequence.IndexOfInstanceKey(current.InstanceKey) : -1;
                // Find next non-disabled instance.
                var next = -1;
                for (var i = currentIdx + 1; i < _playbackSequence.Count; i++)
                {
                    if (!_playbackSequence.Instances[i].Slide.Disabled)
                    {
                        next = i;
                        break;
                    }
                }
                if (next < 0)
                {
                    StopAutoAdvance();
                    return;
                }

                var nextInstance = _playbackSequence.Instances[next];
                await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), nextInstance.SlideId, nextInstance.InstanceKey).ConfigureAwait(true);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_engine.CurrentSlideId))
                    return;

                var nextSlide = FindNextEnabledAutoAdvanceSlide();
                if (nextSlide == null)
                {
                    StopAutoAdvance();
                    return;
                }

                await TakeSlideLiveAsync(GetCurrentOpenPresentationPath(), nextSlide.Id, nextSlide.Id).ConfigureAwait(true);
            }
        });
    }

    private PresentationSlide? FindNextEnabledAutoAdvanceSlide()
    {
        if (OpenProject == null || string.IsNullOrWhiteSpace(_engine.CurrentSlideId))
            return null;

        var currentIndex = OpenProject.Slides.ToList().FindIndex(slide =>
            string.Equals(slide.Id, _engine.CurrentSlideId, StringComparison.OrdinalIgnoreCase));
        for (var i = currentIndex + 1; i < OpenProject.Slides.Count; i++)
        {
            if (!OpenProject.Slides[i].Disabled)
                return OpenProject.Slides[i];
        }

        return null;
    }

    // ── Transition favorites / recents ──────────────────────────────────────

    public HashSet<string> GetFavoriteTransitions() =>
        new(_settings.Settings.Show.FavoriteTransitions, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetRecentTransitions() =>
        _settings.Settings.Show.RecentTransitions;

    public async Task SetFavoriteTransitionAsync(string transitionKey, bool isFavorite)
    {
        _settings.Update(s =>
        {
            if (isFavorite)
            {
                if (!s.Show.FavoriteTransitions.Contains(transitionKey))
                    s.Show.FavoriteTransitions.Add(transitionKey);
            }
            else
            {
                s.Show.FavoriteTransitions.Remove(transitionKey);
            }
        });
        await _settings.SaveAsync().ConfigureAwait(true);
    }

    public async Task RecordRecentTransitionAsync(string transitionKey)
    {
        _settings.Update(s =>
        {
            s.Show.RecentTransitions.Remove(transitionKey);
            s.Show.RecentTransitions.Insert(0, transitionKey);
            if (s.Show.RecentTransitions.Count > 8)
                s.Show.RecentTransitions = s.Show.RecentTransitions.Take(8).ToList();
        });
        await _settings.SaveAsync().ConfigureAwait(true);
    }

    // ── Persist project helper ───────────────────────────────────────────────

    private void PersistOpenProject()
    {
        if (OpenDocument?.Project == null || string.IsNullOrEmpty(OpenDocument.SourcePath))
            return;
        try
        {
            _projects.Save(OpenDocument.Project, OpenDocument.SourcePath);
            // Keep the cache consistent so subsequent navigation reads the updated data.
            _sessionCache.UpdateEntry(OpenDocument.SourcePath, OpenDocument);
        }
        catch { /* non-critical */ }
    }

    private Task PersistOpenProjectAsync()
    {
        PersistOpenProject();
        return Task.CompletedTask;
    }

    // ── Deck rebuild override for arranged sequence ──────────────────────────

    private void RebuildSlideDeckItemsFromSequence()
    {
        var project = OpenProject;
        var path = GetCurrentOpenPresentationPath();
        var i = 1;
        var newItems = _playbackSequence.Instances.Select(inst =>
            new ShowSlideDeckItem(inst.Slide, i++, path, project, inst.InstanceKey));
        SlideDeckItems.ReplaceAll(newItems);
        ApplyDeckSettingsToItems(SlideDeckItems);
        SyncBrowseStackSlideRowsFromSlideDeck();
        RebuildGroupedSections();
        RefreshAllSlideDeckState();
    }

    // ── Media playback coordinator ───────────────────────────────────────────

    /// <summary>Shared observable state for the active media cue playback (used by the output-panel transport row).</summary>
    public IMediaPlaybackCoordinator PlaybackCoordinator => _playbackCoordinator;

    [RelayCommand]
    private void MediaTogglePlayPause() => _playbackCoordinator.TogglePlayPause();

    [RelayCommand]
    private void MediaRestart() => _playbackCoordinator.Restart();

    [RelayCommand]
    private void MediaSeekForward() => _playbackCoordinator.SeekForward(GetMediaSeekSeconds());

    [RelayCommand]
    private void MediaSeekBackward() => _playbackCoordinator.SeekBackward(GetMediaSeekSeconds());

    private int GetMediaSeekSeconds() => MediaSeekSeconds <= 0 ? 5 : Math.Clamp(MediaSeekSeconds, 1, 60);

}