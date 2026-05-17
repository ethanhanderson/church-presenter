using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;


namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Compiles typed presentation slides into host-neutral scene snapshots.
/// </summary>
public interface ISlideSceneCompiler
{
    /// <summary>
    /// Compiles the supplied request into an immutable slide scene.
    /// </summary>
    /// <param name="request">Scene compile request.</param>
    /// <returns>Compiled scene and diagnostics.</returns>
    SceneCompileResult Compile(SceneCompileRequest request);
}

/// <summary>
/// Default host-neutral compiler for typed presentation slides.
/// </summary>
public sealed partial class SlideSceneCompiler : ISlideSceneCompiler
{
    /// <inheritdoc />
    public SceneCompileResult Compile(SceneCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> warnings = [];
        List<string> errors = [];
        List<SceneDependency> dependencies = [];
        AddPresentationDependency(request, dependencies);

        if (request.Slide == null)
        {
            errors.Add("No slide was supplied for scene compilation.");
            stopwatch.Stop();
            SceneCompileDiagnostics diagnostics = CreateDiagnostics(stopwatch.Elapsed, 0, 0, dependencies, warnings, errors);
            return new SceneCompileResult
            {
                Scene = SlideScene.Empty with { Diagnostics = diagnostics },
                Diagnostics = diagnostics,
            };
        }

        SlideSizeDto size = PresentationModelUtilities.GetBaseSlideSize(
            request.Project?.Manifest.AspectRatio,
            request.Project?.Manifest.SlideSize);
        PixelSize renderSize = new(size.Width, size.Height);
        SceneBackground background = CompileBackground(request.Slide.Background ?? request.ThemeSlide?.Background, dependencies);
        IReadOnlySet<string>? buildVisibleIds = ResolveBuildVisibleLayerIds(request.Slide, request.BuildIndex);
        List<SlideSceneNode> nodes = [];

        foreach (CompiledLayerSource layerSource in EnumerateLayers(request))
        {
            SlideSceneNode? node = CompileLayer(layerSource.Layer, layerSource.SourceReference, request, nodes.Count, buildVisibleIds, dependencies, warnings);
            if (node != null)
                nodes.Add(node);
        }

        stopwatch.Stop();
        string version = CreateVersion(request, background, nodes);
        SceneCompileDiagnostics sceneDiagnostics = CreateDiagnostics(
            stopwatch.Elapsed,
            nodes.Count,
            nodes.Count(static node => node.IsVisible),
            dependencies,
            warnings,
            errors);

        SlideScene scene = new()
        {
            Id = CreateSceneId(request),
            Version = version,
            PresentationId = request.Project?.Manifest.PresentationId,
            PresentationPath = request.Project?.SourcePath,
            SlideId = request.Slide.Id,
            ArrangementInstanceKey = request.ArrangementInstanceKey,
            RenderSize = renderSize,
            Background = background,
            Nodes = nodes,
            Dependencies = dependencies,
            Diagnostics = sceneDiagnostics,
        };

        return new SceneCompileResult
        {
            Scene = scene,
            Diagnostics = sceneDiagnostics,
        };
    }

    private static IEnumerable<CompiledLayerSource> EnumerateLayers(SceneCompileRequest request)
    {
        if (request.ThemeSlide?.Layers != null)
        {
            foreach (SlideLayer layer in request.ThemeSlide.Layers)
            {
                var resolvedLayer = ResolveThemeLayerContent(layer, request.Slide);
                yield return new CompiledLayerSource(resolvedLayer, $"themeLayer:{layer.Id}");
            }
        }

        if (request.Slide?.Layers != null)
        {
            foreach (SlideLayer layer in request.Slide.Layers)
            {
                if (request.ThemeSlide != null && layer is TextLayer textLayer && textLayer.TextBinding != null)
                    continue;

                yield return new CompiledLayerSource(layer, $"slideLayer:{layer.Id}");
            }
        }
    }

