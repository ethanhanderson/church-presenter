
namespace ChurchPresenter.Services.Media;

/// <summary>
/// Resolves the best user-facing display name for media cues and output-layer media.
/// </summary>
public static class MediaCueDisplayNameResolver
{
    /// <summary>Normalizes a stored cue display name into a trimmed nullable string.</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Resolves a user-facing display name for a typed slide media cue.
    /// </summary>
    public static string Resolve(SlideMediaCue? cue, PresentationProject? project = null, string fallback = "Media")
    {
        if (cue == null)
            return fallback;

        return Resolve(cue.DisplayName, cue.MediaId, project, fallback);
    }

    /// <summary>
    /// Resolves a user-facing display name for lightweight slide DTO media cues.
    /// </summary>
    public static string Resolve(SlideMediaCueDto? cue, string fallback = "Media")
    {
        if (cue == null)
            return fallback;

        return Resolve(cue.DisplayName, cue.MediaId, project: null, fallback);
    }

    /// <summary>
    /// Resolves a user-facing display name for output-layer media.
    /// </summary>
    public static string Resolve(OutputLayerMedia? media, PresentationProject? project = null, string fallback = "Media")
    {
        if (media == null)
            return fallback;

        return Resolve(media.DisplayName, media.MediaId, project, fallback);
    }

    /// <summary>
    /// Resolves a display name for a media id using project manifest metadata when available.
    /// </summary>
    public static string? ResolveProjectMediaDisplayName(PresentationProject? project, string? mediaId)
    {
        if (project?.Manifest?.Media == null)
            return null;

        var normalizedMediaId = Normalize(mediaId);
        if (normalizedMediaId == null)
            return null;

        var match = project.Manifest.Media.FirstOrDefault(entry =>
            string.Equals(Normalize(entry.Id), normalizedMediaId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(entry.Path), normalizedMediaId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(entry.SourcePath), normalizedMediaId, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return null;

        var fileName = Normalize(match.FileName);
        if (fileName == null)
            return null;

        var fileStem = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(fileStem) ? fileName : fileStem;
    }

    /// <summary>
    /// Resolves a fallback display name from a media id or path.
    /// </summary>
    public static string ResolveFallback(string? mediaId, string fallback = "Media")
    {
        var normalizedMediaId = Normalize(mediaId);
        if (normalizedMediaId == null)
            return fallback;

        var pathLike = normalizedMediaId.Contains(Path.DirectorySeparatorChar)
                       || normalizedMediaId.Contains(Path.AltDirectorySeparatorChar)
                       || Path.IsPathRooted(normalizedMediaId)
                       || Path.HasExtension(normalizedMediaId);

        if (!pathLike)
            return normalizedMediaId;

        var fileStem = Path.GetFileNameWithoutExtension(normalizedMediaId);
        if (!string.IsNullOrWhiteSpace(fileStem))
            return fileStem;

        var fileName = Path.GetFileName(normalizedMediaId);
        return string.IsNullOrWhiteSpace(fileName) ? normalizedMediaId : fileName;
    }

    private static string Resolve(string? displayName, string? mediaId, PresentationProject? project, string fallback)
    {
        var normalizedDisplayName = Normalize(displayName);
        if (normalizedDisplayName != null)
            return normalizedDisplayName;

        var projectDisplayName = ResolveProjectMediaDisplayName(project, mediaId);
        if (projectDisplayName != null)
            return projectDisplayName;

        return ResolveFallback(mediaId, fallback);
    }
}