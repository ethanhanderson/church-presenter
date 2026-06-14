using System.Runtime.InteropServices.WindowsRuntime;

using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ChurchPresenter.Services.Show;

/// <summary>Creates compositor-safe display sources from frozen BGRA8 pixel buffers.</summary>
internal static class DeckThumbnailPixelSnapshot
{
    public static async Task<SoftwareBitmapSource?> CreateDisplaySourceAsync(
        byte[] pixels,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied);

        try
        {
            var source = new SoftwareBitmapSource();
            cancellationToken.ThrowIfCancellationRequested();
            await source.SetBitmapAsync(bitmap).AsTask().ConfigureAwait(true);
            return source;
        }
        finally
        {
            bitmap.Dispose();
        }
    }
}
