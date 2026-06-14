using System.Runtime.InteropServices.WindowsRuntime;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ChurchPresenter.Controls;

/// <summary>
/// Checkerboard for transparent slide previews. Tile counts are expressed across the native slide
/// render width; the Win2D scene renderer converts them to pixel cell size per surface.
/// </summary>
public static class CheckerboardPatternBrush
{
    /// <summary>Fallback cell size when no tile count is supplied (logical px).</summary>
    public const int DefaultCellSize = 6;

    /// <summary>Dense checker for deck cards and editor slide-list thumbs (~8–10 px when letterboxed).</summary>
    public const int ThumbnailTileCount = 32;

    /// <summary>Main editor / reflow / theme canvas previews.</summary>
    public const int EditorTileCount = 40;

    /// <summary>Generic preview surfaces (non-editor, non-thumbnail intent).</summary>
    public const int PreviewTileCount = 36;

    /// <summary>
    /// Creates an <see cref="ImageBrush"/> sized exactly to <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/>.
    /// Light theme: white / light grey. Dark theme: dark grey / slightly lighter dark grey.
    /// </summary>
    public static ImageBrush CreateBrush(int pixelWidth, int pixelHeight, int cellSize = DefaultCellSize, bool isDarkTheme = false)
    {
        pixelWidth = Math.Clamp(pixelWidth, 1, 4096);
        pixelHeight = Math.Clamp(pixelHeight, 1, 4096);
        cellSize = Math.Clamp(cellSize, 2, 64);

        var w = pixelWidth;
        var h = pixelHeight;
        var wb = new WriteableBitmap(w, h);
        var bytes = new byte[w * h * 4];

        byte bLight, gLight, rLight, bDark, gDark, rDark;
        const byte a = 0xFF;
        if (isDarkTheme)
        {
            // Dark UI: two blue-grey tints (contrast preserved; not applied to image/video layers).
            bDark = 0x30; gDark = 0x28; rDark = 0x25;
            bLight = 0x3C; gLight = 0x32; rLight = 0x2E;
        }
        else
        {
            // Light UI: cool-neutral tints (Fluent-style layer hint).
            bLight = 0xFC; gLight = 0xF8; rLight = 0xF5;
            bDark = 0xEF; gDark = 0xEA; rDark = 0xE8;
        }

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var darkCell = (((x / cellSize) + (y / cellSize)) & 1) == 0;
                bytes[i] = darkCell ? bDark : bLight;
                bytes[i + 1] = darkCell ? gDark : gLight;
                bytes[i + 2] = darkCell ? rDark : rLight;
                bytes[i + 3] = a;
            }
        }

        using (var stream = wb.PixelBuffer.AsStream())
            stream.Write(bytes, 0, bytes.Length);

        return new ImageBrush
        {
            ImageSource = wb,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
    }
}