    private static SlideLayer ResolveThemeLayerContent(SlideLayer layer, PresentationSlide? slide)
    {
        if (layer is not TextLayer textLayer || slide == null)
            return layer;

        TextLayer resolvedText = PresentationModelUtilities.DeepClone(textLayer) ?? new TextLayer();
        SlideTextBlock? textBlock = PresentationModelUtilities.ResolveTextBlock(slide, textLayer.TextBinding, textLayer.TextBinding?.FallbackIndex ?? 0);
        resolvedText.Content = textBlock?.Text ?? textLayer.TextBinding?.PlaceholderText ?? textLayer.Content;
        return resolvedText;
    }

    private static SlideSceneNode? CompileLayer(
        SlideLayer layer,
        string sourceReference,
        SceneCompileRequest request,
        int zOrder,
        IReadOnlySet<string>? buildVisibleIds,
        ICollection<SceneDependency> dependencies,
        ICollection<string> warnings)
    {
        if (request.VisibleLayerIds is { Count: > 0 } && !request.VisibleLayerIds.Contains(layer.Id))
            return null;

        bool isVisible = layer.Visible && (buildVisibleIds == null || buildVisibleIds.Contains(layer.Id));
        SceneNodeTransform transform = CompileTransform(layer.Transform);

        return layer switch
        {
            TextLayer textLayer => CompileTextLayer(textLayer, request, zOrder, transform, sourceReference, isVisible, dependencies),
            ShapeLayer shapeLayer => CompileShapeLayer(shapeLayer, zOrder, transform, sourceReference, isVisible),
            MediaLayer mediaLayer => CompileMediaLayer(mediaLayer, request, zOrder, transform, sourceReference, isVisible, dependencies),
            WebLayer webLayer => CompileWebLayer(webLayer, zOrder, transform, sourceReference, isVisible),
            VectorLayer vectorLayer => CompileVectorLayer(vectorLayer, zOrder, transform, sourceReference, isVisible, warnings),
            _ => null,
        };
    }

    private static TextSceneNode CompileTextLayer(
        TextLayer layer,
        SceneCompileRequest request,
        int zOrder,
        SceneNodeTransform transform,
        string sourceReference,
        bool isVisible,
        ICollection<SceneDependency> dependencies)
    {
        TextStyleModel style = layer.Style ?? PresentationModelUtilities.CreateDefaultTextStyle();
        IReadOnlyList<string> tokenKeys = TokenPattern()
            .Matches(layer.Content ?? string.Empty)
            .Select(static match => match.Groups["key"].Value)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string resolvedText = ResolveTokens(layer.Content ?? string.Empty, request.RuntimeTokens, tokenKeys, dependencies, layer.Id);

        if (!string.IsNullOrWhiteSpace(style.Font.Family))
        {
            dependencies.Add(new SceneDependency
            {
                Kind = SceneDependencyKind.Font,
                Id = style.Font.Family,
                NodeId = layer.Id,
            });
        }

        return new TextSceneNode
        {
            Id = layer.Id,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? "Text" : layer.Name,
            SourceReference = sourceReference,
            ZOrder = zOrder,
            Transform = transform,
            IsVisible = isVisible,
            IsLocked = layer.Locked,
            BlendMode = layer.BlendMode,
            Text = resolvedText,
            FontFamily = style.Font.Family,
            FontSize = style.Font.Size,
            FontWeight = style.Font.Weight,
            IsItalic = style.Font.Italic,
            Alignment = style.Alignment,
            VerticalAlignment = style.VerticalAlignment,
            LineHeight = style.Font.LineHeight,
            LetterSpacing = style.Font.LetterSpacing,
            Color = layer.Fills?.FirstOrDefault(static fill => fill.Enabled is not false)?.Color ?? style.Color,
            Padding = layer.Padding ?? 0,
            TokenKeys = tokenKeys,
        };
    }

    private static ShapeSceneNode CompileShapeLayer(
        ShapeLayer layer,
        int zOrder,
        SceneNodeTransform transform,
        string sourceReference,
        bool isVisible)
    {
        ShapeStyleModel style = layer.Style ?? new ShapeStyleModel();
        IReadOnlyList<SceneFill> fills = CompileFills(layer.Fills, style.Fill, style.FillOpacity);
        IReadOnlyList<SceneStroke> strokes = CompileStrokes(layer.Strokes, style.Stroke, style.StrokeOpacity, style.StrokeWidth);

        return new ShapeSceneNode
        {
            Id = layer.Id,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? "Shape" : layer.Name,
            SourceReference = sourceReference,
            ZOrder = zOrder,
            Transform = transform,
            IsVisible = isVisible,
            IsLocked = layer.Locked,
            BlendMode = layer.BlendMode,
            ShapeType = layer.ShapeType,
            Fills = fills,
            Strokes = strokes,
        };
    }

