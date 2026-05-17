namespace ChurchPresenter.Services.Content;

/// <summary>
/// Ensures the documents content layout and migrates legacy <c>catalog.json</c>
/// into the documents-backed aggregate catalog files when needed.
/// </summary>
public interface IAppDataInitializer
{
    /// <summary>Runs one-time folder setup and first-run file seeding.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}