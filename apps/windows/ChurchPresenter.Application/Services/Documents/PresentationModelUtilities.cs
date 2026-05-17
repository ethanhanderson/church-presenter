using System.Text.Json;
using System.Text.Json.Serialization;


namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Shared helpers for normalizing, cloning, and creating typed presentation and theme models.
/// </summary>
public static class PresentationModelUtilities
{
    public const string DefaultOutputScaleMode = "fit";

    private static readonly JsonSerializerOptions CloneJsonOptions = PresentationJsonSerialization.CreateOptions();

    public static readonly string[] SongSections =
    {
        "title",
        "intro",
        "verse",
        "pre-chorus",
        "chorus",
        "bridge",
        "refrain",
        "tag",
        "vamp",
        "interlude",
        "outro",
        "ending",
        "custom",
    };

    public static SlideSizeDto GetBaseSlideSize(string? aspectRatio, SlideSizeDto? slideSize = null)
    {
        if (slideSize is { Width: > 0, Height: > 0 })
        {
            return new SlideSizeDto
            {
                Width = slideSize.Width,
                Height = slideSize.Height,
            };
        }

        return aspectRatio switch
        {
            "4:3" => new SlideSizeDto { Width = 1440, Height = 1080 },
            "16:10" => new SlideSizeDto { Width = 1920, Height = 1200 },
            _ => new SlideSizeDto { Width = 1920, Height = 1080 },
        };
    }

    public static PresentationProject CloneProject(PresentationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return DeepClone(project) ?? new PresentationProject();
    }

    public static ThemeTemplate CloneTheme(ThemeTemplate theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return DeepClone(theme) ?? new ThemeTemplate();
    }

    public static PresentationSlide CloneSlide(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return DeepClone(slide) ?? new PresentationSlide();
    }

    public static T? DeepClone<T>(T value)
    {
        if (value == null)
            return default;

        var json = JsonSerializer.Serialize(value, CloneJsonOptions);
        return JsonSerializer.Deserialize<T>(json, CloneJsonOptions);
    }

    public static void NormalizeProject(PresentationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        project.Manifest ??= new PresentationManifest();
        project.Manifest.OutputScaleMode = NormalizeOutputScaleMode(project.Manifest.OutputScaleMode);
        project.Manifest.Media ??= new List<MediaEntry>();
        project.Manifest.Fonts ??= new List<FontEntry>();
        project.Manifest.ThemeBinding = NormalizeThemeBinding(project.Manifest.ThemeBinding);
        project.Manifest.SlideSize = GetBaseSlideSize(project.Manifest.AspectRatio, project.Manifest.SlideSize);
        project.Slides ??= new List<PresentationSlide>();
        project.Arrangement ??= new PresentationArrangement();
        project.EmbeddedThemes ??= new List<BundleThemeEntry>();

        foreach (var slide in project.Slides)
            NormalizeSlide(slide, project.Manifest.SlideSize);

        NormalizeArrangement(project);

        project.Arrangement.DefaultTransition =
            TransitionStorageNormalizer.NormalizeForStorage(project.Arrangement.DefaultTransition);

        foreach (var theme in project.EmbeddedThemes.Where(entry => entry.Template != null))
            NormalizeTheme(theme.Template!);
    }

    public static string NormalizeOutputScaleMode(string? outputScaleMode) =>
        string.Equals(outputScaleMode, "fill", StringComparison.OrdinalIgnoreCase)
            ? "fill"
            : DefaultOutputScaleMode;

    /// <summary>
    /// Returns a stable string identity for signatures and render diffing (manifest id, then title).
    /// Avoids <see cref="object.GetHashCode"/> on <see cref="PresentationProject"/> instances, which changes per object reference.
    /// </summary>
    public static string StablePresentationKey(PresentationProject? project)
    {
        if (project?.Manifest == null)
            return "";

        var id = project.Manifest.PresentationId?.Trim();
        if (!string.IsNullOrEmpty(id))
            return id;

        var title = project.Manifest.Title?.Trim();
        if (!string.IsNullOrEmpty(title))
            return title;

        return "";
    }

