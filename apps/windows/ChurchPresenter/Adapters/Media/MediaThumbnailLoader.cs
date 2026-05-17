using System.IO;


using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace ChurchPresenter.Adapters.Media;

/// <summary>
/// Loads thumbnail-sized image sources for media files shown in WinUI chrome surfaces.
/// Prefers real file pixels for photos and real preview frames for video assets, while still using
/// Windows shell thumbnails when they provide a richer preview than a generic file icon.
/// </summary>
internal static class MediaThumbnailLoader
{
    internal sealed record ThumbnailLoadResult(ImageSource? Image, ContentAccessFailureKind? FailureKind, string? DiagnosticMessage)
    {
        public bool Succeeded => Image is not null;
    }

    /// <summary>
    /// Attempts to create an <see cref="ImageSource"/> for the given media file.
    /// </summary>
    /// <param name="path">Absolute file path to the source media.</param>
    /// <param name="mediaType">Logical media type: image, video, or audio.</param>
    /// <param name="requestedSize">Preferred thumbnail size in pixels.</param>
    /// <returns>A thumbnail-sized image source, or <c>null</c> when no thumbnail is available.</returns>
    public static async Task<ImageSource?> TryLoadAsync(string? path, string? mediaType, uint requestedSize = 256)
    {
        var result = await TryLoadDetailedAsync(path, mediaType, requestedSize);
        return result.Image;
    }

    /// <summary>
    /// Attempts to create a thumbnail and classifies why one was not available.
    /// </summary>
    public static async Task<ThumbnailLoadResult> TryLoadDetailedAsync(string? path, string? mediaType, uint requestedSize = 256)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ThumbnailLoadResult(null, ContentAccessFailureKind.Missing, "Media path is empty.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return new ThumbnailLoadResult(null, ContentAccessFailureKind.Unavailable, "Media path is invalid.");
        }

        if (!Path.IsPathRooted(fullPath) || !File.Exists(fullPath))
            return new ThumbnailLoadResult(null, ContentAccessFailureKind.Missing, "Media file is missing.");

