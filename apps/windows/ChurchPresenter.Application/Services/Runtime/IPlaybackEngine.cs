
namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Central playback engine — the single source of truth for slide selection, program output,
/// build animations, operator controls (blackout / clear / suppress), seek lifecycle,
/// and output destination flags.
/// <para>
/// Implements <see cref="ILiveSessionService"/> for backward compatibility so existing consumers
/// that subscribe to <see cref="ILiveSessionService.Changed"/> continue to work.
/// </para>
/// </summary>
public interface IPlaybackEngine : ILiveSessionService
{
    /// <summary>
    /// Raised whenever any engine state changes — selection, program output, seek, or output flags.
    /// Supersedes the coarser <see cref="ILiveSessionService.Changed"/> event for new consumers.
    /// </summary>
    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>The current immutable snapshot of all engine state.</summary>
    PlaybackState CurrentState { get; }

    // ── Session navigation ────────────────────────────────────────────────────

    /// <summary>
    /// Atomically switches the live session to <paramref name="presentation"/> and navigates
    /// directly to <paramref name="slideId"/> in a single state update, avoiding the brief
    /// blank-frame that would occur from separate <see cref="ILiveSessionService.GoLive"/> +
    /// <see cref="ILiveSessionService.GoToSlide"/> calls.
    /// <para>
    /// Unlike <see cref="ILiveSessionService.GoLive"/>, this method does <em>not</em> require
    /// the caller to pre-load the document outside the engine; the caller supplies the already-
    /// loaded <paramref name="presentation"/> directly.
    /// </para>
    /// </summary>
    /// <param name="presentation">The new active presentation (must be non-null).</param>
    /// <param name="path">Absolute path associated with the bundle, used for identity checks.</param>
    /// <param name="slideId">
    /// The slide to display.  If the ID is not found the first enabled slide is used.
    /// </param>
    void SwitchToPresentation(PresentationDocument presentation, string path, string slideId);

    // ── Program media layer ───────────────────────────────────────────────────

    /// <summary>
    /// Applies a media cue directly to the program media layer, replacing only the cue's target slot.
    /// Existing media on other targets remains active until another cue or a clear action changes it.
    /// </summary>
    /// <param name="cue">Cue describing target slot, media identity, and playback flags.</param>
    /// <param name="resolvedMediaPath">
    /// Optional absolute path to the media file when <see cref="SlideMediaCue.MediaId"/> is a library
    /// or content-relative reference; forwarded to <see cref="OutputLayerMedia.ResolvedSourcePath"/> for rendering.
    /// </param>
    void PlayMediaCue(SlideMediaCue cue, string? resolvedMediaPath = null);

    /// <summary>Enters a prepared slide cue onto the live slide layer.</summary>
    void EnterPreparedSlideCue(PreparedSlideCue cue);

    /// <summary>Enters a prepared media cue onto the live media layer.</summary>
    void EnterPreparedMediaCue(PreparedMediaCue cue);

    // ── Operator selection ────────────────────────────────────────────────────

    /// <summary>
    /// Updates the operator's cursor to point at a specific slide without sending it to the program output.
    /// </summary>
    void SelectSlide(string? presentationPath, string slideId, string? instanceKey,
        SelectionSource source = SelectionSource.Operator);

    /// <summary>Clears the operator's slide selection cursor.</summary>
    void ClearSelection();

    /// <summary>Sets whether the user has explicitly overridden the program slide selection.</summary>
    void SetUserOverrideSelection(bool value);

    // ── Seek lifecycle ────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a hold-to-seek navigation loop in the given <paramref name="direction"/>.
    /// The engine calls <paramref name="stepProvider"/> once immediately and then on each
    /// seek tick until <see cref="StopSeek"/> is called or the step provider signals no more moves.
    /// </summary>
    /// <param name="direction">+1 for forward, -1 for backward.</param>
    /// <param name="stepProvider">
    /// Async delegate that performs one navigation step and returns the result.
    /// Called on the UI thread (with <c>ConfigureAwait(true)</c>).
    /// </param>
    /// <returns><c>true</c> when a seek was started or was already running in the same direction.</returns>
    Task<bool> StartSeekAsync(int direction, Func<int, Task<SlideSeekStepResult>> stepProvider);

    /// <summary>Cancels any active seek loop.</summary>
    void StopSeek();

    /// <summary>
    /// Rebuilds the cached <see cref="CurrentState"/> so backend frame adaptation picks up updated
    /// <see cref="IShowTransitionDefaults"/> without mutating program output.
    /// </summary>
    void NotifyGlobalTransitionDefaultsChanged();
}

/// <summary>Event arguments for <see cref="IPlaybackEngine.StateChanged"/>.</summary>
public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    /// <summary>The new engine state snapshot.</summary>
    public PlaybackState State { get; init; } = new();
}