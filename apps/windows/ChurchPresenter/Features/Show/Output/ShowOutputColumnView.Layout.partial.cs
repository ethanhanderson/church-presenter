using System.Numerics;

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace ChurchPresenter.Features.Show.Output;

public sealed partial class ShowOutputColumnView
{
    private const float CardCornerRadius = 8f;

    private RectangleClip? _mediaTransportCardClip;
    private RectangleClip? _showControlsCardClip;

    private void MediaTransportCardContainer_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateMediaTransportCardClip();
    }

    private void MediaTransportCardContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateMediaTransportCardClip();
    }

    private void MediaTransportCardContainer_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClearRoundedCardClip(MediaTransportCardContainer, ref _mediaTransportCardClip);
    }

    private void ShowControlsCardContainer_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateShowControlsCardClip();
    }

    private void ShowControlsCardContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateShowControlsCardClip();
    }

    private void ShowControlsCardContainer_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ClearRoundedCardClip(ShowControlsCardContainer, ref _showControlsCardClip);
    }

    private void UpdateMediaTransportCardClip() =>
        UpdateRoundedCardClip(MediaTransportCardContainer, ref _mediaTransportCardClip);

    private void UpdateShowControlsCardClip() =>
        UpdateRoundedCardClip(ShowControlsCardContainer, ref _showControlsCardClip);

    private static void UpdateRoundedCardClip(Border? border, ref RectangleClip? clipStorage)
    {
        if (border is null)
            return;

        float width = (float)border.ActualWidth;
        float height = (float)border.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(border);
        RectangleClip clip = clipStorage ??= visual.Compositor.CreateRectangleClip();
        clip.Left = 0;
        clip.Top = 0;
        clip.Right = width;
        clip.Bottom = height;

        var radius = new Vector2(CardCornerRadius, CardCornerRadius);
        clip.TopLeftRadius = radius;
        clip.TopRightRadius = radius;
        clip.BottomLeftRadius = radius;
        clip.BottomRightRadius = radius;
        visual.Clip = clip;
    }

    private static void ClearRoundedCardClip(Border? border, ref RectangleClip? clipStorage)
    {
        if (border is not null)
            ElementCompositionPreview.GetElementVisual(border).Clip = null;

        clipStorage?.Dispose();
        clipStorage = null;
    }
}