    private static MediaSceneNode CompileMediaLayer(
        MediaLayer layer,
        SceneCompileRequest request,
        int zOrder,
        SceneNodeTransform transform,
        string sourceReference,
        bool isVisible,
        ICollection<SceneDependency> dependencies)
    {
        request.DependencyStamps.TryGetValue(layer.MediaId, out var stamp);
        dependencies.Add(new SceneDependency
        {
            Kind = SceneDependencyKind.Media,
            Id = layer.MediaId,
            NodeId = layer.Id,
            IsResolved = !string.IsNullOrWhiteSpace(layer.MediaId) && stamp?.Failure == null,
            Message = string.IsNullOrWhiteSpace(layer.MediaId) ? "Media layer has no media id." : null,
            Stamp = stamp,
            FailureKind = stamp?.Failure?.Kind,
        });

        return new MediaSceneNode
        {
            Id = layer.Id,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? "Media" : layer.Name,
            SourceReference = sourceReference,
            ZOrder = zOrder,
            Transform = transform,
            IsVisible = isVisible,
            IsLocked = layer.Locked,
            BlendMode = layer.BlendMode,
            MediaId = layer.MediaId,
            MediaType = layer.MediaType,
            Fit = layer.Fit,
            Loop = layer.Loop ?? false,
            Muted = layer.Muted ?? false,
            Autoplay = layer.Autoplay ?? false,
        };
    }

    private static void AddPresentationDependency(SceneCompileRequest request, ICollection<SceneDependency> dependencies)
    {
        var presentationPath = request.Project?.SourcePath;
        if (!string.IsNullOrWhiteSpace(presentationPath))
        {
            request.DependencyStamps.TryGetValue(presentationPath, out var stamp);
            dependencies.Add(new SceneDependency
            {
                Kind = SceneDependencyKind.Presentation,
                Id = presentationPath,
                IsResolved = stamp?.Exists != false,
                Stamp = stamp,
                FailureKind = stamp?.Failure?.Kind,
                Message = stamp?.Failure?.Message,
            });
        }

        var themeId = request.ThemeSlide?.Id ?? request.Project?.Manifest.ThemeBinding?.ThemeId ?? request.Project?.Manifest.ThemeId;
        if (!string.IsNullOrWhiteSpace(themeId))
        {
            dependencies.Add(new SceneDependency
            {
                Kind = SceneDependencyKind.Theme,
                Id = themeId,
                IsResolved = request.ThemeSlide != null,
                Message = request.ThemeSlide == null ? "Theme binding did not resolve to a theme slide." : null,
            });
        }
    }


    private static WebSceneNode CompileWebLayer(
        WebLayer layer,
        int zOrder,
        SceneNodeTransform transform,
        string sourceReference,
        bool isVisible)
    {
        return new WebSceneNode
        {
            Id = layer.Id,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? "Web" : layer.Name,
            SourceReference = sourceReference,
            ZOrder = zOrder,
            Transform = transform,
            IsVisible = isVisible,
            IsLocked = layer.Locked,
            BlendMode = layer.BlendMode,
            Url = layer.Url,
            Zoom = layer.Zoom,
            Interactive = layer.Interactive,
            RefreshInterval = layer.RefreshInterval,
        };
    }

    private static VectorSceneNode CompileVectorLayer(
        VectorLayer layer,
        int zOrder,
        SceneNodeTransform transform,
        string sourceReference,
        bool isVisible,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(layer.Path))
            warnings.Add($"Vector layer '{layer.Id}' has no path data.");

