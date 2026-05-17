using System.IO;

namespace ChurchPresenter.Services.Media;

/// <summary>
/// Infers logical media kind from file paths so rendering uses the correct decoder (image vs video vs audio).
/// </summary>
public static class MediaInference
{
    /// <summary>
    /// Infers <c>image</c>, <c>video</c>, or <c>audio</c> from the file extension (works for absolute paths and content-relative paths like <c>Media/Files/id.mp4</c>).
    /// </summary>
    public static string InferMediaTypeFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "image";

        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "mp4" or "mov" or "avi" or "mkv" or "webm" or "wmv" or "m4v" => "video",
            "mp3" or "wav" or "aac" or "m4a" or "flac" or "ogg" or "wma" => "audio",
            _ => "image",
        };
    }

    /// <summary>
    /// Chooses the media type to use for playback. When the catalogued type disagrees with the file extension,
    /// the extension wins for anything other than the generic <c>image</c> default so we never feed video bytes
    /// through the image pipeline or still images through the video pipeline.
    /// </summary>
    public static string ResolveEffectiveMediaType(string? cataloguedType, string? pathForExtension)
    {
        var inferred = InferMediaTypeFromPath(pathForExtension);
        var d = (cataloguedType ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(d))
            return inferred;
        if (d == inferred)
            return inferred;

        // Extension carries a concrete signal (video/audio) — trust it over a wrong manifest type.
        if (inferred != "image")
            return inferred;

        // Inferred "image" includes unknown extensions; keep an explicit video/audio declaration if we could not infer better.
        return d;
    }
}