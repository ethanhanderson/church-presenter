using ChurchPresenter.Core.Cpres;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Facade over <see cref="ChurchPresenter.Core.Cpres.CpresBundleReader"/> and <see cref="ChurchPresenter.Core.Cpres.CpresBundleWriter"/> for dependency injection and testing.
/// </summary>
public interface ICpresDocumentService
{
    /// <summary>Opens a <c>.cpres</c> bundle from disk.</summary>
    /// <param name="path">Path to the file.</param>
    /// <returns>Parsed JSON and theme payloads.</returns>
    ParsedBundle Open(string path);

    /// <summary>Writes a bundle to disk atomically.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="state">Content to serialize.</param>
    void Save(string path, BundleSaveState state);
}

/// <summary>
/// Default implementation that delegates to the core bundle reader and writer.
/// </summary>
public sealed class CpresDocumentService(ILogger<CpresDocumentService> logger) : ICpresDocumentService
{
    private readonly ILogger<CpresDocumentService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public ParsedBundle Open(string path)
    {
        _logger.LogTrace("Opening cpres bundle {Path}.", path);
        return CpresBundleReader.Open(path);
    }

    /// <inheritdoc />
    public void Save(string path, BundleSaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _logger.LogTrace("Saving cpres bundle {Path}.", path);
        CpresBundleWriter.Save(path, state);
    }
}