        return new VectorSceneNode
        {
            Id = layer.Id,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? "Vector" : layer.Name,
            SourceReference = sourceReference,
            ZOrder = zOrder,
            Transform = transform,
            IsVisible = isVisible,
            IsLocked = layer.Locked,
            BlendMode = layer.BlendMode,
            Path = layer.Path,
            ViewBox = layer.ViewBox,
            FillRule = layer.FillRule,
            Fills = CompileFills(layer.Fills, "#FFFFFF", 1),
            Strokes = CompileStrokes(layer.Strokes, "#000000", 1, 0),
        };
    }

    private static SceneBackground CompileBackground(SlideBackground? background, ICollection<SceneDependency> dependencies)
    {
        return background switch
        {
            TransparentSlideBackground => new SceneBackground
            {
                Color = "transparent",
                AlphaMode = RenderAlphaMode.StraightAlpha,
                Opacity = 0,
            },
            SolidSlideBackground solid => new SceneBackground
            {
                Color = solid.Color,
                AlphaMode = IsTransparentColor(solid.Color) ? RenderAlphaMode.StraightAlpha : RenderAlphaMode.Opaque,
            },
            GradientSlideBackground => new SceneBackground
            {
                Color = "gradient",
                AlphaMode = RenderAlphaMode.Opaque,
            },
            ImageSlideBackground image => AddBackgroundDependency(
                dependencies,
                image.MediaId,
                new SceneBackground
                {
                    MediaId = image.MediaId,
                    MediaType = "image",
                    Fit = image.Fit,
                    Opacity = image.Opacity,
                    AlphaMode = RenderAlphaMode.Opaque,
                }),
            VideoSlideBackground video => AddBackgroundDependency(
                dependencies,
                video.MediaId,
                new SceneBackground
                {
                    MediaId = video.MediaId,
                    MediaType = "video",
                    Fit = video.Fit,
                    Opacity = video.Opacity,
                    AlphaMode = RenderAlphaMode.Opaque,
                }),
            _ => SceneBackground.OpaqueBlack,
        };
    }

    private static SceneBackground AddBackgroundDependency(
        ICollection<SceneDependency> dependencies,
        string mediaId,
        SceneBackground background)
    {
        dependencies.Add(new SceneDependency
        {
            Kind = SceneDependencyKind.Media,
            Id = mediaId,
            NodeId = "background",
            IsResolved = !string.IsNullOrWhiteSpace(mediaId),
            Message = string.IsNullOrWhiteSpace(mediaId) ? "Background media has no media id." : null,
        });
        return background;
    }

    private static SceneNodeTransform CompileTransform(LayerTransformModel transform)
    {
        return new SceneNodeTransform
        {
            X = transform.X,
            Y = transform.Y,
            Width = transform.Width,
            Height = transform.Height,
            Rotation = transform.Rotation,
            Opacity = Math.Clamp(transform.Opacity, 0, 1),
            FlipX = transform.FlipX == true,
            FlipY = transform.FlipY == true,
            ClipContent = transform.ClipContent == true,
            CornerRadius = transform.CornerRadius,
        };
    }

    private static IReadOnlyList<SceneFill> CompileFills(
        IReadOnlyList<LayerFillModel>? fills,
        string fallbackColor,
        double fallbackOpacity)
    {
        SceneFill[] compiled = fills?
            .Where(static fill => fill.Enabled is not false)
            .Select(fill => new SceneFill
            {
                Id = fill.Id,
                Color = fill.Color,
                Opacity = fill.Opacity,
            })
            .ToArray() ?? [];

        return compiled.Length > 0
            ? compiled
            :
            [
                new SceneFill
                {
                    Id = "default-fill",
                    Color = fallbackColor,
                    Opacity = fallbackOpacity,
                },
            ];
    }

    private static IReadOnlyList<SceneStroke> CompileStrokes(
        IReadOnlyList<LayerStrokeModel>? strokes,
        string fallbackColor,
        double fallbackOpacity,
        double fallbackWidth)
    {
        SceneStroke[] compiled = strokes?
            .Where(static stroke => stroke.Enabled is not false)
            .Select(stroke => new SceneStroke
            {
                Id = stroke.Id,
                Color = stroke.Color,
                Opacity = stroke.Opacity,
                Width = stroke.Width,
            })
            .ToArray() ?? [];

        return compiled.Length > 0 || fallbackWidth <= 0
            ? compiled
            :
            [
                new SceneStroke
                {
                    Id = "default-stroke",
                    Color = fallbackColor,
                    Opacity = fallbackOpacity,
                    Width = fallbackWidth,
                },
            ];
    }

    private static IReadOnlySet<string>? ResolveBuildVisibleLayerIds(PresentationSlide slide, int buildIndex)
    {
        if (buildIndex < 0 || slide.Animations?.BuildIn.Count is not > 0)
            return null;

        HashSet<string> visibleIds = slide.Layers
            .Where(static layer => layer.Visible)
            .Select(static layer => layer.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (BuildStep step in slide.Animations.BuildIn.Skip(buildIndex + 1))
        {
            if (!string.IsNullOrWhiteSpace(step.LayerId))
                visibleIds.Remove(step.LayerId);
        }

        return visibleIds;
    }

    private static string ResolveTokens(
        string text,
        IReadOnlyDictionary<string, string> runtimeTokens,
        IEnumerable<string> tokenKeys,
        ICollection<SceneDependency> dependencies,
        string nodeId)
    {
        foreach (string key in tokenKeys)
        {
            bool resolved = runtimeTokens.ContainsKey(key);
            dependencies.Add(new SceneDependency
            {
                Kind = SceneDependencyKind.Token,
                Id = key,
                NodeId = nodeId,
                IsResolved = resolved,
                Message = resolved ? null : $"Runtime token '{key}' was not resolved.",
            });
        }

        return TokenPattern().Replace(text, match =>
        {
            string key = match.Groups["key"].Value;
            return runtimeTokens.TryGetValue(key, out string? value) ? value : match.Value;
        });
    }

    private static SceneCompileDiagnostics CreateDiagnostics(
        TimeSpan elapsed,
        int nodeCount,
        int visibleNodeCount,
        IReadOnlyCollection<SceneDependency> dependencies,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        return new SceneCompileDiagnostics
        {
            CompileDuration = elapsed,
            NodeCount = nodeCount,
            VisibleNodeCount = visibleNodeCount,
            DependencyCount = dependencies.Count,
            Warnings = warnings.ToArray(),
            Errors = errors.ToArray(),
            Performance = new ScenePerformanceMetrics
            {
                CompileTime = elapsed,
                NodeCount = nodeCount,
                VisibleNodeCount = visibleNodeCount,
                MediaNodeCount = dependencies.Count(static dependency => dependency.Kind == SceneDependencyKind.Media),
            },
        };
    }

    private static string CreateSceneId(SceneCompileRequest request)
    {
        string presentationId = request.Project?.Manifest.PresentationId ?? "presentation";
        string slideId = request.Slide?.Id ?? "slide";
        string instanceKey = string.IsNullOrWhiteSpace(request.ArrangementInstanceKey)
            ? "default"
            : request.ArrangementInstanceKey;
        return $"scene:{presentationId}:{slideId}:{instanceKey}";
    }

    private static string CreateVersion(SceneCompileRequest request, SceneBackground background, IReadOnlyList<SlideSceneNode> nodes)
    {
        StringBuilder builder = new();
        builder.Append(request.Project?.Manifest.UpdatedAt).Append('|')
            .Append(request.Slide?.UpdatedAt).Append('|')
            .Append(request.Project?.Manifest.ThemeBinding?.ThemeId).Append('|')
            .Append(request.Slide?.ThemeBinding?.ThemeId).Append('|')
            .Append(request.ThemeSlide?.Id).Append('|')
            .Append(request.ThemeVariantId).Append('|')
            .Append(request.BuildIndex).Append('|')
            .Append(background.Color).Append('|')
            .Append(background.MediaId).Append('|')
            .Append(nodes.Count);

        foreach (SlideSceneNode node in nodes)
        {
            builder.Append('|')
                .Append(node.Id)
                .Append(':')
                .Append(node.Kind)
                .Append(':')
                .Append(node.IsVisible)
                .Append(':')
                .Append(node.Transform.X)
                .Append(',')
                .Append(node.Transform.Y)
                .Append(',')
                .Append(node.Transform.Width)
                .Append(',')
                .Append(node.Transform.Height);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    private sealed record CompiledLayerSource(SlideLayer Layer, string SourceReference);

    private static bool IsTransparentColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return false;

        string normalized = color.Trim();
        return string.Equals(normalized, "transparent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "#00000000", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "#00FFFFFF", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\{\{(?<key>[A-Za-z0-9_.:-]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();
}
