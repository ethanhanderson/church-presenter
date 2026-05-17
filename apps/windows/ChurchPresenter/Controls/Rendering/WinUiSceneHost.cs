using ChurchPresenter.Backend.Rendering;

using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Windows.Foundation;
using Windows.UI;

namespace ChurchPresenter.Controls.Rendering;

/// <summary>
/// Applies compiled host-neutral slide scenes to WinUI vector elements.
/// </summary>
public interface IWinUiSceneHost
{
    /// <summary>
    /// Adds the compiled scene canvas to the supplied host.
    /// </summary>
    /// <param name="host">Parent host.</param>
    /// <param name="scene">Compiled scene.</param>
    /// <param name="options">Host options and delegates.</param>
    /// <returns>Host apply diagnostics.</returns>
    ScenePerformanceMetrics Apply(Grid host, SlideScene scene, WinUiSceneHostOptions options);
}

/// <summary>
/// Options used by the WinUI scene host.
/// </summary>
public sealed record WinUiSceneHostOptions
{
    /// <summary>Creates a media element for a scene node.</summary>
    public Func<MediaSceneNode, FrameworkElement?>? CreateMediaElement { get; init; }

    /// <summary>Creates a web element for a scene node.</summary>
    public Func<WebSceneNode, FrameworkElement?>? CreateWebElement { get; init; }

    /// <summary>Whether hidden nodes should be skipped.</summary>
    public bool SkipHiddenNodes { get; init; } = true;
}

/// <summary>
/// Default WinUI scene host that renders scene nodes as XAML/Composition-friendly visuals.
/// </summary>
public sealed class WinUiSceneHost : IWinUiSceneHost
{
    /// <inheritdoc />
    public ScenePerformanceMetrics Apply(Grid host, SlideScene scene, WinUiSceneHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(options);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Canvas canvas = new()
        {
            Width = scene.RenderSize.Width,
            Height = scene.RenderSize.Height,
        };
        int visibleNodeCount = 0;
        int mediaNodeCount = 0;

        foreach (SlideSceneNode node in scene.Nodes.OrderBy(static item => item.ZOrder))
        {
            if (options.SkipHiddenNodes && !node.IsVisible)
                continue;

            FrameworkElement? element = CreateElement(node, options);
            if (element == null)
                continue;

            ApplyNodeLayout(element, node.Transform);
            canvas.Children.Add(element);
            visibleNodeCount++;
            if (node.Kind is SlideSceneNodeKind.Media or SlideSceneNodeKind.LiveVideo)
                mediaNodeCount++;
        }

        host.Children.Add(canvas);
        return new ScenePerformanceMetrics
        {
            HostApplyTime = DateTimeOffset.UtcNow - startedAt,
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            NodeCount = scene.Nodes.Count,
            VisibleNodeCount = visibleNodeCount,
            MediaNodeCount = mediaNodeCount,
        };
    }

    private static FrameworkElement? CreateElement(SlideSceneNode node, WinUiSceneHostOptions options)
    {
        return node switch
        {
            TextSceneNode textNode => CreateTextElement(textNode),
            ShapeSceneNode shapeNode => CreateShapeElement(shapeNode),
            MediaSceneNode mediaNode => options.CreateMediaElement?.Invoke(mediaNode) ?? CreatePlaceholder(mediaNode.MediaType),
            WebSceneNode webNode => options.CreateWebElement?.Invoke(webNode) ?? CreatePlaceholder("Web"),
            VectorSceneNode vectorNode => CreateVectorElement(vectorNode),
            LiveVideoSceneNode liveVideoNode => CreatePlaceholder(string.IsNullOrWhiteSpace(liveVideoNode.SourceId) ? "Live Video" : liveVideoNode.SourceId),
            GroupSceneNode groupNode => CreateGroupElement(groupNode, options),
            _ => null,
        };
    }

