
namespace ChurchPresenter.Services.Themes;

/// <summary>
/// Applies theme template slides to presentation slides, including optional scale-to-fit behavior.
/// </summary>
public interface IThemeApplicationService
{
    /// <summary>
    /// Applies a theme slide to the target slide in place.
    /// </summary>
    void ApplyThemeSlideToSlide(PresentationSlide slide, ThemeTemplateSlide themeSlide, ThemeApplyOptions options);

    void ApplyLinkedTheme(PresentationProject project, PresentationSlide? slide, ThemeTemplate theme, ThemeTemplateSlide? themeSlide = null);

    void DetachSlideTheme(PresentationSlide slide, ThemeTemplateSlide themeSlide, ThemeApplyOptions options);

    ThemeTemplate ForkThemeForPresentation(PresentationProject project, ThemeTemplate sourceTheme);

    /// <summary>
    /// Builds a preview clone of a slide after applying a theme slide.
    /// </summary>
    PresentationSlide BuildPreview(PresentationSlide slide, ThemeTemplateSlide themeSlide, ThemeApplyOptions options);
}

/// <summary>
/// Theme-application settings copied from the legacy editor behavior.
/// </summary>
public sealed class ThemeApplyOptions
{
    public string ScaleMode { get; set; } = "none";

    public required SlideSizeDto SourceSize { get; init; }

    public required SlideSizeDto TargetSize { get; init; }
}

/// <inheritdoc />
public sealed class ThemeApplicationService : IThemeApplicationService
{
    public void ApplyLinkedTheme(PresentationProject project, PresentationSlide? slide, ThemeTemplate theme, ThemeTemplateSlide? themeSlide = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(theme);

        PresentationModelUtilities.NormalizeTheme(theme);
        var binding = new PresentationThemeBinding
        {
            ThemeId = theme.Id,
            ThemeVersion = theme.Version,
            ThemeSlideId = themeSlide?.Id,
            Mode = ThemeBindingModes.Linked,
            EmbeddedSnapshotId = EnsureEmbeddedSnapshot(project, theme),
        };

        if (slide == null)
        {
            project.Manifest.ThemeId = theme.Id;
            project.Manifest.ThemeBinding = binding;
            return;
        }

        slide.ThemeBinding = binding;
        slide.LayoutType ??= themeSlide?.LayoutType;
        slide.UpdatedAt = DateTime.UtcNow.ToString("O");
    }

    public void DetachSlideTheme(PresentationSlide slide, ThemeTemplateSlide themeSlide, ThemeApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(themeSlide);
        ArgumentNullException.ThrowIfNull(options);

        ApplyThemeSlideToSlide(slide, themeSlide, options);
        slide.ThemeBinding = new PresentationThemeBinding
        {
            ThemeSlideId = themeSlide.Id,
            Mode = ThemeBindingModes.Detached,
        };
    }

