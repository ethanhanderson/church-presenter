#nullable enable

using Microsoft.Graphics.Canvas;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Resolves already-loaded GPU bitmaps for scene background and image nodes.
/// The scene renderer is synchronous, so callers preload bitmaps and expose them
/// through this source keyed by media id or resolved source path.
/// </summary>
public interface ISceneImageSource
{
    /// <summary>Returns a loaded bitmap for a media id or path, or null when unavailable.</summary>
    CanvasBitmap? TryGetBitmap(string mediaIdOrPath);
}
