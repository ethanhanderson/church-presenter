
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Audits the managed content root for structural, consistency, and integrity issues.
/// Results are written to <c>Audits/ContentAudit.json</c> (portable) and logged to the
/// local machine maintenance log.
/// </summary>
public interface IContentAuditService
{
    /// <summary>Gets the most recent audit result; <c>null</c> if no audit has run this session.</summary>
    ContentAuditResult? LastAuditResult { get; }

    /// <summary>
    /// Runs a full content audit and persists the result.
    /// Returns the audit result including all detected issues and counts.
    /// </summary>
    Task<ContentAuditResult> RunAuditAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the last persisted audit result from <c>Audits/ContentAudit.json</c>.
    /// Returns <c>null</c> when no audit file exists.
    /// </summary>
    Task<ContentAuditResult?> LoadLastAuditResultAsync(CancellationToken cancellationToken = default);
}