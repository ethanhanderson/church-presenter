#nullable enable

using ChurchPresenter.Adapters.FileSystem;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Documents;

using Microsoft.Graphics.Canvas;

using Windows.Media.Editing;
using Windows.Storage;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Resolves a compiled <see cref="SlideScene"/>'s background and image/video media references to
/// GPU bitmaps so the offscreen <see cref="SceneSurfaceRenderer"/> can rasterize them. Image media
/// load directly; video media contribute a first-frame still (parity with the legacy thumbnail
/// path). Unresolved or failed assets are skipped so a thumbnail still renders its text and shapes.
/// </summary>
public static class SceneImageSourceFactory
{
    private sealed record MediaReference(string Key, string? MediaId, string? MediaType);

    /// <summary>Builds a preloaded image source for every resolvable image/video reference in the scene.</summary>
    public static async Task<PreloadedSceneImageSource> CreateAsync(
        SlideScene scene,
        IBundleAssetCacheService assets,
        PresentationProject? project,
        int targetSize = 512,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(assets);

        List<MediaReference> references = [];
        CollectReferences(scene, references);
        if (references.Count == 0)
            return PreloadedSceneImageSource.Empty;

        CanvasDevice device = CanvasDevice.GetSharedDevice();
        Dictionary<string, CanvasBitmap> byKey = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CanvasBitmap> byPath = new(StringComparer.OrdinalIgnoreCase);

        foreach (MediaReference reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(reference.MediaId))
                continue;

            string? path = assets.ResolveMediaPath(project, reference.MediaId);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            if (!byPath.TryGetValue(path, out CanvasBitmap? bitmap))
            {
                bitmap = await TryLoadBitmapAsync(device, path, reference.MediaType, targetSize, cancellationToken).ConfigureAwait(false);
                if (bitmap == null)
                    continue;
                byPath[path] = bitmap;
            }

            byKey[reference.Key] = bitmap;
            if (!string.IsNullOrWhiteSpace(reference.MediaId))
                byKey[reference.MediaId!] = bitmap;
            byKey[path] = bitmap;
        }

        return byKey.Count == 0
            ? PreloadedSceneImageSource.Empty
            : PreloadedSceneImageSource.Create(byKey, ownsBitmaps: true);
    }

    private static void CollectReferences(SlideScene scene, ICollection<MediaReference> references)
    {
        SceneBackground background = scene.Background;
        if (!string.IsNullOrWhiteSpace(background.MediaId))
            references.Add(new MediaReference(background.MediaId!, background.MediaId, background.MediaType));

        foreach (SlideSceneNode node in scene.Nodes)
            CollectNodeReferences(node, references);
    }

    private static void CollectNodeReferences(SlideSceneNode node, ICollection<MediaReference> references)
    {
        switch (node)
        {
            case MediaSceneNode media:
                string key = string.IsNullOrWhiteSpace(media.MediaId) ? media.SourceReference ?? string.Empty : media.MediaId;
                string? mediaId = string.IsNullOrWhiteSpace(media.MediaId) ? media.SourceReference : media.MediaId;
                if (!string.IsNullOrWhiteSpace(key))
                    references.Add(new MediaReference(key, mediaId, media.MediaType));
                break;
            case GroupSceneNode group:
                foreach (SlideSceneNode child in group.Children)
                    CollectNodeReferences(child, references);
                break;
        }
    }

    private static async Task<CanvasBitmap?> TryLoadBitmapAsync(
        CanvasDevice device,
        string path,
        string? mediaType,
        int targetSize,
        CancellationToken cancellationToken)
    {
        bool isVideo = string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isVideo)
                return await TryLoadVideoFrameAsync(device, path, targetSize).ConfigureAwait(false);

            return await CanvasBitmap.LoadAsync(device, path).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<CanvasBitmap?> TryLoadVideoFrameAsync(CanvasDevice device, string path, int targetSize)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        MediaClip clip = await MediaClip.CreateFromFileAsync(file);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        using var thumbnail = await composition.GetThumbnailAsync(
            TimeSpan.Zero,
            targetSize,
            0,
            VideoFramePrecision.NearestFrame);

        return thumbnail == null ? null : await CanvasBitmap.LoadAsync(device, thumbnail).AsTask().ConfigureAwait(false);
    }
}