    public ThemeTemplate ForkThemeForPresentation(PresentationProject project, ThemeTemplate sourceTheme)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sourceTheme);

        ThemeTemplate fork = PresentationModelUtilities.CloneTheme(sourceTheme);
        fork.Id = Guid.NewGuid().ToString("N");
        fork.Name = string.IsNullOrWhiteSpace(sourceTheme.Name) ? "Presentation Theme" : $"{sourceTheme.Name} Copy";
        fork.Version = DateTime.UtcNow.ToString("O");
        fork.CreatedAt = DateTime.UtcNow.ToString("O");
        fork.UpdatedAt = fork.CreatedAt;
        PresentationModelUtilities.NormalizeTheme(fork);

        string snapshotId = EnsureEmbeddedSnapshot(project, fork);
        project.Manifest.ThemeId = fork.Id;
        project.Manifest.ThemeBinding = new PresentationThemeBinding
        {
            ThemeId = fork.Id,
            ThemeVersion = fork.Version,
            EmbeddedSnapshotId = snapshotId,
            Mode = ThemeBindingModes.Forked,
        };
        return fork;
    }

    /// <inheritdoc />
    public void ApplyThemeSlideToSlide(PresentationSlide slide, ThemeTemplateSlide themeSlide, ThemeApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(themeSlide);
        ArgumentNullException.ThrowIfNull(options);

        var scaleMode = options.ScaleMode;
        var scaleX = options.TargetSize.Width / (double)Math.Max(1, options.SourceSize.Width);
        var scaleY = options.TargetSize.Height / (double)Math.Max(1, options.SourceSize.Height);
        var uniformScale = Math.Min(scaleX, scaleY);
        var offsetX = scaleMode == "fit"
            ? (options.TargetSize.Width - options.SourceSize.Width * uniformScale) / 2d
            : 0d;
        var offsetY = scaleMode == "fit"
            ? (options.TargetSize.Height - options.SourceSize.Height * uniformScale) / 2d
            : 0d;

        LayerTransformModel MapTransform(LayerTransformModel transform)
        {
            if (scaleMode != "fit")
                return PresentationModelUtilities.DeepClone(transform) ?? new LayerTransformModel();

            var radiusScale = Math.Min(scaleX, scaleY);
            return new LayerTransformModel
            {
                X = transform.X * uniformScale + offsetX,
                Y = transform.Y * uniformScale + offsetY,
                Width = transform.Width * uniformScale,
                Height = transform.Height * uniformScale,
                Rotation = transform.Rotation,
                Opacity = transform.Opacity,
                CornerRadius = (transform.CornerRadius ?? 0) * radiusScale,
                CornerRadiusTopLeft = transform.CornerRadiusTopLeft * radiusScale,
                CornerRadiusTopRight = transform.CornerRadiusTopRight * radiusScale,
                CornerRadiusBottomRight = transform.CornerRadiusBottomRight * radiusScale,
                CornerRadiusBottomLeft = transform.CornerRadiusBottomLeft * radiusScale,
                FlipX = transform.FlipX,
                FlipY = transform.FlipY,
                LockAspectRatio = transform.LockAspectRatio,
                ClipContent = transform.ClipContent,
            };
        }

        TextStyleModel MapTextStyle(TextStyleModel style)
        {
            if (scaleMode != "fit")
                return PresentationModelUtilities.DeepClone(style) ?? PresentationModelUtilities.CreateDefaultTextStyle();

            return new TextStyleModel
            {
                Color = style.Color,
                Alignment = style.Alignment,
                VerticalAlignment = style.VerticalAlignment,
                Font = new TextFontModel
                {
                    Family = style.Font.Family,
                    Size = style.Font.Size * uniformScale,
                    Weight = style.Font.Weight,
                    Italic = style.Font.Italic,
                    LineHeight = style.Font.LineHeight,
                    LetterSpacing = style.Font.LetterSpacing * uniformScale,
                },
                Shadow = new TextShadowModel
                {
                    Enabled = style.Shadow.Enabled,
                    Color = style.Shadow.Color,
                    OffsetX = style.Shadow.OffsetX * uniformScale,
                    OffsetY = style.Shadow.OffsetY * uniformScale,
                    Blur = style.Shadow.Blur * uniformScale,
                },
                Outline = new TextOutlineModel
                {
                    Enabled = style.Outline.Enabled,
                    Color = style.Outline.Color,
                    Width = style.Outline.Width * uniformScale,
                },
                EffectsOrder = style.EffectsOrder == null ? null : new List<string>(style.EffectsOrder),
                ExtensionData = style.ExtensionData == null ? null : new Dictionary<string, System.Text.Json.JsonElement>(style.ExtensionData),
            };
        }

        var targetTextLayers = slide.Layers.OfType<TextLayer>().ToList();
        PresentationModelUtilities.NormalizeSlide(slide, options.TargetSize);
        var themedLayers = PresentationModelUtilities.GetThemeSlideLayers(themeSlide);
        var nextLayers = new List<SlideLayer>();
        var textLayerIndex = 0;

        foreach (var themeLayer in themedLayers)
        {
            switch (themeLayer)
            {
                case TextLayer themedText:
                    {
                        var targetText = textLayerIndex < targetTextLayers.Count ? targetTextLayers[textLayerIndex] : null;
                        var textLayer = PresentationModelUtilities.DeepClone(themedText) ?? new TextLayer();
                        textLayer.Id = targetText?.Id ?? Guid.NewGuid().ToString("N");
                        var textBlock = PresentationModelUtilities.ResolveTextBlock(slide, themedText.TextBinding, textLayerIndex);
                        textLayer.Content = textBlock?.Text ?? targetText?.Content ?? themedText.Content;
                        textLayer.TextBinding = targetText?.TextBinding ?? themedText.TextBinding;
                        textLayer.Name = string.IsNullOrWhiteSpace(textLayer.Name) ? $"Text {textLayerIndex + 1}" : textLayer.Name;
                        textLayer.Transform = MapTransform(themedText.Transform);
                        textLayer.Style = MapTextStyle(themedText.Style ?? PresentationModelUtilities.CreateDefaultTextStyle());
                        nextLayers.Add(textLayer);
                        textLayerIndex++;
                        break;
                    }

                case ShapeLayer themedShape:
                    {
                        var shape = PresentationModelUtilities.DeepClone(themedShape) ?? new ShapeLayer();
                        shape.Id = Guid.NewGuid().ToString("N");
                        shape.Transform = MapTransform(themedShape.Transform);
                        if (scaleMode == "fit")
                        {
                            shape.Style.StrokeWidth *= uniformScale;
                            shape.Style.CornerRadius *= uniformScale;
                        }
                        nextLayers.Add(shape);
                        break;
                    }

                default:
                    {
                        var clonedLayer = PresentationModelUtilities.DeepClone(themeLayer) ?? throw new InvalidOperationException("Theme layer clone failed.");
                        clonedLayer.Id = Guid.NewGuid().ToString("N");
                        clonedLayer.Transform = MapTransform(themeLayer.Transform);
                        nextLayers.Add(clonedLayer);
                        break;
                    }
            }
        }

        slide.Background = PresentationModelUtilities.DeepClone(themeSlide.Background) ?? new SolidSlideBackground { Color = "#000000" };
        slide.Layers = nextLayers;
        slide.MediaCues = themeSlide.MediaCues?.Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue()).ToList()
                          ?? new List<SlideMediaCue>();
        slide.UpdatedAt = DateTime.UtcNow.ToString("O");
        PresentationModelUtilities.NormalizeSlide(slide, options.TargetSize);
    }

    /// <inheritdoc />
    public PresentationSlide BuildPreview(PresentationSlide slide, ThemeTemplateSlide themeSlide, ThemeApplyOptions options)
    {
        var preview = PresentationModelUtilities.CloneSlide(slide);
        ApplyThemeSlideToSlide(preview, themeSlide, options);
        return preview;
    }

    private static string EnsureEmbeddedSnapshot(PresentationProject project, ThemeTemplate theme)
    {
        PresentationModelUtilities.NormalizeTheme(theme);
        BundleThemeEntry? existing = project.EmbeddedThemes.FirstOrDefault(entry =>
            string.Equals(entry.Template?.Id, theme.Id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new BundleThemeEntry
            {
                FileName = $"themes/{theme.Id}.json",
            };
            project.EmbeddedThemes.Add(existing);
        }

        existing.Template = PresentationModelUtilities.CloneTheme(theme);
        existing.FileName = string.IsNullOrWhiteSpace(existing.FileName)
            ? $"themes/{theme.Id}.json"
            : existing.FileName.Replace('\\', '/');
        existing.RawJson = "{}";
        return theme.Id;
    }
}