        var effective = MediaInference.ResolveEffectiveMediaType(mediaType, fullPath);
        var isImageType = string.Equals(effective, "image", StringComparison.OrdinalIgnoreCase);

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fullPath);

            // Raster photos should show the file itself, not a shell-generated proxy, when we can decode it.
            if (isImageType && IsLikelyDecodeFriendlyRasterImage(fullPath))
            {
                try
                {
                    return new ThumbnailLoadResult(await TryDecodeImageFromFileAsync(file, requestedSize), null, null);
                }
                catch
                {
                    // Corrupt or unsupported decode — try shell thumbnails below.
                }
            }

            var thumbnail = await TryLoadFromWindowsThumbnailApisAsync(file, effective, requestedSize);
            if (thumbnail != null)
                return new ThumbnailLoadResult(thumbnail, null, null);

            if (string.Equals(effective, "video", StringComparison.OrdinalIgnoreCase))
            {
                var firstFrame = await TryLoadVideoFrameAsync(file, requestedSize);
                if (firstFrame != null)
                    return new ThumbnailLoadResult(firstFrame, null, null);
            }

            // e.g. HEIC or other types catalogued as image where the shell had no Image thumbnail.
            if (isImageType && !IsLikelyDecodeFriendlyRasterImage(fullPath))
            {
                return new ThumbnailLoadResult(await TryDecodeImageFromFileAsync(file, requestedSize), null, null);
            }
        }
        catch
        {
            return new ThumbnailLoadResult(null, ContentAccessFailureKind.Corrupt, "Thumbnail source could not be decoded.");
        }

        return new ThumbnailLoadResult(null, ContentAccessFailureKind.Unavailable, "No usable thumbnail provider returned an image.");
    }

    /// <summary>Thumbnail option sets to try; shell behavior varies by codec/container.</summary>
    private static readonly ThumbnailOptions[] ThumbnailOptionAttempts =
    {
        ThumbnailOptions.UseCurrentScale | ThumbnailOptions.ResizeThumbnail,
        ThumbnailOptions.UseCurrentScale,
        ThumbnailOptions.ResizeThumbnail,
        ThumbnailOptions.None,
    };

    private static async Task<BitmapImage?> TryLoadFromWindowsThumbnailApisAsync(
        StorageFile file,
        string effective,
        uint requestedSize)
    {
        foreach (var mode in ThumbnailModesForType(effective))
        {
            foreach (var options in ThumbnailOptionAttempts)
            {
                var scaled = await TryLoadFromScaledThumbnailAsync(file, mode, requestedSize, options);
                if (scaled != null)
                    return scaled;

                var shell = await TryLoadFromShellThumbnailAsync(file, mode, requestedSize, options);
                if (shell != null)
                    return shell;
            }
        }

        return null;
    }

    private static IEnumerable<ThumbnailMode> ThumbnailModesForType(string effective)
    {
        switch (effective.ToLowerInvariant())
        {
            case "video":
                yield return ThumbnailMode.VideosView;
                yield return ThumbnailMode.SingleItem;
                yield return ThumbnailMode.PicturesView;
                yield break;
            case "audio":
                yield return ThumbnailMode.MusicView;
                yield return ThumbnailMode.SingleItem;
                yield return ThumbnailMode.PicturesView;
                yield break;
            case "image":
                yield return ThumbnailMode.PicturesView;
                yield return ThumbnailMode.SingleItem;
                yield break;
            default:
                yield return ThumbnailMode.SingleItem;
                yield return ThumbnailMode.PicturesView;
                yield break;
        }
    }

    /// <summary>
    /// Extensions we expect <see cref="BitmapImage"/> to decode reliably; avoids shell returning a type icon for common photo formats.
    /// </summary>
    private static bool IsLikelyDecodeFriendlyRasterImage(string fullPath)
    {
        var ext = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        return ext is "jpg" or "jpeg" or "jpe" or "jfif" or "png" or "gif" or "bmp" or "webp" or "ico" or "tif" or "tiff" or "wdp" or "jxr";
    }

    private static async Task<BitmapImage?> TryLoadFromScaledThumbnailAsync(
        StorageFile file,
        ThumbnailMode mode,
        uint requestedSize,
        ThumbnailOptions options)
    {
        var thumbnail = await file.GetScaledImageAsThumbnailAsync(mode, requestedSize, options);
        return await TryCreateBitmapFromThumbnailAsync(thumbnail, requestedSize);
    }

    private static async Task<BitmapImage?> TryLoadFromShellThumbnailAsync(
        StorageFile file,
        ThumbnailMode mode,
        uint requestedSize,
        ThumbnailOptions options)
    {
        var thumbnail = await file.GetThumbnailAsync(mode, requestedSize, options);
        return await TryCreateBitmapFromThumbnailAsync(thumbnail, requestedSize);
    }

    private static async Task<BitmapImage?> TryCreateBitmapFromThumbnailAsync(
        StorageItemThumbnail? thumbnail,
        uint requestedSize)
    {
        if (thumbnail == null || thumbnail.Size == 0)
            return null;

        if (thumbnail.Type == ThumbnailType.Icon)
        {
            thumbnail.Dispose();
            return null;
        }

        using (thumbnail)
        {
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = (int)requestedSize,
                DecodePixelHeight = (int)requestedSize,
            };
            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
    }

    private static async Task<BitmapImage?> TryLoadVideoFrameAsync(StorageFile file, uint requestedSize)
    {
        var clip = await MediaClip.CreateFromFileAsync(file);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        using var thumbnail = await composition.GetThumbnailAsync(
            TimeSpan.Zero,
            (int)requestedSize,
            0,
            VideoFramePrecision.NearestFrame);

        if (thumbnail == null || thumbnail.Size == 0)
            return null;

        var bitmap = new BitmapImage
        {
            DecodePixelWidth = (int)requestedSize,
        };
        await bitmap.SetSourceAsync(thumbnail);
        return bitmap;
    }

    private static async Task<BitmapImage?> TryDecodeImageFromFileAsync(StorageFile file, uint requestedSize)
    {
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var bitmap = new BitmapImage
        {
            DecodePixelWidth = (int)requestedSize,
            DecodePixelHeight = (int)requestedSize,
        };
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}