
namespace ChurchPresenter.Services.Support;

/// <summary>
/// Exports, previews, and imports portable support configuration packages.
/// </summary>
public interface ISupportPackageService
{
    /// <summary>Exports the current <c>Configurations/</c> support files into a package archive.</summary>
    Task ExportAsync(string destinationPath, SupportPackageExportOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Builds an import/sync preview without modifying local support files.</summary>
    Task<SupportPackagePreview> PreviewImportAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>Imports support files after destructive replace safeguards are satisfied.</summary>
    Task<SupportPackagePreview> ImportAsync(string packagePath, SupportPackageImportOptions? options = null, CancellationToken cancellationToken = default);
}