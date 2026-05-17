
namespace ChurchPresenter.Services.Documents;

/// <inheritdoc />
public sealed class PresentationTextWorkflowService(
    IPresentationProjectService projects,
    ICuePreparationService cuePreparation) : IPresentationTextWorkflowService
{
    private readonly IPresentationProjectService _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));

    /// <inheritdoc />
    public Task<PresentationTextDocument> OpenAsync(string presentationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);

        PresentationProject project = _projects.Open(presentationPath);
        return Task.FromResult(ProjectToDocument(project));
    }

    /// <inheritdoc />
    public Task<PresentationTextDocument> SaveSlideTextAsync(
        string presentationPath,
        string slideId,
        string text,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);

        PresentationProject project = _projects.Open(presentationPath);
        PresentationSlide slide = project.Slides.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, slideId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Slide '{slideId}' was not found.");

        SlideTextBlock block = EnsurePrimaryTextBlock(slide);
        block.Text = text ?? string.Empty;
        block.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        SyncLegacyPrimaryTextLayer(slide, block.Text);
        slide.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        slide.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        project.Manifest.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        _projects.Save(project, presentationPath);
        _cuePreparation.InvalidatePresentationCues(project.SourcePath);
        return Task.FromResult(ProjectToDocument(project));
    }

    /// <inheritdoc />
    public Task<PresentationTextDocument> ReflowAsync(
        string presentationPath,
        string reflowText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);

        PresentationProject project = _projects.Open(presentationPath);
        List<PresentationSlide> existingSlides = project.Slides.Select(PresentationModelUtilities.CloneSlide).ToList();
        Dictionary<string, SectionGroup> previousSectionsBySlideId = BuildPreviousSectionsBySlideId(project.Arrangement);
        List<string> blocks = SplitReflowText(reflowText);
        project.Slides = blocks.Select((block, index) => CreateReflowSlide(existingSlides, previousSectionsBySlideId, block, index)).ToList();
        RebuildArrangement(project);
        project.Manifest.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        _projects.Save(project, presentationPath);
        _cuePreparation.InvalidatePresentationCues(project.SourcePath);
        return Task.FromResult(ProjectToDocument(project));
    }

    private static PresentationTextDocument ProjectToDocument(PresentationProject project)
    {
        return new PresentationTextDocument
        {
            SourcePath = project.SourcePath,
            Title = string.IsNullOrWhiteSpace(project.Manifest.Title) ? "Untitled presentation" : project.Manifest.Title,
            ThemeId = project.Manifest.ThemeId ?? string.Empty,
            EmbeddedThemeCount = project.EmbeddedThemes.Count,
            ArrangementSummary = BuildArrangementSummary(project),
            CueSummary = BuildCueSummary(project.Slides),
            TemplateVariantSummary = BuildTemplateVariantSummary(project),
            Slides = project.Slides.Select((slide, index) => new PresentationTextSlide
            {
                SlideId = slide.Id,
                Ordinal = index + 1,
                Title = ResolveSlideTitle(slide, index),
                Section = FirstNonWhiteSpace(slide.SectionLabel, slide.Section),
                LayoutType = slide.LayoutType ?? string.Empty,
                Text = ExtractPrimaryText(slide),
                Notes = slide.Notes ?? string.Empty,
                LayerCount = slide.Layers.Count,
                ActionCount = slide.Actions.Count,
                MediaCueCount = slide.MediaCues.Count,
                TransitionSummary = FormatTransition(slide.Animations?.Transition),
            }).ToArray(),
        };
    }

    private static string ResolveSlideTitle(PresentationSlide slide, int index)
    {
        string label = FirstNonWhiteSpace(slide.SectionLabel, slide.Section);
        return string.IsNullOrWhiteSpace(label) ? $"Slide {index + 1}" : $"{index + 1}. {label}";
    }

    private static string ExtractPrimaryText(PresentationSlide slide)
    {
        PresentationModelUtilities.NormalizeSlide(slide, null);
        return PresentationModelUtilities.BuildSlideText(slide);
    }

    private static SlideTextBlock EnsurePrimaryTextBlock(PresentationSlide slide)
    {
        PresentationModelUtilities.NormalizeSlide(slide, null);
        SlideTextBlock? block = slide.TextBlocks.FirstOrDefault();
        if (block != null)
            return block;

        block = PresentationModelUtilities.CreateTextBlock(string.Empty, "body", "Text");
        slide.TextBlocks.Add(block);
        return block;
    }

    private static void SyncLegacyPrimaryTextLayer(PresentationSlide slide, string text)
    {
        TextLayer? layer = slide.Layers.OfType<TextLayer>().FirstOrDefault();
        if (layer == null)
        {
            layer = CreateTextLayer("text-1", text);
            slide.Layers.Insert(0, layer);
        }

        layer.Content = text;
    }

    private static PresentationSlide CreateReflowSlide(
        IReadOnlyList<PresentationSlide> existingSlides,
        IReadOnlyDictionary<string, SectionGroup> previousSectionsBySlideId,
        string text,
        int index)
    {
        PresentationSlide? existing = existingSlides.ElementAtOrDefault(index);
        string slideId = !string.IsNullOrWhiteSpace(existing?.Id)
            ? existing.Id
            : $"slide-{index + 1}";

        PresentationSlide slide = existing == null
            ? new PresentationSlide
            {
                Id = slideId,
                Type = "song",
                Section = $"slide-{index + 1}",
                SectionLabel = $"Slide {index + 1}",
                TextBlocks = [PresentationModelUtilities.CreateTextBlock(text, "body", "Text", "text-1")],
                Layers = [CreateTextLayer("text-1", text)],
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            }
            : PresentationModelUtilities.CloneSlide(existing);

        slide.Id = slideId;
        if (previousSectionsBySlideId.TryGetValue(slideId, out SectionGroup? previousSection))
        {
            slide.Section ??= previousSection.Section;
            slide.SectionLabel ??= previousSection.Label;
        }

        slide.Section ??= $"slide-{index + 1}";
        slide.SectionLabel ??= $"Slide {index + 1}";
        SlideTextBlock block = EnsurePrimaryTextBlock(slide);
        block.Text = text;
        block.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        SyncLegacyPrimaryTextLayer(slide, text);
        slide.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        return slide;
    }

    private static Dictionary<string, SectionGroup> BuildPreviousSectionsBySlideId(PresentationArrangement arrangement)
    {
        return arrangement.Sections
            .SelectMany(static section => section.SlideIds.Select(slideId => new { SlideId = slideId, Section = section }))
            .Where(static item => !string.IsNullOrWhiteSpace(item.SlideId))
            .GroupBy(static item => item.SlideId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Section, StringComparer.OrdinalIgnoreCase);
    }

    private static void RebuildArrangement(PresentationProject project)
    {
        project.Arrangement ??= new PresentationArrangement();

        Dictionary<string, string> previousIdsByKey = project.Arrangement.Sections
            .Where(static section => !string.IsNullOrWhiteSpace(section.Id))
            .GroupBy(BuildSectionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        project.Arrangement.Order = project.Slides.Select(static slide => slide.Id).ToList();
        project.Arrangement.Sections = project.Slides
            .Select((slide, index) => new
            {
                Slide = slide,
                Index = index,
                Key = BuildSlideSectionKey(slide, index),
            })
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PresentationSlide first = group.First().Slide;
                string label = FirstNonWhiteSpace(first.SectionLabel, first.Section, $"Slide {group.First().Index + 1}");
                string id = previousIdsByKey.TryGetValue(group.Key, out string? previousId)
                    ? previousId
                    : CreateSectionId(group.Key, group.First().Index);

                return new SectionGroup
                {
                    Id = id,
                    Section = FirstNonWhiteSpace(first.Section, label),
                    Label = label,
                    SlideIds = group.Select(static item => item.Slide.Id).ToList(),
                };
            })
            .ToList();

        HashSet<string> validSectionIds = project.Arrangement.Sections
            .Select(static section => section.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (NamedArrangement arrangement in project.Arrangement.Arrangements)
        {
            arrangement.Groups = arrangement.Groups
                .Where(group => validSectionIds.Contains(group.SectionGroupId))
                .ToList();
        }
    }

    private static TextLayer CreateTextLayer(string id, string text) => new()
    {
        Id = id,
        Name = "Text",
        Content = text,
            TextBinding = new ThemeTextBinding { Role = "body", FallbackIndex = 0, PlaceholderText = text },
        Style = PresentationModelUtilities.CreateDefaultTextStyle(),
        Transform = new LayerTransformModel
        {
            X = 120,
            Y = 220,
            Width = 1680,
            Height = 640,
            Opacity = 1,
        },
    };

    private static string BuildArrangementSummary(PresentationProject project)
    {
        int groupCount = project.Arrangement.Sections.Count;
        int namedCount = project.Arrangement.Arrangements.Count;
        string active = string.IsNullOrWhiteSpace(project.Arrangement.ActiveArrangementId)
            ? "natural order"
            : project.Arrangement.ActiveArrangementId;
        return $"{groupCount} group(s), {namedCount} named arrangement(s), active: {active}.";
    }

    private static string BuildCueSummary(IReadOnlyList<PresentationSlide> slides)
    {
        int actionCount = slides.Sum(static slide => slide.Actions.Count);
        int cueCount = slides.Sum(static slide => slide.MediaCues.Count);
        int notesCount = slides.Count(static slide => !string.IsNullOrWhiteSpace(slide.Notes));
        return $"{actionCount} slide action(s), {cueCount} media cue(s), {notesCount} slide(s) with notes.";
    }

    private static string BuildTemplateVariantSummary(PresentationProject project)
    {
        int layoutCount = project.Slides
            .Select(static slide => slide.LayoutType)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return $"{layoutCount} slide layout variant(s), {project.EmbeddedThemes.Count} embedded theme template(s).";
    }

    private static string FormatTransition(SlideTransition? transition)
    {
        if (transition == null)
            return "Default transition";

        return $"{transition.Type} ({transition.Duration} ms)";
    }

    private static string BuildSectionKey(SectionGroup section) =>
        FirstNonWhiteSpace(section.Section, section.Label, section.Id).Trim();

    private static string BuildSlideSectionKey(PresentationSlide slide, int index) =>
        FirstNonWhiteSpace(slide.Section, slide.SectionLabel, $"slide-{index + 1}").Trim();

    private static string CreateSectionId(string key, int index)
    {
        string normalized = new(
            key.ToLowerInvariant()
                .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray());
        normalized = string.Join("-", normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? $"section-{index + 1}" : normalized;
    }

    private static List<string> SplitReflowText(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static block => block.Replace("\n", Environment.NewLine, StringComparison.Ordinal))
            .DefaultIfEmpty(string.Empty)
            .ToList();
    }

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}