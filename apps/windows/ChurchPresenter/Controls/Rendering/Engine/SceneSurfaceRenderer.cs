#nullable enable

using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls;

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;

using System.Numerics;

using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// The single canonical Win2D rasterizer for a <see cref="SlideScene"/>. Draws the
/// scene at its native render size onto a composition drawing surface and exposes it
/// as a <see cref="CompositionSurfaceBrush"/>, so every surface engine (output,
/// editor, themes, reflow, thumbnails) shares one faithful rendering path. Video and
/// live-input layers are composited separately by their own renderers; this renderer
/// handles background color/gradient/image plus Text, Shape, Vector, image Media, and
/// Group nodes with transforms, opacity, clipping, and rounded-rect clips.
/// </summary>
public sealed class SceneSurfaceRenderer
{
    private readonly GpuDeviceContext _device;

    /// <summary>Initializes a scene renderer over a shared GPU device context.</summary>
    public SceneSurfaceRenderer(GpuDeviceContext device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>Renders a scene into a new drawing surface sized to the scene's native render size.</summary>
    public CompositionDrawingSurface RenderScene(
        SlideScene scene,
        ISceneImageSource? images = null,
        SceneDrawOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options = NormalizeOptions(options);

        var size = new Size(
            Math.Max(1, scene.RenderSize.Width),
            Math.Max(1, scene.RenderSize.Height));
        CompositionDrawingSurface surface = _device.CreateDrawingSurface(size);

        using (CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(surface))
        {
            DrawScene(session, scene, images, options);
        }

        return surface;
    }

    /// <summary>
    /// Rasters a scene at the target surface size so typography stays vector-sharp on high-DPI
    /// outputs. Presentation-sized scenes are scaled uniformly to fit; the brush is 1:1 with the slot.
    /// </summary>
    public CompositionSurfaceBrush RenderSceneToBrush(
        SlideScene scene,
        SpriteVisual targetVisual,
        Vector2 targetSize,
        ISceneImageSource? images = null,
        SceneDrawOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(targetVisual);
        options = NormalizeOptions(options);

        var surfaceSize = new PixelSize(
            Math.Max(1, (int)Math.Round(targetSize.X)),
            Math.Max(1, (int)Math.Round(targetSize.Y)));
        (SlideScene fittedScene, UniformFitMetrics fit) = SlideSceneFitter.Uniform(scene, surfaceSize);

        CompositionDrawingSurface surface = _device.CreateDrawingSurface(
            new Size(surfaceSize.Width, surfaceSize.Height));

        using (CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(surface))
        {
            session.TextAntialiasing = CanvasTextAntialiasing.ClearType;
            session.Antialiasing = CanvasAntialiasing.Antialiased;
            if (fit.IsFullBleed)
            {
                DrawScene(session, fittedScene, images, options);
            }
            else
            {
                session.Clear(SceneColor.Transparent);
                Matrix3x2 previous = session.Transform;
                session.Transform = Matrix3x2.CreateTranslation((float)fit.OffsetX, (float)fit.OffsetY) * previous;
                try
                {
                    DrawScene(session, fittedScene, images, options);
                }
                finally
                {
                    session.Transform = previous;
                }
            }
        }

        CompositionSurfaceBrush brush = _device.Compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        brush.HorizontalAlignmentRatio = 0f;
        brush.VerticalAlignmentRatio = 0f;
        targetVisual.Brush = brush;
        targetVisual.Size = targetSize;
        return brush;
    }

    /// <summary>
    /// Draws a scene onto an arbitrary Win2D drawing session (composition surface or offscreen
    /// <c>CanvasRenderTarget</c>). Shared by the live composition path and the offscreen
    /// <c>ThumbnailEngine</c> so thumbnails and exports use the exact same rasterizer as output.
    /// </summary>
    public static void Draw(
        CanvasDrawingSession session,
        SlideScene scene,
        ISceneImageSource? images = null,
        SceneDrawOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        DrawScene(session, scene, images, NormalizeOptions(options));
    }

    private static void DrawScene(
        CanvasDrawingSession session,
        SlideScene scene,
        ISceneImageSource? images,
        SceneDrawOptions options)
    {
        DrawBackground(session, scene, images, options);

        foreach (SlideSceneNode node in scene.Nodes.OrderBy(static n => n.ZOrder))
            DrawNode(session, node, images, Matrix3x2.Identity);
    }

    private static SceneDrawOptions NormalizeOptions(SceneDrawOptions options) =>
        options.ShowTransparencyPattern || options.PreviewBackgroundFill != null
            ? options
            : SceneDrawOptions.Output;

    private static void DrawBackground(
        CanvasDrawingSession session,
        SlideScene scene,
        ISceneImageSource? images,
        SceneDrawOptions options)
    {
        SceneBackground background = scene.Background;

        var bounds = new Rect(0, 0, scene.RenderSize.Width, scene.RenderSize.Height);

        if (IsTransparentBackground(background))
        {
            if (options.PreviewBackgroundFill is Color fill)
            {
                if (fill.A == 255)
                    session.Clear(fill);
                else
                {
                    if (options.ShowTransparencyPattern)
                        DrawCheckerboard(session, bounds, options);
                    session.FillRectangle(bounds, fill);
                }
            }
            else if (options.ShowTransparencyPattern)
                DrawCheckerboard(session, bounds, options);
            else
                session.Clear(SceneColor.Transparent);
        }
        else
        {
            session.Clear(SceneColor.Parse(background.Color, SceneColor.Black));
        }

        if (background.GradientStops.Count > 0)
            DrawGradient(session, bounds, background.GradientAngle ?? 0, background.GradientStops, background.Opacity);

        if (!string.IsNullOrWhiteSpace(background.MediaId) && images?.TryGetBitmap(background.MediaId!) is { } bitmap)
            DrawBitmap(session, bitmap, bounds, background.Fit ?? "cover", background.Opacity);
    }

    private static void DrawNode(CanvasDrawingSession session, SlideSceneNode node, ISceneImageSource? images, Matrix3x2 parent)
    {
        if (!node.IsVisible)
            return;

        SceneNodeTransform transform = node.Transform;
        Matrix3x2 frame = BuildFrameTransform(transform) * parent;
        Matrix3x2 previous = session.Transform;
        session.Transform = frame;

        var bounds = new Rect(transform.X, transform.Y, Math.Max(0, transform.Width), Math.Max(0, transform.Height));
        bool needsLayer = transform.Opacity < 0.999 || transform.ClipContent;
        CanvasActiveLayer? layer = null;
        if (needsLayer)
        {
            float opacity = (float)Math.Clamp(transform.Opacity, 0d, 1d);
            if (transform.ClipContent && transform.CornerRadius is { } radius && radius > 0)
            {
                using CanvasGeometry clip = CanvasGeometry.CreateRoundedRectangle(
                    session, (float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height,
                    (float)radius, (float)radius);
                layer = session.CreateLayer(opacity, clip);
            }
            else if (transform.ClipContent)
            {
                layer = session.CreateLayer(opacity, bounds);
            }
            else
            {
                layer = session.CreateLayer(opacity);
            }
        }

        try
        {
            switch (node)
            {
                case TextSceneNode text:
                    DrawText(session, text, bounds);
                    break;
                case ShapeSceneNode shape:
                    DrawShape(session, shape, bounds);
                    break;
                case VectorSceneNode vector:
                    DrawVector(session, vector, bounds, frame);
                    break;
                case MediaSceneNode media when images?.TryGetBitmap(ResolveMediaKey(media)) is { } bitmap:
                    DrawBitmap(session, bitmap, bounds, media.Fit, 1d);
                    break;
                case GroupSceneNode group:
                    foreach (SlideSceneNode child in group.Children.OrderBy(static n => n.ZOrder))
                        DrawNode(session, child, images, frame);
                    break;
            }
        }
        finally
        {
            layer?.Dispose();
            session.Transform = previous;
        }
    }

    private static Matrix3x2 BuildFrameTransform(SceneNodeTransform transform)
    {
        var center = new Vector2(
            (float)(transform.X + transform.Width / 2),
            (float)(transform.Y + transform.Height / 2));
        Matrix3x2 matrix = Matrix3x2.Identity;

        if (transform.FlipX || transform.FlipY)
            matrix *= Matrix3x2.CreateScale(transform.FlipX ? -1f : 1f, transform.FlipY ? -1f : 1f, center);

        if (Math.Abs(transform.Rotation) > double.Epsilon)
            matrix *= Matrix3x2.CreateRotation((float)(transform.Rotation * Math.PI / 180d), center);

        return matrix;
    }

    private static void DrawText(CanvasDrawingSession session, TextSceneNode text, Rect bounds)
    {
        using var format = new CanvasTextFormat
        {
            FontFamily = string.IsNullOrWhiteSpace(text.FontFamily) ? "Segoe UI" : text.FontFamily,
            FontSize = (float)Math.Max(1, text.FontSize),
            FontWeight = new FontWeight { Weight = (ushort)Math.Clamp(text.FontWeight, 1, 999) },
            FontStyle = text.IsItalic ? FontStyle.Italic : FontStyle.Normal,
            HorizontalAlignment = ParseHorizontalAlignment(text.Alignment),
            VerticalAlignment = ParseVerticalAlignment(text.VerticalAlignment),
            WordWrapping = CanvasWordWrapping.Wrap,
            LineSpacingMode = CanvasLineSpacingMode.Proportional,
        };

        if (text.LineHeight is { } lineHeight && lineHeight > 0)
            format.LineSpacing = (float)lineHeight;

        double padding = Math.Max(0, text.Padding);
        var layoutBounds = new Rect(
            bounds.X + padding,
            bounds.Y + padding,
            Math.Max(1, bounds.Width - padding * 2),
            Math.Max(1, bounds.Height - padding * 2));

        string content = text.Text ?? string.Empty;
        using var layout = new CanvasTextLayout(
            session, content, format,
            (float)layoutBounds.Width, (float)layoutBounds.Height);

        session.DrawTextLayout(
            layout,
            (float)layoutBounds.X,
            (float)layoutBounds.Y,
            SceneColor.Parse(text.Color, SceneColor.White));
    }

    private static void DrawShape(CanvasDrawingSession session, ShapeSceneNode shape, Rect bounds)
    {
        using CanvasGeometry geometry = CreateShapeGeometry(session, shape, bounds);

        foreach (SceneFill fill in shape.Fills)
            session.FillGeometry(geometry, SceneColor.WithOpacity(SceneColor.Parse(fill.Color, SceneColor.White), fill.Opacity));

        foreach (SceneStroke stroke in shape.Strokes)
        {
            session.DrawGeometry(
                geometry,
                SceneColor.WithOpacity(SceneColor.Parse(stroke.Color, SceneColor.Black), stroke.Opacity),
                (float)Math.Max(0.5, stroke.Width));
        }
    }

    private static CanvasGeometry CreateShapeGeometry(CanvasDrawingSession session, ShapeSceneNode shape, Rect bounds)
    {
        float x = (float)bounds.X;
        float y = (float)bounds.Y;
        float w = (float)Math.Max(1, bounds.Width);
        float h = (float)Math.Max(1, bounds.Height);

        return shape.ShapeType?.ToLowerInvariant() switch
        {
            "ellipse" or "circle" => CanvasGeometry.CreateEllipse(session, x + w / 2, y + h / 2, w / 2, h / 2),
            "rounded" or "roundedrectangle" => CanvasGeometry.CreateRoundedRectangle(session, x, y, w, h, Math.Min(w, h) * 0.1f, Math.Min(w, h) * 0.1f),
            _ => CanvasGeometry.CreateRectangle(session, x, y, w, h),
        };
    }

    private static void DrawVector(CanvasDrawingSession session, VectorSceneNode vector, Rect bounds, Matrix3x2 frame)
    {
        if (!string.IsNullOrWhiteSpace(vector.Path))
        {
            try
            {
                string svg = BuildSvgMarkup(vector, bounds);
                using var document = CanvasSvgDocument.LoadFromXml(session.Device, svg);
                Matrix3x2 previous = session.Transform;
                session.Transform = Matrix3x2.CreateTranslation((float)bounds.X, (float)bounds.Y) * frame;
                session.DrawSvg(document, new Size(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)));
                session.Transform = previous;
                return;
            }
            catch (Exception)
            {
                // Fall back to a filled bounding rect when path data cannot be parsed.
            }
        }

        Color fallback = vector.Fills.Count > 0
            ? SceneColor.WithOpacity(SceneColor.Parse(vector.Fills[0].Color, SceneColor.White), vector.Fills[0].Opacity)
            : SceneColor.White;
        session.FillRectangle(bounds, fallback);
    }

    private static string BuildSvgMarkup(VectorSceneNode vector, Rect bounds)
    {
        string viewBox = string.IsNullOrWhiteSpace(vector.ViewBox)
            ? $"0 0 {Math.Max(1, bounds.Width).ToString(System.Globalization.CultureInfo.InvariantCulture)} {Math.Max(1, bounds.Height).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : vector.ViewBox!;
        string fill = vector.Fills.Count > 0 ? vector.Fills[0].Color : "#FFFFFF";
        string fillRule = string.IsNullOrWhiteSpace(vector.FillRule) ? "nonzero" : vector.FillRule!;
        string stroke = vector.Strokes.Count > 0 ? $" stroke=\"{vector.Strokes[0].Color}\" stroke-width=\"{vector.Strokes[0].Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"" : string.Empty;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBox}\"><path d=\"{System.Security.SecurityElement.Escape(vector.Path)}\" fill=\"{fill}\" fill-rule=\"{fillRule}\"{stroke}/></svg>";
    }

    private static void DrawBitmap(CanvasDrawingSession session, CanvasBitmap bitmap, Rect bounds, string fit, double opacity)
    {
        Rect dest = ComputeFitRect(bitmap.Size, bounds, fit);
        session.DrawImage(bitmap, dest, new Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height), (float)Math.Clamp(opacity, 0d, 1d));
    }

