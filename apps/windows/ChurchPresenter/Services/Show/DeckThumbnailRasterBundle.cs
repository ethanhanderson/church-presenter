using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Services.Show;

/// <summary>Compositor thumbnail plus an independent pixel buffer for safe navigation display copies.</summary>
public sealed class DeckThumbnailRasterBundle
{
    public DeckThumbnailRasterBundle(ImageSource source, byte[] pixels, int width, int height)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        Width = width;
        Height = height;
    }

    public ImageSource Source { get; }

    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }
}
