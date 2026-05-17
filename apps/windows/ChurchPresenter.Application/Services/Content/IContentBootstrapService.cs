
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Bootstraps and migrates the managed content root on startup.
/// Detects the current layout version, creates the TitleCase folder structure,
/// migrates legacy content when present, and records the outcome.
/// </summary>
public interface IContentBootstrapService
{
    /// <summary>Current migration state after the most recent <see cref="InitializeAsync"/> call.</summary>
    MigrationState CurrentMigrationState { get; }

    /// <summary>
    /// Ensures the content root layout is current, running migration automatically when needed.
    /// Safe to call multiple times — idempotent when the layout is already up to date.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Indicates the outcome of the last bootstrap/migration pass.
/// </summary>
public enum MigrationState
{
    /// <summary>Bootstrap has not run yet this session.</summary>
    NotStarted,
    /// <summary>Layout was already in the current TitleCase format; no migration was needed.</summary>
    NotNeeded,
    /// <summary>Migration is currently in progress.</summary>
    InProgress,
    /// <summary>Migration completed successfully.</summary>
    Completed,
    /// <summary>Migration failed or was interrupted; partial results may exist.</summary>
    Failed,
}