    private static Rect ComputeFitRect(Size source, Rect bounds, string fit)
    {
        if (source.Width <= 0 || source.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return bounds;

        double scaleX = bounds.Width / source.Width;
        double scaleY = bounds.Height / source.Height;

        double scale = fit?.ToLowerInvariant() switch
        {
            "fill" or "stretch" => 0, // handled below as exact bounds
            "contain" or "uniform" => Math.Min(scaleX, scaleY),
            "none" => 1,
            _ => Math.Max(scaleX, scaleY), // cover / uniformtofill
        };

        if (scale == 0)
            return bounds;

        double width = source.Width * scale;
        double height = source.Height * scale;
        double left = bounds.X + (bounds.Width - width) / 2;
        double top = bounds.Y + (bounds.Height - height) / 2;
        return new Rect(left, top, width, height);
    }

    private static void DrawGradient(
        CanvasDrawingSession session,
        Rect bounds,
        double angleDegrees,
        IReadOnlyList<SceneGradientStop> stops,
        double opacity)
    {
        CanvasGradientStop[] gradientStops = stops
            .OrderBy(static s => s.Position)
            .Select(s => new CanvasGradientStop
            {
                Position = (float)Math.Clamp(s.Position / 100d, 0d, 1d),
                Color = SceneColor.WithOpacity(SceneColor.Parse(s.Color, SceneColor.Black), opacity),
            })
            .ToArray();

        if (gradientStops.Length == 0)
            return;

        double radians = angleDegrees * Math.PI / 180d;
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        float halfLength = (float)(Math.Abs(Math.Cos(radians)) * bounds.Width + Math.Abs(Math.Sin(radians)) * bounds.Height) / 2f;
        var direction = new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians));

