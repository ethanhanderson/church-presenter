namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Scales compiled slide scenes from presentation coordinates into a target output surface so
/// typography and vector geometry rasterize at the destination pixel density instead of being
/// bitmap-stretched after the fact.
/// </summary>
public static class SlideSceneFitter
{
    /// <summary>
    /// Uniformly scales a scene to fit inside <paramref name="surfaceSize"/> while preserving aspect
    /// ratio. The returned scene uses <paramref name="surfaceSize"/> as its render size; content is
    /// centered with letterboxing handled at draw time via <see cref="UniformFitMetrics"/>.
    /// </summary>
    public static (SlideScene Scene, UniformFitMetrics Metrics) Uniform(SlideScene scene, PixelSize surfaceSize)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.RenderSize.Width <= 0 || scene.RenderSize.Height <= 0)
            return (scene with { RenderSize = surfaceSize }, UniformFitMetrics.Identity(surfaceSize));

        if (scene.RenderSize.Width == surfaceSize.Width && scene.RenderSize.Height == surfaceSize.Height)
            return (scene, UniformFitMetrics.Identity(surfaceSize));

        double scaleX = surfaceSize.Width / (double)scene.RenderSize.Width;
        double scaleY = surfaceSize.Height / (double)scene.RenderSize.Height;
        double scale = Math.Min(scaleX, scaleY);

        int contentWidth = Math.Max(1, (int)Math.Round(scene.RenderSize.Width * scale));
        int contentHeight = Math.Max(1, (int)Math.Round(scene.RenderSize.Height * scale));
        double offsetX = (surfaceSize.Width - contentWidth) / 2.0;
        double offsetY = (surfaceSize.Height - contentHeight) / 2.0;

        SlideScene scaled = scene with
        {
            RenderSize = new PixelSize(contentWidth, contentHeight),
            Nodes = scene.Nodes.Select(ScaleNode).ToArray(),
        };

        var metrics = new UniformFitMetrics(surfaceSize, new PixelSize(contentWidth, contentHeight), offsetX, offsetY);
        return (scaled, metrics);

        SlideSceneNode ScaleNode(SlideSceneNode node) =>
            node switch
            {
                TextSceneNode text => text with
                {
                    Transform = ScaleTransform(text.Transform),
                    FontSize = text.FontSize * scale,
                    Padding = text.Padding * scale,
                    LineHeight = text.LineHeight * scale,
                    LetterSpacing = text.LetterSpacing * scale,
                },
                ShapeSceneNode shape => shape with
                {
                    Transform = ScaleTransform(shape.Transform),
                    Strokes = shape.Strokes
                        .Select(stroke => stroke with { Width = stroke.Width * scale })
                        .ToArray(),
                },
                VectorSceneNode vector => vector with
                {
                    Transform = ScaleTransform(vector.Transform),
                    Strokes = vector.Strokes
                        .Select(stroke => stroke with { Width = stroke.Width * scale })
                        .ToArray(),
                },
                GroupSceneNode group => group with
                {
                    Transform = ScaleTransform(group.Transform),
                    Children = group.Children.Select(ScaleNode).ToArray(),
                },
                _ => node with { Transform = ScaleTransform(node.Transform) },
            };

        SceneNodeTransform ScaleTransform(SceneNodeTransform transform) =>
            transform with
            {
                X = transform.X * scale,
                Y = transform.Y * scale,
                Width = transform.Width * scale,
                Height = transform.Height * scale,
                CornerRadius = transform.CornerRadius * scale,
            };
    }
}

/// <summary>Letterbox placement for a scene scaled with <see cref="SlideSceneFitter"/>.</summary>
public sealed record UniformFitMetrics(
    PixelSize SurfaceSize,
    PixelSize ContentSize,
    double OffsetX,
    double OffsetY)
{
    /// <summary>Creates a 1:1 fit when the scene already matches the surface.</summary>
    public static UniformFitMetrics Identity(PixelSize surfaceSize) =>
        new(surfaceSize, surfaceSize, 0, 0);

    /// <summary>True when content fills the surface without letterboxing.</summary>
    public bool IsFullBleed =>
        ContentSize.Width == SurfaceSize.Width && ContentSize.Height == SurfaceSize.Height;
}
