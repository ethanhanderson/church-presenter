using System.Text.Json;

using ChurchPresenter.Backend.Rendering;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Central playback engine implementing both <see cref="IPlaybackEngine"/> and <see cref="ILiveSessionService"/>.
/// This is the single source of truth for all playback state: program output, operator selection, and seek lifecycle.
/// <para>
/// <see cref="LiveSessionService"/> is retained as a standalone class for existing unit tests.
/// Production DI should register <see cref="PlaybackEngine"/> for <em>both</em>
/// <see cref="IPlaybackEngine"/> and <see cref="ILiveSessionService"/>.
/// </para>
/// </summary>
public sealed class PlaybackEngine : IPlaybackEngine
{
    private static readonly MediaLayersState DefaultMedia = new();

    private readonly ILogger<PlaybackEngine> _logger;
    private readonly IShowTransitionDefaults _transitionDefaults;

    // ── Program output mutable state (mirrors LiveSessionService) ─────────────
    private bool _audience;
    private bool _stage;
    private bool _isLive;
    private PresentationDocument? _presentation;
    private string? _presentationPath;
    private string? _currentSlideId;
    private string? _currentSlideInstanceKey;
    private int _currentSlideIndex = -1;
    private OutputLayerKind _presentationLayerKind = OutputLayerKind.Slide;
    private int _currentBuildIndex = -1;
    private bool _isBlackout;
    private bool _isClear;
    private readonly List<string> _visibleLayerIds = new();
    private MediaLayersState _mediaLayers = new();
    private SuppressState _suppress = new();
    private ClearingState _isClearing = new();
    private ClearedPresentationState? _clearedPresentation;
    private ClearedMediaState? _clearedMedia;

    // ── Operator selection and seek state (new in engine) ─────────────────────
    private SelectionCursor _operatorCursor = SelectionCursor.Empty;
    private bool _userOverrideSelection;
    private CancellationTokenSource? _seekCts;
    private int _seekDirection;
    private Task? _seekTask;
    private const int MinimumSeekRepeatMs = 50;

    // ── Cached snapshot ───────────────────────────────────────────────────────
    private PlaybackState _currentState;