    public static void NormalizeTheme(ThemeTemplate theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        theme.BaseSize = GetBaseSlideSize(theme.AspectRatio, theme.BaseSize);
        theme.Folder = string.IsNullOrWhiteSpace(theme.Folder) ? null : theme.Folder.Trim();
        theme.Tags ??= new List<string>();
        theme.SupportedAspectRatios ??= new List<string>();
        if (theme.SupportedAspectRatios.Count == 0 && !string.IsNullOrWhiteSpace(theme.AspectRatio))
            theme.SupportedAspectRatios.Add(theme.AspectRatio);
        theme.RoleAliases ??= new List<ThemeRoleAlias>();
        theme.StyleTokens ??= new ThemeStyleTokens();
        theme.StyleTokens.Fonts ??= new List<ThemeFontToken>();
        theme.StyleTokens.Colors ??= new List<ThemeColorToken>();
        theme.StyleTokens.Shapes ??= new List<ThemeShapeToken>();
        theme.Version = string.IsNullOrWhiteSpace(theme.Version)
            ? FirstNonWhiteSpace(theme.UpdatedAt, theme.CreatedAt, "1")
            : theme.Version;
        theme.Slides ??= new List<ThemeTemplateSlide>();

        foreach (var slide in theme.Slides)
        {
            slide.Roles ??= new List<string>();
            slide.RoleAliases ??= new List<string>();
            if (!string.IsNullOrWhiteSpace(slide.LayoutType) && slide.Roles.Count == 0)
                slide.Roles.Add(NormalizeRole(slide.LayoutType));
            slide.Background ??= new SolidSlideBackground { Color = "#000000" };
            slide.MediaCues ??= new List<SlideMediaCue>();
            foreach (var cue in slide.MediaCues)
            {
                cue.DisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName);
                cue.Transition = TransitionStorageNormalizer.NormalizeForStorage(cue.Transition);
            }
            if (slide.Layers.Count == 0 && slide.LegacyTextLayers?.Count > 0)
            {
                slide.Layers = slide.LegacyTextLayers
                    .Select(ConvertLegacyThemeTextLayer)
                    .Cast<SlideLayer>()
                    .ToList();
            }

            foreach (var layer in slide.Layers)
                NormalizeLayer(layer, theme.BaseSize);
        }
    }

    public static void NormalizeSlide(PresentationSlide slide, SlideSizeDto? slideSize)
    {
        ArgumentNullException.ThrowIfNull(slide);

        slide.Layers ??= new List<SlideLayer>();
        slide.TextBlocks ??= new List<SlideTextBlock>();
        slide.ThemeBinding = NormalizeThemeBinding(slide.ThemeBinding);
        slide.MediaCues ??= new List<SlideMediaCue>();
        slide.Actions ??= new List<SlideActionDefinition>();
        slide.Background ??= new SolidSlideBackground { Color = "#000000" };
        slide.Animations ??= new SlideAnimations();
        slide.Animations.BuildIn ??= new List<BuildStep>();
        slide.Animations.BuildOut ??= new List<BuildStep>();

        foreach (var layer in slide.Layers)
            NormalizeLayer(layer, slideSize);

        NormalizeTextBlocks(slide);

        slide.Animations.Transition = TransitionStorageNormalizer.NormalizeForStorage(slide.Animations.Transition);
        foreach (var cue in slide.MediaCues)
        {
            cue.DisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName);
            cue.Transition = TransitionStorageNormalizer.NormalizeForStorage(cue.Transition);
        }
    }

    public static void NormalizeLayer(SlideLayer layer, SlideSizeDto? slideSize)
    {
        ArgumentNullException.ThrowIfNull(layer);

        layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? layer.GetType().Name : layer.Name;
        layer.Transform ??= CreateDefaultTransform(slideSize);
        if (layer.Transform.Width <= 0 || layer.Transform.Height <= 0)
        {
            var defaultTransform = CreateDefaultTransform(slideSize);
            if (layer.Transform.Width <= 0)
                layer.Transform.Width = defaultTransform.Width;
            if (layer.Transform.Height <= 0)
                layer.Transform.Height = defaultTransform.Height;
        }

        layer.Fills ??= new List<LayerFillModel>();
        layer.Strokes ??= new List<LayerStrokeModel>();
        layer.Effects ??= new List<LayerEffectModel>();

        switch (layer)
        {
            case TextLayer textLayer:
                textLayer.Style ??= CreateDefaultTextStyle();
                textLayer.TextBinding = NormalizeTextBinding(textLayer.TextBinding);
                var textFills = textLayer.Fills ??= new List<LayerFillModel>();
                if (!textFills.Any())
                {
                    textFills.Add(new LayerFillModel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Color = textLayer.Style.Color,
                        Opacity = 1,
                        Enabled = true,
                    });
                }
                textLayer.Padding ??= 2;
                textLayer.TextFit ??= "auto";
                break;

            case ShapeLayer shapeLayer:
                shapeLayer.Style ??= new ShapeStyleModel();
                var shapeFills = shapeLayer.Fills ??= new List<LayerFillModel>();
                if (!shapeFills.Any())
                {
                    shapeFills.Add(new LayerFillModel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Color = shapeLayer.Style.Fill,
                        Opacity = shapeLayer.Style.FillOpacity,
                        Enabled = true,
                    });
                }
                var shapeStrokes = shapeLayer.Strokes ??= new List<LayerStrokeModel>();
                if (!shapeStrokes.Any())
                {
                    shapeStrokes.Add(new LayerStrokeModel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Color = shapeLayer.Style.Stroke,
                        Opacity = shapeLayer.Style.StrokeOpacity,
                        Width = shapeLayer.Style.StrokeWidth,
                        Position = "inside",
                        Sides = "all",
                        Enabled = true,
                    });
                }
                break;

            case MediaLayer mediaLayer:
                mediaLayer.Fit = string.IsNullOrWhiteSpace(mediaLayer.Fit) ? "contain" : mediaLayer.Fit;
                mediaLayer.Autoplay ??= true;
                mediaLayer.Muted ??= mediaLayer.MediaType == "video";
                mediaLayer.Loop ??= mediaLayer.MediaType == "video";
                break;

            case WebLayer webLayer:
                if (webLayer.Zoom <= 0)
                    webLayer.Zoom = 1;
                webLayer.RefreshInterval ??= 0;
                break;
        }
    }

    public static LayerTransformModel CreateDefaultTransform(SlideSizeDto? slideSize)
    {
        var size = GetBaseSlideSize("16:9", slideSize);
        return new LayerTransformModel
        {
            X = size.Width * 0.05,
            Y = size.Height * 0.3,
            Width = size.Width * 0.9,
            Height = size.Height * 0.4,
            Rotation = 0,
            Opacity = 1,
            CornerRadius = 0,
            FlipX = false,
            FlipY = false,
            LockAspectRatio = false,
            ClipContent = false,
        };
    }

    public static TextStyleModel CreateDefaultTextStyle()
    {
        return new TextStyleModel
        {
            Font = new TextFontModel
            {
                Family = "Segoe UI",
                Size = 72,
                Weight = 700,
                Italic = false,
                LineHeight = 1.2,
                LetterSpacing = 0,
            },
            Color = "#FFFFFF",
            Alignment = "center",
            VerticalAlignment = "middle",
            Shadow = new TextShadowModel
            {
                Enabled = false,
                Color = "#000000",
                OffsetX = 2,
                OffsetY = 2,
                Blur = 8,
            },
            Outline = new TextOutlineModel
            {
                Enabled = false,
                Color = "#000000",
                Width = 2,
            },
            EffectsOrder = new List<string> { "shadow", "outline" },
        };
    }

    public static PresentationSlide CreateSlide(
        string type = "blank",
        string? content = null,
        string? section = null,
        string? sectionLabel = null,
        int? sectionIndex = null,
        SlideSizeDto? slideSize = null)
    {
        var now = DateTime.UtcNow.ToString("O");
        var slide = new PresentationSlide
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Section = section,
            SectionLabel = sectionLabel ?? (section == null ? null : FormatSectionLabel(section, sectionIndex)),
            SectionIndex = sectionIndex,
            Background = new TransparentSlideBackground(),
            Animations = new SlideAnimations(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (!string.IsNullOrWhiteSpace(content))
        {
            slide.Layers.Add(CreateTextLayer(content, slideSize: slideSize));
            slide.TextBlocks.Add(CreateTextBlock(content, "body", "Text", slide.Layers.OfType<TextLayer>().FirstOrDefault()?.Id));
        }

        NormalizeSlide(slide, slideSize);
        return slide;
    }

    public static TextLayer CreateTextLayer(
        string content,
        LayerTransformModel? transform = null,
        TextStyleModel? style = null,
        string? name = null,
        SlideSizeDto? slideSize = null)
    {
        var textLayer = new TextLayer
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "Text" : name,
            Locked = false,
            Visible = true,
            Transform = transform ?? CreateDefaultTransform(slideSize),
            Content = content,
            TextBinding = new ThemeTextBinding { Role = "body", FallbackIndex = 0, PlaceholderText = content },
            Style = style ?? CreateDefaultTextStyle(),
            TextFit = "auto",
            Padding = 2,
        };

        NormalizeLayer(textLayer, slideSize);
        return textLayer;
    }

    public static ShapeLayer CreateShapeLayer(string shapeType = "rectangle", SlideSizeDto? slideSize = null)
    {
        var size = GetBaseSlideSize("16:9", slideSize);
        var layer = new ShapeLayer
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Shape",
            Locked = false,
            Visible = true,
            Transform = new LayerTransformModel
            {
                X = size.Width * 0.35,
                Y = size.Height * 0.35,
                Width = size.Width * 0.3,
                Height = size.Height * 0.2,
                Rotation = 0,
                Opacity = 1,
                CornerRadius = 0,
            },
            ShapeType = shapeType,
            Style = new ShapeStyleModel
            {
                Fill = "#3B82F6",
                FillOpacity = 1,
                Stroke = "#1D4ED8",
                StrokeWidth = 2,
                StrokeOpacity = 1,
                CornerRadius = 0,
            },
        };

        NormalizeLayer(layer, slideSize);
        return layer;
    }

    public static ThemeTemplate CreateThemeFromSlide(string name, PresentationSlide slide, string aspectRatio)
    {
        var now = DateTime.UtcNow.ToString("O");
        var theme = new ThemeTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            AspectRatio = string.IsNullOrWhiteSpace(aspectRatio) ? "16:9" : aspectRatio,
        };

        theme.BaseSize = GetBaseSlideSize(theme.AspectRatio);
        theme.Slides.Add(CreateThemeSlideFromSlide(slide));
        NormalizeTheme(theme);
        return theme;
    }

    public static ThemeTemplateSlide CreateThemeSlideFromSlide(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var textLayers = slide.Layers.OfType<TextLayer>().ToList();
        var clonedLayers = slide.Layers
            .Select((layer, index) =>
            {
                var cloned = DeepClone(layer)!;
                if (cloned is TextLayer textLayer)
                {
                    var textIndex = textLayers.FindIndex(candidate => string.Equals(candidate.Id, layer.Id, StringComparison.OrdinalIgnoreCase));
                    textLayer.TextBinding ??= new ThemeTextBinding
                    {
                        Role = ResolveTextBlockRole(slide, textIndex),
                        FallbackIndex = textIndex < 0 ? index : textIndex,
                        PlaceholderText = textLayer.Content,
                    };
                }

                return cloned;
            })
            .Where(layer => layer != null)
            .ToList();

        return new ThemeTemplateSlide
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = slide.SectionLabel,
            LayoutType = slide.LayoutType,
            Roles = [NormalizeRole(FirstNonWhiteSpace(slide.LayoutType, slide.Section, slide.SectionLabel, "body"))],
            Background = DeepClone(slide.Background) ?? new SolidSlideBackground { Color = "#000000" },
            Layers = clonedLayers,
            MediaCues = slide.MediaCues.Select(cue => DeepClone(cue)!).Where(cue => cue != null).ToList()!,
        };
    }

    public static PresentationArrangement CreateArrangement(IEnumerable<PresentationSlide> slides)
    {
        var slideList = slides.ToList();
        var sections = BuildSectionGroups(slideList);

        return new PresentationArrangement
        {
            Order = slideList.Select(slide => slide.Id).ToList(),
            Sections = sections,
        };
    }

    /// <summary>
    /// Builds <see cref="SectionGroup"/> objects from the given slide list, assigning stable IDs.
    /// Slides are grouped by consecutive (Section, SectionIndex) runs — so Verse 1 and Verse 2 become
    /// separate groups even though they share the same Section value.
    /// </summary>
    public static List<SectionGroup> BuildSectionGroups(IReadOnlyList<PresentationSlide> slides)
    {
        var sections = new List<SectionGroup>();

        // Group by (Section, SectionIndex) to keep Verse 1 / Verse 2 separate.
        foreach (var grouping in slides
                     .Where(s => !string.IsNullOrWhiteSpace(s.Section))
                     .GroupBy(s => (s.Section!, s.SectionIndex), new SectionIndexComparer()))
        {
            var first = grouping.First();
            sections.Add(new SectionGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Section = grouping.Key.Item1,
                Label = first.SectionLabel ?? FormatSectionLabel(grouping.Key.Item1, grouping.Key.Item2),
                SlideIds = grouping.Select(s => s.Id).ToList(),
            });
        }

        return sections;
    }

    /// <summary>
    /// Updates the natural/base section groups for a project after slide mutations, preserving
    /// stable group IDs and any existing named arrangements that reference still-valid groups.
    /// This is the safe replacement for <c>Project.Arrangement = CreateArrangement(Project.Slides)</c>.
    /// </summary>
    public static void ReconcileArrangement(PresentationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Arrangement ??= new PresentationArrangement();

        var newGroups = BuildSectionGroups(project.Slides);
        var existingSections = project.Arrangement.Sections ?? [];
        var duplicateIds = existingSections
            .Where(g => !string.IsNullOrWhiteSpace(g.Id))
            .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var duplicateKeys = existingSections
            .GroupBy(SectionGroupKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        // Preserve IDs from existing groups by matching on (Section, Label).
        var existingById = existingSections
            .Where(g => !string.IsNullOrEmpty(g.Id))
            .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var existingByKey = existingSections
            .GroupBy(SectionGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var ng in newGroups)
        {
            var key = SectionGroupKey(ng);
            if (existingByKey.TryGetValue(key, out var existing) && !string.IsNullOrEmpty(existing.Id))
                ng.Id = existing.Id;
        }

        project.Arrangement.Order = project.Slides.Select(s => s.Id).ToList();
        project.Arrangement.Sections = newGroups;

        // Rebuild or create the natural arrangement entry.
        var naturalArr = project.Arrangement.Arrangements.FirstOrDefault(a => a.IsNatural);
        if (naturalArr == null)
        {
            naturalArr = new NamedArrangement
            {
                Id = "natural",
                Name = "Master",
                IsNatural = true,
            };
            project.Arrangement.Arrangements.Insert(0, naturalArr);
        }
        else
        {
            // Ensure it's first.
            project.Arrangement.Arrangements.Remove(naturalArr);
            project.Arrangement.Arrangements.Insert(0, naturalArr);
        }

        naturalArr.Groups = newGroups
            .Select(g => new ArrangementGroupRef { SectionGroupId = g.Id })
            .ToList();

        // Prune group refs in custom arrangements that no longer exist.
        var validIds = newGroups.Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var arr in project.Arrangement.Arrangements.Where(a => !a.IsNatural))
        {
            arr.Groups = arr.Groups
                .Where(r => validIds.Contains(r.SectionGroupId))
                .ToList();
        }
    }

    /// <summary>
    /// Builds the ordered playback sequence for the active arrangement of a project.
    /// All slides (including disabled) are included; callers skip disabled at navigation time.
    /// </summary>
    public static PlaybackSequence BuildPlaybackSequence(PresentationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var arrangement = project.Arrangement;
        if (arrangement == null)
            return PlaybackSequence.Empty;

        var duplicateSectionIds = arrangement.Sections
            .Where(section => !string.IsNullOrWhiteSpace(section.Id))
            .GroupBy(section => section.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateSectionIds.Length > 0)
            return BuildNaturalSequence(project);

        var activeId = arrangement.ActiveArrangementId;
        NamedArrangement? active = null;
        if (!string.IsNullOrEmpty(activeId))
            active = arrangement.Arrangements.FirstOrDefault(a => a.Id == activeId);

        // Fall back to natural if the active arrangement isn't found.
        if (active == null)
            active = arrangement.Arrangements.FirstOrDefault(a => a.IsNatural);

        if (active != null && arrangement.Sections.Count > 0)
            return BuildFromNamedArrangement(project, active);

        return BuildNaturalSequence(project);
    }

    private static PlaybackSequence BuildFromNamedArrangement(PresentationProject project, NamedArrangement arrangement)
    {
        var instances = new List<PlaybackInstance>();
        var occurrenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var slideMap = project.Slides.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var groupMap = project.Arrangement.Sections
            .Where(g => !string.IsNullOrEmpty(g.Id))
            .ToDictionary(g => g.Id, g => g, StringComparer.OrdinalIgnoreCase);

        foreach (var groupRef in arrangement.Groups)
        {
            if (!groupMap.TryGetValue(groupRef.SectionGroupId, out var group))
                continue;

            occurrenceCounts.TryGetValue(group.Id, out var occIdx);

            foreach (var slideId in group.SlideIds)
            {
                if (!slideMap.TryGetValue(slideId, out var slide))
                    continue;

                instances.Add(new PlaybackInstance
                {
                    InstanceKey = $"{group.Id}_{occIdx}_{slideId}",
                    SectionGroupId = group.Id,
                    SlideId = slideId,
                    OccurrenceIndex = occIdx,
                    Slide = slide,
                });
            }

            occurrenceCounts[group.Id] = occIdx + 1;
        }

        return new PlaybackSequence(instances, arrangement.Id);
    }

    private static PlaybackSequence BuildNaturalSequence(PresentationProject project)
    {
        var instances = project.Slides
            .Select(slide => new PlaybackInstance
            {
                InstanceKey = slide.Id,
                SectionGroupId = slide.Section ?? string.Empty,
                SlideId = slide.Id,
                OccurrenceIndex = 0,
                Slide = slide,
            })
            .ToList();
        return new PlaybackSequence(instances, null);
    }

    private static string SectionGroupKey(SectionGroup g) =>
        $"{g.Section?.ToLowerInvariant()}|{g.Label?.ToLowerInvariant()}";

    public static string FormatSectionLabel(string section, int? index = null)
    {
        if (string.IsNullOrWhiteSpace(section))
            return "Slide";

        var baseLabel = section.ToLowerInvariant() switch
        {
            "title" => "Title",
            "verse" => "Verse",
            "chorus" => "Chorus",
            "bridge" => "Bridge",
            "pre-chorus" => "Pre-Chorus",
            "tag" => "Tag",
            "refrain" => "Refrain",
            "intro" => "Intro",
            "outro" => "Outro",
            "vamp" => "Vamp",
            "interlude" => "Interlude",
            "ending" => "Ending",
            "custom" => "Custom",
            _ => string.Join(
                ' ',
                section.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant())),
        };

        return index is > -1 ? $"{baseLabel} {index.Value + 1}" : baseLabel;
    }

    public static List<SlideLayer> GetThemeSlideLayers(ThemeTemplateSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);

        if (slide.Layers.Count > 0)
            return slide.Layers;

        if (slide.LegacyTextLayers?.Count > 0)
            return slide.LegacyTextLayers.Select(ConvertLegacyThemeTextLayer).Cast<SlideLayer>().ToList();

        return new List<SlideLayer>();
    }

    public static SlideTextBlock CreateTextBlock(
        string? text,
        string? role = null,
        string? name = null,
        string? sourceLayerId = null)
    {
        var now = DateTime.UtcNow.ToString("O");
        return new SlideTextBlock
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = NormalizeRole(role),
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Text = text ?? string.Empty,
            SourceLayerId = string.IsNullOrWhiteSpace(sourceLayerId) ? null : sourceLayerId.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "body";

        return role.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
    }

    public static string BuildSlideText(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        NormalizeTextBlocks(slide);
        var parts = slide.TextBlocks
            .Select(static block => block.Text?.Trim())
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
    }

    public static SlideTextBlock? ResolveTextBlock(PresentationSlide slide, ThemeTextBinding? binding, int fallbackIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(slide);
        NormalizeTextBlocks(slide);
        if (binding == null)
            return slide.TextBlocks.ElementAtOrDefault(fallbackIndex) ?? slide.TextBlocks.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(binding.TextBlockId))
        {
            var block = slide.TextBlocks.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, binding.TextBlockId, StringComparison.OrdinalIgnoreCase));
            if (block != null)
                return block;
        }

        if (!string.IsNullOrWhiteSpace(binding.Role))
        {
            var role = NormalizeRole(binding.Role);
            var block = slide.TextBlocks.FirstOrDefault(candidate =>
                string.Equals(NormalizeRole(candidate.Role), role, StringComparison.OrdinalIgnoreCase));
            if (block != null)
                return block;
        }

        var index = binding.FallbackIndex ?? fallbackIndex;
        return slide.TextBlocks.ElementAtOrDefault(index) ?? slide.TextBlocks.FirstOrDefault();
    }

    private static TextLayer ConvertLegacyThemeTextLayer(ThemeTemplateTextLayer layer)
    {
        return new TextLayer
        {
            Id = string.IsNullOrWhiteSpace(layer.Id) ? Guid.NewGuid().ToString("N") : layer.Id,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? "Text" : layer.Name,
            Locked = false,
            Visible = true,
            Transform = layer.Transform ?? CreateDefaultTransform(null),
            Content = layer.Content ?? layer.Name ?? string.Empty,
            TextBinding = new ThemeTextBinding
            {
                Role = NormalizeRole(layer.Name),
                PlaceholderText = layer.Content ?? layer.Name,
                FallbackIndex = 0,
            },
            Style = layer.Style ?? CreateDefaultTextStyle(),
            Fills = layer.Fills ?? new List<LayerFillModel>(),
            Strokes = layer.Strokes ?? new List<LayerStrokeModel>(),
            TextFit = layer.TextFit ?? "auto",
            Padding = layer.Padding ?? 2,
        };
    }

    private static PresentationThemeBinding? NormalizeThemeBinding(PresentationThemeBinding? binding)
    {
        if (binding == null)
            return null;

        binding.Mode = binding.Mode?.Trim().ToLowerInvariant() switch
        {
            ThemeBindingModes.Detached => ThemeBindingModes.Detached,
            ThemeBindingModes.Forked => ThemeBindingModes.Forked,
            ThemeBindingModes.Materialized => ThemeBindingModes.Materialized,
            _ => ThemeBindingModes.Linked,
        };
        binding.RoleMappings ??= new List<ThemeRoleMapping>();
        foreach (var mapping in binding.RoleMappings)
            mapping.SlideRole = NormalizeRole(mapping.SlideRole);
        return binding;
    }

    private static ThemeTextBinding? NormalizeTextBinding(ThemeTextBinding? binding)
    {
        if (binding == null)
            return null;

        binding.Role = string.IsNullOrWhiteSpace(binding.Role) ? null : NormalizeRole(binding.Role);
        if (binding.FallbackIndex < 0)
            binding.FallbackIndex = 0;
        return binding;
    }

    private static void NormalizeTextBlocks(PresentationSlide slide)
    {
        slide.TextBlocks ??= new List<SlideTextBlock>();
        if (slide.TextBlocks.Count == 0)
        {
            var textLayers = slide.Layers.OfType<TextLayer>().ToList();
            for (var index = 0; index < textLayers.Count; index++)
            {
                var layer = textLayers[index];
                slide.TextBlocks.Add(CreateTextBlock(
                    layer.Content,
                    ResolveTextBlockRole(slide, index),
                    string.IsNullOrWhiteSpace(layer.Name) ? $"Text {index + 1}" : layer.Name,
                    layer.Id));
            }
        }

        for (var index = 0; index < slide.TextBlocks.Count; index++)
        {
            var block = slide.TextBlocks[index];
            block.Id = string.IsNullOrWhiteSpace(block.Id) ? Guid.NewGuid().ToString("N") : block.Id.Trim();
            block.Role = NormalizeRole(FirstNonWhiteSpace(block.Role, ResolveTextBlockRole(slide, index)));
            block.Name = string.IsNullOrWhiteSpace(block.Name) ? FormatSectionLabel(block.Role) : block.Name.Trim();
            block.Text ??= string.Empty;
            block.UpdatedAt ??= slide.UpdatedAt;
        }

        var legacyLayers = slide.Layers.OfType<TextLayer>().ToList();
        for (var index = 0; index < legacyLayers.Count; index++)
        {
            var layer = legacyLayers[index];
            layer.TextBinding ??= new ThemeTextBinding
            {
                TextBlockId = slide.TextBlocks.FirstOrDefault(block =>
                    string.Equals(block.SourceLayerId, layer.Id, StringComparison.OrdinalIgnoreCase))?.Id,
                Role = ResolveTextBlockRole(slide, index),
                FallbackIndex = index,
                PlaceholderText = layer.Content,
            };
        }
    }

    private static string ResolveTextBlockRole(PresentationSlide slide, int index)
    {
        if (index == 0)
            return NormalizeRole(FirstNonWhiteSpace(slide.LayoutType, slide.Section, "body"));

        return $"body-{index + 1}";
    }

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static void NormalizeArrangement(PresentationProject project)
    {
        var slideIds = project.Slides.Select(slide => slide.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        project.Arrangement.Order = project.Arrangement.Order
            .Where(id => slideIds.Contains(id))
            .Concat(project.Slides.Select(slide => slide.Id).Where(id => !project.Arrangement.Order.Contains(id, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (project.Arrangement.Order.Count == 0)
            project.Arrangement.Order = project.Slides.Select(slide => slide.Id).ToList();

        project.Arrangement.Sections = project.Arrangement.Sections
            .Where(section => section.SlideIds.Any(slideIds.Contains))
            .ToList();

        if (project.Arrangement.Sections.Count == 0)
        {
            // No sections yet — perform a full reconcile to seed the natural arrangement.
            ReconcileArrangement(project);
        }
        else
        {
            // Ensure all existing groups have IDs.
            foreach (var g in project.Arrangement.Sections.Where(g => string.IsNullOrEmpty(g.Id)))
                g.Id = Guid.NewGuid().ToString("N");

            // Ensure the natural arrangement exists.
            if (!project.Arrangement.Arrangements.Any(a => a.IsNatural))
            {
                var naturalArr = new NamedArrangement
                {
                    Id = "natural",
                    Name = "Master",
                    IsNatural = true,
                    Groups = project.Arrangement.Sections
                        .Select(g => new ArrangementGroupRef { SectionGroupId = g.Id })
                        .ToList(),
                };
                project.Arrangement.Arrangements.Insert(0, naturalArr);
            }

            // Prune stale group refs from custom arrangements.
            var validIds = project.Arrangement.Sections.Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var arr in project.Arrangement.Arrangements.Where(a => !a.IsNatural))
            {
                arr.Groups = arr.Groups.Where(r => validIds.Contains(r.SectionGroupId)).ToList();
            }
        }
    }

    private sealed class SectionIndexComparer : IEqualityComparer<(string Section, int? Index)>
    {
        public bool Equals((string Section, int? Index) x, (string Section, int? Index) y) =>
            string.Equals(x.Section, y.Section, StringComparison.OrdinalIgnoreCase) && x.Index == y.Index;

        public int GetHashCode((string Section, int? Index) obj) =>
            HashCode.Combine(obj.Section?.ToLowerInvariant(), obj.Index);
    }

}