        using var brush = new CanvasLinearGradientBrush(session, gradientStops)
        {
            StartPoint = center - direction * halfLength,
            EndPoint = center + direction * halfLength,
        };
        session.FillRectangle(bounds, brush);
    }

    private static bool IsTransparentBackground(SceneBackground background)
    {
        if (background.AlphaMode == RenderAlphaMode.Opaque)
            return false;

        if (background.GradientStops.Count > 0 || !string.IsNullOrWhiteSpace(background.MediaId))
            return false;

        if (string.IsNullOrWhiteSpace(background.Color))
            return true;

        return SceneColor.Parse(background.Color, SceneColor.Black).A == 0;
    }

    private static void DrawCheckerboard(CanvasDrawingSession session, Rect bounds, SceneDrawOptions options)
    {
        int tileCount = options.CheckerboardTileCount;
        int cellSize = tileCount > 0
            ? (int)Math.Max(3, Math.Round(bounds.Width / tileCount))
            : CheckerboardPatternBrush.DefaultCellSize;

        byte rLight, gLight, bLight, rDark, gDark, bDark;
        if (options.IsDarkTheme)
        {
            rDark = 0x25; gDark = 0x28; bDark = 0x30;
            rLight = 0x2E; gLight = 0x32; bLight = 0x3C;
        }
        else
        {
            rLight = 0xF5; gLight = 0xF8; bLight = 0xFC;
            rDark = 0xE8; gDark = 0xEA; bDark = 0xEF;
        }

        int startX = (int)Math.Floor(bounds.X);
        int startY = (int)Math.Floor(bounds.Y);
        int endX = (int)Math.Ceiling(bounds.Right);
        int endY = (int)Math.Ceiling(bounds.Bottom);

        for (int y = startY; y < endY; y += cellSize)
        {
            int tileHeight = Math.Min(cellSize, endY - y);
            for (int x = startX; x < endX; x += cellSize)
            {
                int tileWidth = Math.Min(cellSize, endX - x);
                bool darkCell = (((x / cellSize) + (y / cellSize)) & 1) == 0;
                session.FillRectangle(
                    x,
                    y,
                    tileWidth,
                    tileHeight,
                    darkCell
                        ? Color.FromArgb(255, rDark, gDark, bDark)
                        : Color.FromArgb(255, rLight, gLight, bLight));
            }
        }
    }

    private static string ResolveMediaKey(MediaSceneNode media) =>
        string.IsNullOrWhiteSpace(media.MediaId) ? media.SourceReference ?? string.Empty : media.MediaId;

    private static CanvasHorizontalAlignment ParseHorizontalAlignment(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "left" or "start" => CanvasHorizontalAlignment.Left,
            "right" or "end" => CanvasHorizontalAlignment.Right,
            "justify" => CanvasHorizontalAlignment.Justified,
            _ => CanvasHorizontalAlignment.Center,
        };

    private static CanvasVerticalAlignment ParseVerticalAlignment(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "top" => CanvasVerticalAlignment.Top,
            "bottom" => CanvasVerticalAlignment.Bottom,
            _ => CanvasVerticalAlignment.Center,
        };
}
