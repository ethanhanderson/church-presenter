
namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Loads a typed <see cref="PresentationDocument"/> from a <c>.cpres</c> bundle.
/// </summary>
public interface IPresentationDocumentService
{
    /// <summary>Opens and parses manifest + slides JSON from an absolute or content-root-relative path.</summary>
    PresentationDocument Open(string path);
}