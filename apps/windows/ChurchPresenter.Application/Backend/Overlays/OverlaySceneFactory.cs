#nullable enable

using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Overlays;

/// <summary>
/// Compiles generated text overlays (messages, announcements, stage messages) into real
/// <see cref="SlideScene"/> snapshots via a themed template, so output hosts render them through
/// the same scene rasterizer as slides instead of treating them as text placeholders.
/// </summary>
public static class OverlaySceneFactory
{
    /// <summary>Builds a themed text overlay scene for a generated overlay, or null when there is no text.</summary>
    public static SlideScene? CreateTextOverlayScene(string overlayId, OverlayContentKind kind, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        PixelSize size = PixelSize.FullHd;
        bool lowerThird = kind is OverlayContentKind.Message or OverlayContentKind.StageMessage;

        double regionHeight = lowerThird ? size.Height * 0.22 : size.Height * 0.8;
        double regionY = lowerThird ? size.Height * 0.72 : size.Height * 0.1;
        double fontSize = lowerThird ? 54 : 80;
        string verticalAlignment = lowerThird ? "bottom" : "middle";

        return new SlideScene
        {
            Id = $"scene:overlay:{overlayId}",
            Version = $"{kind}:{text.Length}:{text.GetHashCode(StringComparison.Ordinal):X8}",
            RenderSize = size,
            Background = new SceneBackground { AlphaMode = RenderAlphaMode.StraightAlpha, Opacity = 0 },
            Nodes =
            [
                new TextSceneNode
                {
                    Id = $"overlay-text:{overlayId}",
                    Name = "Overlay Text",
                    Text = text,
                    ZOrder = 0,
                    FontSize = fontSize,
                    Alignment = "center",
                    VerticalAlignment = verticalAlignment,
                    Color = "#FFFFFF",
                    Padding = 48,
                    Transform = new SceneNodeTransform
                    {
                        X = size.Width * 0.05,
                        Y = regionY,
                        Width = size.Width * 0.9,
                        Height = regionHeight,
                    },
                },
            ],
        };
    }
}
