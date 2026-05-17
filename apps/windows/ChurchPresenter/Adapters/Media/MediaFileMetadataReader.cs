using System.IO;

using Windows.Storage;

namespace ChurchPresenter.Adapters.Media;

/// <summary>Duration and optional pixel dimensions read from Windows file properties.</summary>
internal readonly record struct MediaFileMetadata(double? DurationSeconds, int? Width, int? Height);

/// <summary>
/// Reads duration (and video dimensions) from <see cref="Windows.Storage"/> content properties.
/// </summary>
internal static class MediaFileMetadataReader
{
    /// <summary>
    /// Attempts to read duration for audio/video and dimensions for video.
    /// </summary>
    /// <param name="path">Absolute path to an existing file.</param>
    /// <param name="mediaType">Logical type: <c>video</c>, <c>audio</c>, or <c>image</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Metadata, or <c>null</c> when unavailable.</returns>
    public static async Task<MediaFileMetadata?> TryReadAsync(string? path, string? mediaType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
        {
            return null;
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var t = (mediaType ?? "").ToLowerInvariant();

            if (t == "video")
            {
                var p = await file.Properties.GetVideoPropertiesAsync();
                var d = p.Duration.TotalSeconds;
                double? dur = d > 0 ? d : null;
                int? w = p.Width > 0 ? (int)p.Width : null;
                int? h = p.Height > 0 ? (int)p.Height : null;
                if (dur == null && w == null && h == null)
                {
                    return null;
                }

                return new MediaFileMetadata(dur, w, h);
            }

            if (t == "audio")
            {
                var p = await file.Properties.GetMusicPropertiesAsync();
                var d = p.Duration.TotalSeconds;
                if (d <= 0)
                {
                    return null;
                }

                return new MediaFileMetadata(d, null, null);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}