    private static FrameworkElement CreateTextElement(TextSceneNode node)
    {
        TextBlock textBlock = new()
        {
            Text = node.Text,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = ParseTextAlignment(node.Alignment),
            Foreground = new SolidColorBrush(ParseColor(node.Color)),
            FontSize = node.FontSize,
            FontStyle = node.IsItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)Math.Clamp(node.FontWeight, 100, 900) },
            CharacterSpacing = (int)Math.Round(node.LetterSpacing * 100),
            LineHeight = node.LineHeight is > 0 ? node.FontSize * node.LineHeight.Value : double.NaN,
            HorizontalAlignment = ParseHorizontalAlignment(node.Alignment),
            VerticalAlignment = ParseVerticalAlignment(node.VerticalAlignment),
            FontFamily = new FontFamily(node.FontFamily),
        };

        return new Grid
        {
            Padding = new Thickness(node.Padding / 100d * Math.Max(1, node.Transform.Width)),
            Clip = node.Transform.ClipContent
                ? new RectangleGeometry { Rect = new Rect(0, 0, Math.Max(1, node.Transform.Width), Math.Max(1, node.Transform.Height)) }
                : null,
            Children = { textBlock },
        };
    }

    private static FrameworkElement CreateShapeElement(ShapeSceneNode node)
    {
        SceneFill fill = node.Fills.FirstOrDefault() ?? new SceneFill();
        SceneStroke? stroke = node.Strokes.FirstOrDefault();
        Brush fillBrush = new SolidColorBrush(ParseColor(fill.Color)) { Opacity = fill.Opacity };
        Brush? strokeBrush = stroke == null
            ? null
            : new SolidColorBrush(ParseColor(stroke.Color)) { Opacity = stroke.Opacity };
        double strokeThickness = stroke?.Width ?? 0;

        return node.ShapeType switch
        {
            "ellipse" => new Ellipse
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
            },
            "line" => new Line
            {
                X1 = 0,
                Y1 = node.Transform.Height / 2d,
                X2 = node.Transform.Width,
                Y2 = node.Transform.Height / 2d,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                Stretch = Stretch.Fill,
            },
            "triangle" => new Polygon
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                Points =
                {
                    new Point(node.Transform.Width / 2d, 0),
                    new Point(node.Transform.Width, node.Transform.Height),
                    new Point(0, node.Transform.Height),
                },
            },
            _ => new Rectangle
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                RadiusX = node.Transform.CornerRadius ?? 0,
                RadiusY = node.Transform.CornerRadius ?? 0,
            },
        };
    }

    private static FrameworkElement CreateVectorElement(VectorSceneNode node)
    {
        try
        {
            Geometry geometry = ParsePathGeometry(node.Path);
            SceneFill fill = node.Fills.FirstOrDefault() ?? new SceneFill();
            SceneStroke? stroke = node.Strokes.FirstOrDefault();

            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(ParseColor(fill.Color)) { Opacity = fill.Opacity },
                Stroke = stroke == null ? null : new SolidColorBrush(ParseColor(stroke.Color)) { Opacity = stroke.Opacity },
                StrokeThickness = stroke?.Width ?? 0,
                Stretch = Stretch.Fill,
            };
        }
        catch
        {
            return CreatePlaceholder("Vector");
        }
    }

    private static FrameworkElement CreateGroupElement(GroupSceneNode node, WinUiSceneHostOptions options)
    {
        Grid group = new();
        Canvas canvas = new()
        {
            Width = Math.Max(1, node.Transform.Width),
            Height = Math.Max(1, node.Transform.Height),
        };

        foreach (SlideSceneNode child in node.Children.OrderBy(static item => item.ZOrder))
        {
            FrameworkElement? element = CreateElement(child, options);
            if (element == null)
                continue;

            ApplyNodeLayout(element, child.Transform);
            canvas.Children.Add(element);
        }

        group.Children.Add(canvas);
        return group;
    }

    private static FrameworkElement CreatePlaceholder(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(ParseColor("#111827")),
            BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.15 },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new Grid
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.75 },
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                    },
                },
            },
        };
    }

    private static void ApplyNodeLayout(FrameworkElement element, SceneNodeTransform transform)
    {
        element.Width = transform.Width;
        element.Height = transform.Height;
        element.Opacity = Math.Clamp(transform.Opacity, 0, 1);
        Canvas.SetLeft(element, transform.X);
        Canvas.SetTop(element, transform.Y);
        element.RenderTransform = new CompositeTransform
        {
            Rotation = transform.Rotation,
            ScaleX = transform.FlipX ? -1 : 1,
            ScaleY = transform.FlipY ? -1 : 1,
            CenterX = transform.Width / 2d,
            CenterY = transform.Height / 2d,
        };
    }

    private static Geometry ParsePathGeometry(string pathData)
    {
        string escaped = pathData
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        string xaml = $"<PathGeometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Figures=\"{escaped}\" />";
        return (Geometry)XamlReader.Load(xaml);
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string? alignment)
    {
        return alignment?.ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
    }

    private static VerticalAlignment ParseVerticalAlignment(string? alignment)
    {
        return alignment?.ToLowerInvariant() switch
        {
            "top" => VerticalAlignment.Top,
            "bottom" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
    }

    private static TextAlignment ParseTextAlignment(string? alignment)
    {
        return alignment?.ToLowerInvariant() switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
    }

    private static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Colors.Transparent;

        string trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            string hex = trimmed[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(character => $"{character}{character}"));
            if (hex.Length == 6)
                hex = $"FF{hex}";

            if (hex.Length == 8 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                byte.TryParse(hex[6..8], System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                return Color.FromArgb(a, r, g, b);
            }
        }

        return trimmed.ToLowerInvariant() switch
        {
            "white" => Colors.White,
            "black" => Colors.Black,
            "transparent" => Colors.Transparent,
            _ => Colors.White,
        };
    }
}
