
namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// In-process live presentation and audience output state for the Windows app.
/// </summary>
public interface ILiveSessionService
{
    /// <summary>Raised when live/output state changes (not for audience routing-only updates on <see cref="PlaybackEngine"/>).</summary>
    event EventHandler<LiveSessionEventArgs>? Changed;

    bool IsAudienceEnabled { get; }

    /// <summary>
    /// Audience output window routing preference. For <see cref="PlaybackEngine"/>, updates <see cref="IPlaybackEngine.CurrentState"/>
    /// without raising <see cref="Changed"/> so program surfaces stay stable when toggling the audience window.
    /// </summary>
    void SetAudienceEnabled(bool enabled);

    bool IsStageEnabled { get; }
    /// <summary>
    /// Stage output enablement. Unlike <see cref="SetAudienceEnabled"/>, stage toggles remain observable so
    /// stage-specific surfaces and compatibility listeners can react immediately.
    /// </summary>
    void SetStageEnabled(bool enabled);

    bool IsLive { get; }
    PresentationDocument? Presentation { get; }
    string? PresentationPath { get; }
    string? CurrentSlideId { get; }
    string? CurrentSlideInstanceKey { get; }
    int CurrentSlideIndex { get; }
    int CurrentBuildIndex { get; }
    bool IsBlackout { get; }
    bool IsClear { get; }
    IReadOnlyList<string> VisibleLayerIds { get; }
    MediaLayersState MediaLayers { get; }
    SuppressState Suppress { get; }
    ClearingState IsClearing { get; }
    bool CanUndoClearPresentation { get; }
    bool CanUndoClearMedia { get; }
    bool HasMoreBuilds { get; }

    void GoLive(PresentationDocument presentation, string? path);
    void EndLive();
    void GoToSlide(string slideId);
    void GoToSlideIndex(int index);
    void NextSlideAction();
    void PreviousSlideAction();
    bool AdvanceBuild();
    void ResetBuild();
    void SetBlackout(bool enabled);
    void SetClear(bool enabled);
    void ClearPresentation();
    void ClearMedia();
    void FinishClearPresentation();
    void FinishClearMedia();
    string? UndoClearPresentation();
    void UndoClearMedia();
    void ResetSuppress();
}

/// <summary>Event data for <see cref="ILiveSessionService.Changed"/>.</summary>
public sealed class LiveSessionEventArgs : EventArgs
{
    /// <summary>Current audience-enabled flag (for compatibility with listeners that only care about output windows).</summary>
    public bool AudienceEnabled { get; init; }
}