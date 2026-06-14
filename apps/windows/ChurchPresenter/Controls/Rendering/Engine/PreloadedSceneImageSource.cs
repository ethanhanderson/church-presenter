#nullable enable

using Microsoft.Graphics.Canvas;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// An <see cref="ISceneImageSource"/> backed by GPU bitmaps preloaded from resolved file paths.
/// The scene renderer is synchronous, so callers resolve and load image assets up front and expose
/// them here keyed by both the scene media id/source reference and the resolved path.
/// </summary>
public sealed class PreloadedSceneImageSource : ISceneImageSource, IDisposable
{
    /// <summary>An empty image source for scenes that have no image dependencies.</summary>
    public static readonly PreloadedSceneImageSource Empty = new(new Dictionary<string, CanvasBitmap>());

    private readonly Dictionary<string, CanvasBitmap> _bitmaps;
    private readonly bool _ownsBitmaps;

    private PreloadedSceneImageSource(Dictionary<string, CanvasBitmap> bitmaps, bool ownsBitmaps = false)
    {
        _bitmaps = bitmaps;
        _ownsBitmaps = ownsBitmaps;
    }

    /// <summary>Wraps an already-loaded key-to-bitmap map. The source disposes the distinct bitmaps when <paramref name="ownsBitmaps"/> is true.</summary>
    public static PreloadedSceneImageSource Create(IDictionary<string, CanvasBitmap> bitmaps, bool ownsBitmaps)
    {
        ArgumentNullException.ThrowIfNull(bitmaps);
        return new PreloadedSceneImageSource(
            new Dictionary<string, CanvasBitmap>(bitmaps, StringComparer.OrdinalIgnoreCase),
            ownsBitmaps);
    }

    /// <inheritdoc />
    public CanvasBitmap? TryGetBitmap(string mediaIdOrPath)
    {
        if (string.IsNullOrWhiteSpace(mediaIdOrPath))
            return null;
        return _bitmaps.GetValueOrDefault(mediaIdOrPath);
    }

    /// <summary>
    /// Loads the supplied key-to-path map into GPU bitmaps. Keys that fail to load (missing file,
    /// unsupported format) are skipped so a thumbnail still renders its text and shapes.
    /// </summary>
    public static async Task<PreloadedSceneImageSource> LoadAsync(
        CanvasDevice device,
        IReadOnlyDictionary<string, string> keyToPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(keyToPath);

        Dictionary<string, CanvasBitmap> bitmaps = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CanvasBitmap> byPath = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, string path) in keyToPath)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            if (!byPath.TryGetValue(path, out CanvasBitmap? bitmap))
            {
                try
                {
                    bitmap = await CanvasBitmap.LoadAsync(device, path).AsTask(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    continue;
                }

                byPath[path] = bitmap;
            }

            bitmaps[key] = bitmap;
            bitmaps[path] = bitmap;
        }

        return new PreloadedSceneImageSource(bitmaps, ownsBitmaps: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_ownsBitmaps)
            return;

        foreach (CanvasBitmap bitmap in _bitmaps.Values.Distinct())
            bitmap.Dispose();
        _bitmaps.Clear();
    }
}
