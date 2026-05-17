namespace ChurchPresenter.Services.Output;

/// <summary>
/// Coordinates the audience-output and stage-output window lifecycles.
/// </summary>
public interface IOutputWindowService
{
    /// <summary>
    /// Applies the configured logical audience screen and its mapped local-display endpoints.
    /// </summary>
    void OpenAudience();

    // ── Audience output ──────────────────────────────────────────────────────

    /// <summary>
    /// Applies the requested audience-output targets.
    /// </summary>
    /// <param name="monitorIndices">Zero-based monitor indices (left-to-right, top-to-bottom order).</param>
    void OpenForMonitors(IReadOnlyList<int> monitorIndices);

    /// <summary>Hides all active audience-output windows while keeping them warm for reuse.</summary>
    void CloseAll();

    // ── Stage output ─────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the configured logical stage screen and its mapped local-display endpoints.
    /// </summary>
    void OpenStage();

    /// <summary>
    /// Applies the requested stage-output targets.
    /// </summary>
    /// <param name="monitorIndices">Zero-based monitor indices.</param>
    void OpenStageForMonitors(IReadOnlyList<int> monitorIndices);

    /// <summary>Hides all active stage-output windows while keeping them warm for reuse.</summary>
    void CloseStage();
}