    /// <summary>Initializes a new <see cref="PlaybackEngine"/> with a logger.</summary>
    public PlaybackEngine(ILogger<PlaybackEngine> logger, IShowTransitionDefaults transitionDefaults)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transitionDefaults = transitionDefaults ?? throw new ArgumentNullException(nameof(transitionDefaults));
        _currentState = BuildSnapshot();
    }

    /// <inheritdoc />
    public void NotifyGlobalTransitionDefaultsChanged() => RaiseAll();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public event EventHandler<LiveSessionEventArgs>? Changed;

    /// <inheritdoc />
    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    // ── IPlaybackEngine ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public PlaybackState CurrentState => _currentState;

    /// <inheritdoc />
    public void PlayMediaCue(SlideMediaCue cue, string? resolvedMediaPath = null)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (string.IsNullOrWhiteSpace(cue.MediaId))
            return;

        var layer = new OutputLayerMedia
        {
            MediaId = cue.MediaId,
            MediaType = cue.MediaType,
            DisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName),
            Fit = cue.Fit,
            Loop = cue.Loop ?? false,
            Muted = cue.Muted ?? false,
            Autoplay = cue.Autoplay ?? false,
            Transition = cue.Transition == null
                ? null
                : new SlideTransition
                {
                    Type = cue.Transition.Type,
                    Duration = cue.Transition.Duration,
                    Easing = cue.Transition.Easing,
                    Parameters = cue.Transition.Parameters == null
                        ? null
                        : new Dictionary<string, string>(cue.Transition.Parameters, StringComparer.OrdinalIgnoreCase),
                },
            ResolvedSourcePath = string.IsNullOrWhiteSpace(resolvedMediaPath) ? null : resolvedMediaPath.Trim(),
        };

        EnterPreparedMediaCue(new PreparedMediaCue
        {
            Target = SlideMediaLayerBuilder.MapCueTarget(cue.Target),
            Media = layer,
        });
    }

    /// <inheritdoc />
    public void EnterPreparedSlideCue(PreparedSlideCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (cue.Presentation == null || cue.SlideIndex < 0 || cue.SlideIndex >= cue.Presentation.Slides.Count)
            return;

        var slide = cue.Presentation.Slides[cue.SlideIndex];
        var slideId = string.IsNullOrWhiteSpace(cue.SlideId) ? slide.Id : cue.SlideId;
        if (string.IsNullOrWhiteSpace(slideId))
            return;

        var priorPresentation = _presentation;
        var priorPresentationPath = _presentationPath;
        var priorIndex = _currentSlideIndex;
        var priorSlideId = _currentSlideId;
        var priorSlideInstanceKey = _currentSlideInstanceKey;
        var priorBuild = _currentBuildIndex;
        var priorVisible = _visibleLayerIds.ToArray();
        var priorMedia = SlideMediaLayerBuilder.Clone(_mediaLayers);
        var priorBlackout = _isBlackout;
        var priorClear = _isClear;
        var priorSuppressPres = _suppress.Presentation;
        var priorSuppressMedia = _suppress.Media;
        var priorClearingPres = _isClearing.Presentation;
        var priorClearingMedia = _isClearing.Media;
        var hadClearedPresentation = _clearedPresentation != null;
        var hadClearedMedia = _clearedMedia != null;

        _isLive = true;
        _presentation = cue.Presentation;
        _presentationPath = string.IsNullOrWhiteSpace(cue.PresentationPath)
            ? cue.Presentation.SourcePath
            : cue.PresentationPath;
        _presentationLayerKind = cue.LayerKind is OutputLayerKind.Announcements
            ? OutputLayerKind.Announcements
            : OutputLayerKind.Slide;
        _currentSlideIndex = cue.SlideIndex;
        _currentSlideId = slideId;
        _currentSlideInstanceKey = string.IsNullOrWhiteSpace(cue.InstanceKey) ? slideId : cue.InstanceKey;
        _currentBuildIndex = -1;
        _isBlackout = false;
        _isClear = false;
        _suppress = new SuppressState
        {
            Presentation = false,
            Media = _suppress.Media,
        };
        _isClearing = new ClearingState
        {
            Presentation = false,
            Media = _isClearing.Media,
        };
        _clearedPresentation = null;

        UpdateVisibleLayerIds();
        ApplyPreparedSlideMediaLayers(cue.MediaLayers);

        var unchanged =
            ReferenceEquals(priorPresentation, _presentation)
            && string.Equals(priorPresentationPath, _presentationPath, StringComparison.OrdinalIgnoreCase)
            && priorIndex == _currentSlideIndex
            && string.Equals(priorSlideId, _currentSlideId, StringComparison.Ordinal)
            && string.Equals(priorSlideInstanceKey, _currentSlideInstanceKey, StringComparison.Ordinal)
            && priorBuild == -1
            && priorBlackout == _isBlackout
            && priorClear == _isClear
            && priorSuppressPres == _suppress.Presentation
            && priorSuppressMedia == _suppress.Media
            && priorClearingPres == _isClearing.Presentation
            && priorClearingMedia == _isClearing.Media
            && hadClearedPresentation == (_clearedPresentation != null)
            && hadClearedMedia == (_clearedMedia != null)
            && priorVisible.SequenceEqual(_visibleLayerIds, StringComparer.Ordinal)
            && SlideMediaLayerBuilder.MediaLayersStateEquals(priorMedia, _mediaLayers);

        if (unchanged)
            return;

        RaiseAll();
    }

    /// <inheritdoc />
    public void EnterPreparedMediaCue(PreparedMediaCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (string.IsNullOrWhiteSpace(cue.Target) || string.IsNullOrWhiteSpace(cue.Media.MediaId))
            return;

        var nextLayers = new MediaLayersState();
        switch (cue.Target)
        {
            case "mediaUnderlay":
                nextLayers.MediaUnderlay = CloneOutputLayerMedia(cue.Media);
                break;
            case "mediaOverlay":
                nextLayers.MediaOverlay = CloneOutputLayerMedia(cue.Media);
                break;
            case "audio":
                nextLayers.Audio = CloneOutputLayerMedia(cue.Media);
                break;
        }

        if (!HasMediaLayers(nextLayers))
            return;

        var merged = SlideMediaLayerBuilder.Overlay(_mediaLayers, nextLayers);
        if (TryMergeProgramMediaLayers(merged))
            RaiseAll();
    }

    /// <summary>
    /// Merges program media when the result differs or when media must be unsuppressed after clear/clearing.
    /// Does not raise events — callers batch <see cref="RaiseAll"/> (e.g. slide navigation).
    /// </summary>
    /// <returns>True when media layers or media suppress/clear bookkeeping changed.</returns>
    private bool TryMergeProgramMediaLayers(MediaLayersState merged)
    {
        if (_suppress.Media || _isClearing.Media || _clearedMedia != null)
        {
            _mediaLayers = merged;
            _suppress.Media = false;
            _isClearing.Media = false;
            _clearedMedia = null;
            return true;
        }

        if (SlideMediaLayerBuilder.MediaLayersStateEquals(merged, _mediaLayers))
            return false;

        _mediaLayers = merged;
        return true;
    }

    // ── ILiveSessionService properties ────────────────────────────────────────

    /// <inheritdoc />
    public bool IsAudienceEnabled => _audience;

    /// <inheritdoc />
    public bool IsStageEnabled => _stage;

    /// <inheritdoc />
    public bool IsLive => _isLive;

    /// <inheritdoc />
    public PresentationDocument? Presentation => _presentation;

    /// <inheritdoc />
    public string? PresentationPath => _presentationPath;

    /// <inheritdoc />
    public string? CurrentSlideId => _currentSlideId;

    /// <inheritdoc />
    public string? CurrentSlideInstanceKey => _currentSlideInstanceKey;

    /// <inheritdoc />
    public int CurrentSlideIndex => _currentSlideIndex;

    /// <inheritdoc />
    public int CurrentBuildIndex => _currentBuildIndex;

    /// <inheritdoc />
    public bool IsBlackout => _isBlackout;

    /// <inheritdoc />
    public bool IsClear => _isClear;

    /// <inheritdoc />
    public IReadOnlyList<string> VisibleLayerIds => _visibleLayerIds;

    /// <inheritdoc />
    public MediaLayersState MediaLayers => _mediaLayers;

    /// <inheritdoc />
    public SuppressState Suppress => _suppress;

    /// <inheritdoc />
    public ClearingState IsClearing => _isClearing;

    /// <inheritdoc />
    public bool CanUndoClearPresentation => _clearedPresentation != null;

    /// <inheritdoc />
    public bool CanUndoClearMedia => _clearedMedia != null;

    /// <inheritdoc />
    public bool HasMoreBuilds
    {
        get
        {
            var slide = CurrentSlideDto;
            var steps = GetAdvanceBuildSteps(slide);
            return _currentBuildIndex < steps.Count - 1;
        }
    }

    private SlideDto? CurrentSlideDto
    {
        get
        {
            if (_presentation == null || string.IsNullOrEmpty(_currentSlideId))
                return null;
            return _presentation.Slides.FirstOrDefault(s => s.Id == _currentSlideId);
        }
    }

    // ── Session ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void SetAudienceEnabled(bool enabled)
    {
        if (_audience == enabled)
            return;
        _audience = enabled;
        _logger.LogDebug("Audience output {State}.", enabled ? "enabled" : "disabled");
        // Routing-only: do not raise Changed/StateChanged so program output surfaces stay hot (no flash).
        _currentState = BuildSnapshot();
    }

    /// <inheritdoc />
    public void SetStageEnabled(bool enabled)
    {
        if (_stage == enabled)
            return;
        _stage = enabled;
        _logger.LogDebug("Stage output {State}.", enabled ? "enabled" : "disabled");
        // Stage observers still rely on the engine notification stream so the toggle remains observable.
        RaiseAll();
    }

    /// <inheritdoc />
    public void GoLive(PresentationDocument presentation, string? path)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _isLive = true;
        _presentation = presentation;
        _presentationPath = path;
        _currentSlideIndex = -1;
        _currentSlideId = null;
        _currentSlideInstanceKey = null;
        _currentBuildIndex = -1;
        _isBlackout = false;
        _isClear = false;
        _mediaLayers = CloneMedia(DefaultMedia);
        _suppress = new SuppressState();
        _isClearing = new ClearingState();
        _clearedPresentation = null;
        _clearedMedia = null;
        UpdateVisibleLayerIds();
        ApplySlideCuesToState(CurrentSlideDto);
        RaiseAll();
    }

    /// <inheritdoc />
    public void EndLive()
    {
        _isLive = false;
        _presentation = null;
        _presentationPath = null;
        _currentSlideId = null;
        _currentSlideInstanceKey = null;
        _currentSlideIndex = 0;
        _presentationLayerKind = OutputLayerKind.Slide;
        _currentBuildIndex = -1;
        _isBlackout = false;
        _isClear = false;
        _visibleLayerIds.Clear();
        _mediaLayers = CloneMedia(DefaultMedia);
        _suppress = new SuppressState();
        _isClearing = new ClearingState();
        _clearedPresentation = null;
        _clearedMedia = null;
        RaiseAll();
    }

    /// <inheritdoc />
    public void SwitchToPresentation(PresentationDocument presentation, string path, string slideId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        // Resolve the target slide index; fall back to the first enabled slide.
        var index = presentation.Slides.ToList().FindIndex(s =>
            string.Equals(s.Id, slideId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
            for (var i = 0; i < presentation.Slides.Count; i++)
            {
                var projectSlide = presentation.Project?.Slides.ElementAtOrDefault(i);
                if (projectSlide == null || !projectSlide.Disabled)
                {
                    index = i;
                    break;
                }
            }
        }

        if (presentation.Slides.Count == 0 || index < 0)
            return;

        EnterPreparedSlideCue(BuildPreparedSlideCue(presentation, path, index));
    }

    /// <inheritdoc />
    public void GoToSlide(string slideId)
    {
        if (_presentation == null)
            return;
        var index = _presentation.Slides.ToList().FindIndex(s =>
            string.Equals(s.Id, slideId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;
        EnterPreparedSlideCue(BuildPreparedSlideCue(_presentation, _presentationPath, index));
    }

    /// <inheritdoc />
    public void GoToSlideIndex(int index)
    {
        if (_presentation == null)
            return;
        if (index < 0 || index >= _presentation.Slides.Count)
            return;
        EnterPreparedSlideCue(BuildPreparedSlideCue(_presentation, _presentationPath, index));
    }

    /// <inheritdoc />
    public void NextSlideAction()
    {
        if (_presentation == null)
            return;
        if (HasMoreBuilds)
        {
            AdvanceBuild();
            return;
        }

        var next = FindNextEnabledSlideIndex(_currentSlideIndex, 1);
        if (next <= _currentSlideIndex)
            return;
        GoToSlideIndex(next);
    }

    /// <inheritdoc />
    public void PreviousSlideAction()
    {
        if (_currentBuildIndex >= 0)
        {
            ResetBuild();
            return;
        }

        if (_currentSlideIndex <= 0)
            return;
        var previous = FindNextEnabledSlideIndex(_currentSlideIndex, -1);
        if (previous >= _currentSlideIndex)
            return;
        GoToSlideIndex(previous);
    }

    /// <inheritdoc />
    public bool AdvanceBuild()
    {
        var slide = CurrentSlideDto;
        var steps = GetAdvanceBuildSteps(slide);
        if (_currentBuildIndex >= steps.Count - 1)
            return false;
        _currentBuildIndex++;
        UpdateVisibleLayerIds();
        RaiseAll();
        return true;
    }

    /// <inheritdoc />
    public void ResetBuild()
    {
        _currentBuildIndex = -1;
        UpdateVisibleLayerIds();
        RaiseAll();
    }

    /// <inheritdoc />
    public void SetBlackout(bool enabled)
    {
        _isBlackout = enabled;
        if (enabled)
            _isClear = false;
        RaiseAll();
    }

    /// <inheritdoc />
    public void SetClear(bool enabled)
    {
        _isClear = enabled;
        if (enabled)
            _isBlackout = false;
        RaiseAll();
    }

    /// <inheritdoc />
    public void ClearPresentation()
    {
        if (_isClearing.Presentation || _suppress.Presentation)
            return;

        // Only store undo state when a slide is currently showing; clear still
        // applies suppress even without a current slide so the clear button always works.
        if (!string.IsNullOrEmpty(_currentSlideId))
        {
            _clearedPresentation = new ClearedPresentationState
            {
                SlideId = _currentSlideId!,
                SlideIndex = _currentSlideIndex,
                BuildIndex = _currentBuildIndex,
            };
        }

        _isClearing = new ClearingState { Presentation = true, Media = _isClearing.Media };
        RaiseAll();
    }

    /// <inheritdoc />
    public void ClearMedia()
    {
        if (_isClearing.Media || _suppress.Media)
            return;
        if (!SlideMediaLayerBuilder.HasProgramMediaBeyondSlideCues(CurrentSlideDto, _mediaLayers))
            return;
        _clearedMedia = new ClearedMediaState { MediaLayers = CloneMedia(_mediaLayers) };
        _isClearing = new ClearingState { Presentation = _isClearing.Presentation, Media = true };
        // Defer Changed/StateChanged until FinishClearMedia.
    }

    /// <inheritdoc />
    public void FinishClearPresentation()
    {
        _isClearing = new ClearingState { Presentation = false, Media = _isClearing.Media };
        _suppress.Presentation = true;
        RaiseAll();
    }

    /// <inheritdoc />
    public void FinishClearMedia()
    {
        _isClearing = new ClearingState { Presentation = _isClearing.Presentation, Media = false };
        _mediaLayers = CloneMedia(SlideMediaLayerBuilder.Build(CurrentSlideDto));
        _suppress.Media = false;
        RaiseAll();
    }

    /// <inheritdoc />
    public string? UndoClearPresentation()
    {
        if (_clearedPresentation == null)
            return null;
        var slideId = _clearedPresentation.SlideId;
        if (_presentation != null)
        {
            var slide = _presentation.Slides.FirstOrDefault(s => s.Id == slideId);
            if (slide == null)
                return null;
            _suppress.Presentation = false;
            _isClearing.Presentation = false;
            _clearedPresentation = null;
        }
        else
        {
            _suppress.Presentation = false;
            _isClearing.Presentation = false;
            _clearedPresentation = null;
        }

        RaiseAll();
        return slideId;
    }

    /// <inheritdoc />
    public void UndoClearMedia()
    {
        if (_clearedMedia == null)
            return;
        _mediaLayers = CloneMedia(_clearedMedia.MediaLayers);
        _suppress.Media = false;
        _isClearing.Media = false;
        _clearedMedia = null;
        RaiseAll();
    }

    /// <inheritdoc />
    public void ResetSuppress()
    {
        _suppress = new SuppressState();
        _isClearing = new ClearingState();
        _clearedPresentation = null;
        _clearedMedia = null;
        RaiseAll();
    }

    // ── Operator selection ────────────────────────────────────────────────────

    /// <inheritdoc />
    public void SelectSlide(string? presentationPath, string slideId, string? instanceKey,
        SelectionSource source = SelectionSource.Operator)
    {
        var normalizedId = string.IsNullOrWhiteSpace(slideId) ? null : slideId;
        if (normalizedId == null)
        {
            ClearSelection();
            return;
        }

        _operatorCursor = new SelectionCursor
        {
            PresentationPath = string.IsNullOrWhiteSpace(presentationPath) ? null : presentationPath,
            SlideId = normalizedId,
            InstanceKey = string.IsNullOrWhiteSpace(instanceKey) ? normalizedId : instanceKey,
            Source = source,
        };
        RaiseAll();
    }

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (!_operatorCursor.HasSelection)
            return;
        _operatorCursor = SelectionCursor.Empty;
        RaiseAll();
    }

    /// <inheritdoc />
    public void SetUserOverrideSelection(bool value)
    {
        if (_userOverrideSelection == value)
            return;
        _userOverrideSelection = value;
        RaiseAll();
    }

    // ── Seek lifecycle ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<bool> StartSeekAsync(int direction, Func<int, Task<SlideSeekStepResult>> stepProvider)
    {
        if (_seekCts != null && !_seekCts.IsCancellationRequested && _seekDirection == direction)
            return true;

        StopSeek();

        var cts = new CancellationTokenSource();
        _seekCts = cts;
        _seekDirection = direction;

        var initialStep = await stepProvider(direction).ConfigureAwait(true);
        if (!initialStep.Moved)
        {
            StopSeek();
            return true;
        }

        _seekTask = RunSeekLoopAsync(direction, initialStep.Delay, cts.Token, stepProvider);
        return true;
    }

    /// <inheritdoc />
    public void StopSeek()
    {
        _seekCts?.Cancel();
        _seekCts = null;
        _seekDirection = 0;
        _seekTask = null;
    }

    private async Task RunSeekLoopAsync(
        int direction,
        TimeSpan initialDelay,
        CancellationToken ct,
        Func<int, Task<SlideSeekStepResult>> stepProvider)
    {
        var delay = NormalizeDelay(initialDelay);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var step = await stepProvider(direction).ConfigureAwait(true);
            if (!step.Moved)
            {
                StopSeek();
                break;
            }

            delay = NormalizeDelay(step.Delay);
        }
    }

    private static TimeSpan NormalizeDelay(TimeSpan delay) =>
        delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(MinimumSeekRepeatMs);

    // ── State snapshot ────────────────────────────────────────────────────────

    private void RaiseAll()
    {
        _currentState = BuildSnapshot();
        Changed?.Invoke(this, new LiveSessionEventArgs { AudienceEnabled = _audience });
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = _currentState });
    }

    private PlaybackState BuildSnapshot() => new()
    {
        IsLive = _isLive,
        Presentation = _presentation,
        PresentationPath = _presentationPath,
        OperatorCursor = _operatorCursor,
        UserOverrideSelection = _userOverrideSelection,
        CurrentSlideId = _currentSlideId,
        CurrentSlideInstanceKey = _currentSlideInstanceKey,
        CurrentSlideIndex = _currentSlideIndex,
        BuildIndex = _currentBuildIndex,
        HasMoreBuilds = ComputeHasMoreBuilds(),
        VisibleLayerIds = _visibleLayerIds.ToArray(),
        MediaLayers = SlideMediaLayerBuilder.Clone(_mediaLayers),
        PresentationLayerKind = _presentationLayerKind,
        IsBlackout = _isBlackout,
        IsClear = _isClear,
        Suppress = new SuppressState { Presentation = _suppress.Presentation, Media = _suppress.Media },
        IsClearing = new ClearingState { Presentation = _isClearing.Presentation, Media = _isClearing.Media },
        CanUndoClearPresentation = _clearedPresentation != null,
        CanUndoClearMedia = _clearedMedia != null,
        IsAudienceEnabled = _audience,
        IsStageEnabled = _stage,
        GlobalSlideFallback = _transitionDefaults.GlobalSlideFallback,
        GlobalMediaFallback = _transitionDefaults.GlobalMediaFallback,
    };

    private bool ComputeHasMoreBuilds()
    {
        var slide = CurrentSlideDto;
        var steps = GetAdvanceBuildSteps(slide);
        return _currentBuildIndex < steps.Count - 1;
    }

    // ── Helpers (ported from LiveSessionService) ──────────────────────────────

    private void UpdateVisibleLayerIds()
    {
        _visibleLayerIds.Clear();
        var slide = CurrentSlideDto;
        if (slide == null)
            return;

        var advanceSteps = GetAdvanceBuildSteps(slide);
        var advanceLayerIds = new HashSet<string>(advanceSteps.Select(s => s.LayerId), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var layerId in EnumerateLayerIds(slide))
        {
            if (advanceLayerIds.Contains(layerId) || !seen.Add(layerId))
                continue;
            _visibleLayerIds.Add(layerId);
        }

        for (var i = 0; i <= _currentBuildIndex && i < advanceSteps.Count; i++)
        {
            var id = advanceSteps[i].LayerId;
            if (!seen.Add(id))
                continue;
            _visibleLayerIds.Add(id);
        }
    }

    private static IEnumerable<string> EnumerateLayerIds(SlideDto slide)
    {
        if (slide.Layers.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var layer in slide.Layers.EnumerateArray())
        {
            if (layer.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var s = id.GetString();
                if (!string.IsNullOrEmpty(s))
                    yield return s!;
            }
        }
    }

    private static IReadOnlyList<BuildStepDto> GetAdvanceBuildSteps(SlideDto? slide)
    {
        if (slide?.Animations?.BuildIn == null)
            return Array.Empty<BuildStepDto>();
        return slide.Animations.BuildIn
            .Where(s => string.Equals(s.Trigger, "onAdvance", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private PreparedSlideCue BuildPreparedSlideCue(PresentationDocument presentation, string? presentationPath, int slideIndex, string? instanceKey = null)
    {
        var slide = presentation.Slides[slideIndex];
        return new PreparedSlideCue
        {
            Presentation = presentation,
            PresentationPath = string.IsNullOrWhiteSpace(presentationPath) ? presentation.SourcePath : presentationPath!,
            SlideId = slide.Id,
            InstanceKey = string.IsNullOrWhiteSpace(instanceKey) ? slide.Id : instanceKey,
            SlideIndex = slideIndex,
            SlideDocument = slide,
            Slide = presentation.Project?.Slides.ElementAtOrDefault(slideIndex),
            MediaLayers = SlideMediaLayerBuilder.Build(slide),
        };
    }

    private void ApplyPreparedSlideMediaLayers(MediaLayersState mediaLayers)
    {
        var nextLayers = CloneMedia(mediaLayers);
        if (!HasMediaLayers(nextLayers))
            return;

        var merged = SlideMediaLayerBuilder.Overlay(_mediaLayers, nextLayers);
        TryMergeProgramMediaLayers(merged);
    }

    private void ApplySlideCuesToState(SlideDto? slide)
    {
        var nextLayers = SlideMediaLayerBuilder.Build(slide);
        if (!HasMediaLayers(nextLayers))
            return;

        var merged = SlideMediaLayerBuilder.Overlay(_mediaLayers, nextLayers);
        TryMergeProgramMediaLayers(merged);
    }

    private static bool HasMediaLayers(MediaLayersState mediaLayers) =>
        mediaLayers.MediaUnderlay != null || mediaLayers.MediaOverlay != null || mediaLayers.Audio != null;

    private static OutputLayerMedia CloneOutputLayerMedia(OutputLayerMedia media) =>
        new()
        {
            MediaId = media.MediaId,
            MediaType = media.MediaType,
            DisplayName = media.DisplayName,
            Fit = media.Fit,
            Loop = media.Loop,
            Muted = media.Muted,
            Autoplay = media.Autoplay,
            Transition = media.Transition == null
                ? null
                : new SlideTransition
                {
                    Type = media.Transition.Type,
                    Duration = media.Transition.Duration,
                    Easing = media.Transition.Easing,
                    Parameters = media.Transition.Parameters == null
                        ? null
                        : new Dictionary<string, string>(media.Transition.Parameters, StringComparer.OrdinalIgnoreCase),
                },
            ResolvedSourcePath = media.ResolvedSourcePath,
        };

    private int FindNextEnabledSlideIndex(int currentIndex, int direction)
    {
        if (_presentation?.Project == null || _presentation.Project.Slides.Count == 0)
            return currentIndex;

        var slides = _presentation.Project.Slides;
        var index = currentIndex;
        while (true)
        {
            var candidateIndex = Math.Clamp(index + direction, 0, slides.Count - 1);
            if (candidateIndex == index)
                return currentIndex;
            if (!slides[candidateIndex].Disabled)
                return candidateIndex;
            index = candidateIndex;
        }
    }

    private static MediaLayersState CloneMedia(MediaLayersState src) =>
        SlideMediaLayerBuilder.Clone(src);
}