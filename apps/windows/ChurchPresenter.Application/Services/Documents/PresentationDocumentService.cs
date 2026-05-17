using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Documents;

/// <inheritdoc />
public sealed class PresentationDocumentService(
    IContentDirectoryService contentDirectories,
    ICpresDocumentService cpres,
    IPresentationProjectService projectDocuments,
    ILogger<PresentationDocumentService> logger) : IPresentationDocumentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IContentDirectoryService _contentDirectories = contentDirectories ?? throw new ArgumentNullException(nameof(contentDirectories));
    private readonly ICpresDocumentService _cpres = cpres ?? throw new ArgumentNullException(nameof(cpres));
    private readonly IPresentationProjectService _projectDocuments = projectDocuments ?? throw new ArgumentNullException(nameof(projectDocuments));
    private readonly ILogger<PresentationDocumentService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public PresentationDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var resolvedPath = _contentDirectories.ResolvePresentationPath(path);
        var bundle = _cpres.Open(resolvedPath);

        var manifest = JsonSerializer.Deserialize<PresentationManifestDto>(bundle.ManifestJson, JsonOptions)
                       ?? new PresentationManifestDto();

        List<SlideDto> slides;
        try
        {
            slides = JsonSerializer.Deserialize<List<SlideDto>>(bundle.SlidesJson, JsonOptions) ?? new List<SlideDto>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize slides.json; using empty list.");
            slides = new List<SlideDto>();
        }

        // Ensure each slide has default layers element if missing
        for (var i = 0; i < slides.Count; i++)
        {
            if (slides[i].Layers.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                slides[i].Layers = JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
        }

        var project = _projectDocuments.Open(resolvedPath);

        return new PresentationDocument
        {
            Manifest = manifest,
            Slides = slides,
            Project = project,
            SourcePath = resolvedPath,
        };
    }
}