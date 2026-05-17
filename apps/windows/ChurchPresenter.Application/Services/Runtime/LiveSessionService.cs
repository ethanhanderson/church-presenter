using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Runtime;

/// <summary>Default in-memory implementation of <see cref="ILiveSessionService"/>.</summary>
public sealed class LiveSessionService(ILogger<LiveSessionService> logger) : ILiveSessionService
{
    private static readonly MediaLayersState DefaultMedia = new();
    private static readonly SuppressState DefaultSuppress = new();

    private readonly ILogger<LiveSessionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private bool _audience;
    private bool _stage;
    private bool _isLive;
    private PresentationDocument? _presentation;
    private string? _presentationPath;
    private string? _currentSlideId;
    private string? _currentSlideInstanceKey;
    private int _currentSlideIndex;
    private int _currentBuildIndex = -1;
    private bool _isBlackout;
    private bool _isClear;
    private readonly List<string> _visibleLayerIds = new();
    private MediaLayersState _mediaLayers = new();
    private SuppressState _suppress = new();
    private ClearingState _isClearing = new();
    private ClearedPresentationState? _clearedPresentation;
    private ClearedMediaState? _clearedMedia;

    /// <inheritdoc />
    public event EventHandler<LiveSessionEventArgs>? Changed;

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
            var slide = CurrentSlide;
            var steps = GetAdvanceBuildSteps(slide);
            return _currentBuildIndex < steps.Count - 1;
        }
    }

    private SlideDto? CurrentSlide
    {
        get
        {
            if (_presentation == null || string.IsNullOrEmpty(_currentSlideId))
                return null;
            return _presentation.Slides.FirstOrDefault(s => s.Id == _currentSlideId);
        }
    }

    /// <inheritdoc />
    public void SetAudienceEnabled(bool enabled)
    {
        if (_audience == enabled)
            return;
        _audience = enabled;
        _logger.LogDebug("Audience output {State}.", enabled ? "enabled" : "disabled");
        // Routing-only: parity with <see cref="PlaybackEngine.SetAudienceEnabled"/>.
    }

    /// <inheritdoc />
    public void SetStageEnabled(bool enabled)
    {
        if (_stage == enabled)
            return;
        _stage = enabled;
        _logger.LogDebug("Stage output {State}.", enabled ? "enabled" : "disabled");
        RaiseChanged();
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
        var slide = CurrentSlide;
        ApplySlideCuesToState(slide);
        RaiseChanged();
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
        _currentBuildIndex = -1;
        _isBlackout = false;
        _isClear = false;
        _visibleLayerIds.Clear();
        _mediaLayers = CloneMedia(DefaultMedia);
        _suppress = new SuppressState();
        _isClearing = new ClearingState();
        _clearedPresentation = null;
        _clearedMedia = null;
        RaiseChanged();
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
        GoToSlideIndexCore(index);
    }

    /// <inheritdoc />
    public void GoToSlideIndex(int index)
    {
        if (_presentation == null)
            return;
        if (index < 0 || index >= _presentation.Slides.Count)
            return;
        GoToSlideIndexCore(index);
    }

    private void GoToSlideIndexCore(int index)
    {
        if (_presentation == null)
            return;
        var slide = _presentation.Slides[index];
        _currentSlideIndex = index;
        _currentSlideId = slide.Id;
        _currentSlideInstanceKey = slide.Id;
        _currentBuildIndex = -1;
        _isBlackout = false;
        _isClear = false;
        _suppress = new SuppressState();
        _isClearing = new ClearingState();
        _clearedPresentation = null;
        UpdateVisibleLayerIds();
        ApplySlideCuesToState(slide);
        RaiseChanged();
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
        var slide = CurrentSlide;
        var steps = GetAdvanceBuildSteps(slide);
        if (_currentBuildIndex >= steps.Count - 1)
            return false;
        _currentBuildIndex++;
        UpdateVisibleLayerIds();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public void ResetBuild()
    {
        _currentBuildIndex = -1;
        UpdateVisibleLayerIds();
        RaiseChanged();
    }

    /// <inheritdoc />
    public void SetBlackout(bool enabled)
    {
        _isBlackout = enabled;
        if (enabled)
            _isClear = false;
        RaiseChanged();
    }

    /// <inheritdoc />
    public void SetClear(bool enabled)
    {
        _isClear = enabled;
        if (enabled)
            _isBlackout = false;
        RaiseChanged();
    }

    /// <inheritdoc />
    public void ClearPresentation()
    {
        if (_isClearing.Presentation || _suppress.Presentation)
            return;

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
        RaiseChanged();
    }

    /// <inheritdoc />
    public void ClearMedia()
    {
        if (_isClearing.Media || _suppress.Media)
            return;
        if (!SlideMediaLayerBuilder.HasProgramMediaBeyondSlideCues(CurrentSlide, _mediaLayers))
            return;

        _clearedMedia = new ClearedMediaState { MediaLayers = CloneMedia(_mediaLayers) };
        _isClearing = new ClearingState { Presentation = _isClearing.Presentation, Media = true };
    }

    /// <inheritdoc />
    public void FinishClearPresentation()
    {
        _isClearing = new ClearingState { Presentation = false, Media = _isClearing.Media };
        _suppress.Presentation = true;
        RaiseChanged();
    }

    /// <inheritdoc />
    public void FinishClearMedia()
    {
        _isClearing = new ClearingState { Presentation = _isClearing.Presentation, Media = false };
        _mediaLayers = CloneMedia(SlideMediaLayerBuilder.Build(CurrentSlide));
        _suppress.Media = false;
        RaiseChanged();
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

        RaiseChanged();
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
        RaiseChanged();
    }

    /// <inheritdoc />
    public void ResetSuppress()
    {
        _suppress = new SuppressState();
        _isClearing = new ClearingState();
        _clearedPresentation = null;
        _clearedMedia = null;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, new LiveSessionEventArgs { AudienceEnabled = _audience });
    }

    private void UpdateVisibleLayerIds()
    {
        _visibleLayerIds.Clear();
        var slide = CurrentSlide;
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
        return slide.Animations.BuildIn.Where(s => string.Equals(s.Trigger, "onAdvance", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ApplySlideCuesToState(SlideDto? slide)
    {
        _mediaLayers = SlideMediaLayerBuilder.Build(slide);
        if (_mediaLayers.MediaUnderlay != null || _mediaLayers.MediaOverlay != null || _mediaLayers.Audio != null)
            _suppress.Media = false;
    }

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

    private static MediaLayersState CloneMedia(MediaLayersState src)
    {
        return SlideMediaLayerBuilder.Clone(src);
    }
}