using System.Text.Json;
using System.Text.Json.Serialization;

using ChurchPresenter.Core.Cpres;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Opens and saves full typed presentation projects from <c>.cpres</c> bundles.
/// </summary>
public interface IPresentationProjectService
{
    /// <summary>
    /// Opens a typed project from an absolute or content-root-relative bundle path.
    /// </summary>
    PresentationProject Open(string path);

    /// <summary>
    /// Saves a typed project back to disk.
    /// </summary>
    void Save(PresentationProject project, string path);
}

/// <inheritdoc />
public sealed class PresentationProjectService(
    IContentDirectoryService contentDirectories,
    ICpresDocumentService cpres,
    ILogger<PresentationProjectService> logger) : IPresentationProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = PresentationJsonSerialization.CreateOptions();

    private readonly IContentDirectoryService _contentDirectories = contentDirectories ?? throw new ArgumentNullException(nameof(contentDirectories));
    private readonly ICpresDocumentService _cpres = cpres ?? throw new ArgumentNullException(nameof(cpres));
    private readonly ILogger<PresentationProjectService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public PresentationProject Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var resolvedPath = _contentDirectories.ResolvePresentationPath(path);
        var bundle = _cpres.Open(resolvedPath);

        var manifest = DeserializeOrDefault<PresentationManifest>(bundle.ManifestJson);
        var slides = DeserializeOrDefault<List<PresentationSlide>>(bundle.SlidesJson) ?? new List<PresentationSlide>();
        var arrangement = DeserializeOrDefault<PresentationArrangement>(bundle.ArrangementJson);

        var project = new PresentationProject
        {
            Manifest = manifest,
            Slides = slides,
            Arrangement = arrangement,
            SourcePath = resolvedPath,
        };

        project.EmbeddedThemes = bundle.Themes
            .Select(entry => new BundleThemeEntry
            {
                FileName = entry.FileName,
                RawJson = entry.Content,
                Template = TryDeserialize<ThemeTemplate>(entry.Content),
            })
            .ToList();

        foreach (var media in project.Manifest.Media)
            media.SourcePath = $"bundle:{media.Path}";

        foreach (var font in project.Manifest.Fonts)
            font.SourcePath = $"bundle:{font.Path}";

        PresentationModelUtilities.NormalizeProject(project);
        return project;
    }

    /// <inheritdoc />
    public void Save(PresentationProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var resolvedPath = _contentDirectories.ResolvePresentationPath(path);
        PresentationModelUtilities.NormalizeProject(project);

        var themes = project.EmbeddedThemes
            .Select(entry => new ThemeFileEntry
            {
                FileName = string.IsNullOrWhiteSpace(entry.FileName)
                    ? $"themes/{entry.Template?.Id ?? Guid.NewGuid().ToString("N")}.json"
                    : entry.FileName.Replace('\\', '/'),
                Content = entry.Template == null
                    ? entry.RawJson
                    : JsonSerializer.Serialize(entry.Template, JsonOptions),
            })
            .ToList();

        var mediaRefs = project.Manifest.Media
            .Select(media => new MediaFileRef
            {
                Id = media.Id,
                SourcePath = string.IsNullOrWhiteSpace(media.SourcePath) ? $"bundle:{NormalizeMediaBundlePath(media)}" : media.SourcePath!,
                BundlePath = NormalizeMediaBundlePath(media),
            })
            .ToList();

        var fontRefs = project.Manifest.Fonts
            .Select(font => new FontFileRef
            {
                Id = font.Id,
                SourcePath = string.IsNullOrWhiteSpace(font.SourcePath) ? $"bundle:{NormalizeFontBundlePath(font)}" : font.SourcePath!,
                BundlePath = NormalizeFontBundlePath(font),
            })
            .ToList();

        var saveState = new BundleSaveState
        {
            ManifestJson = JsonSerializer.Serialize(project.Manifest, JsonOptions),
            SlidesJson = JsonSerializer.Serialize(project.Slides, JsonOptions),
            ArrangementJson = JsonSerializer.Serialize(project.Arrangement, JsonOptions),
            Themes = themes,
            Media = mediaRefs,
            Fonts = fontRefs,
        };

        _cpres.Save(resolvedPath, saveState);
        project.SourcePath = resolvedPath;

        foreach (var media in project.Manifest.Media)
            media.SourcePath = $"bundle:{NormalizeMediaBundlePath(media)}";

        foreach (var font in project.Manifest.Fonts)
            font.SourcePath = $"bundle:{NormalizeFontBundlePath(font)}";

        _logger.LogInformation("Saved presentation project {Title} to {Path}.", project.Manifest.Title, resolvedPath);
    }

    private static T DeserializeOrDefault<T>(string json) where T : new()
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string NormalizeMediaBundlePath(MediaEntry media)
    {
        if (!string.IsNullOrWhiteSpace(media.Path))
            return media.Path.Replace('\\', '/');

        var extension = Path.GetExtension(media.SourcePath ?? media.FileName ?? string.Empty);
        return $"media/{media.Id}{extension}";
    }

    private static string NormalizeFontBundlePath(FontEntry font)
    {
        if (!string.IsNullOrWhiteSpace(font.Path))
            return font.Path.Replace('\\', '/');

        var extension = Path.GetExtension(font.SourcePath ?? font.FullName ?? string.Empty);
        return $"fonts/{font.Id}{extension}";
    }
}