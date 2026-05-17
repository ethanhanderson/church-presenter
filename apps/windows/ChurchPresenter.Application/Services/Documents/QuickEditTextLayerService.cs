
namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Builds and applies Quick Edit text-layer drafts for slides that may not already contain editable text.
/// </summary>
public interface IQuickEditTextLayerService
{
    /// <summary>Builds the text layers Quick Edit should expose for the supplied slide.</summary>
    Task<QuickEditTextLayerDraft> BuildDraftAsync(
        PresentationProject project,
        PresentationSlide slide,
        CancellationToken cancellationToken = default);

    /// <summary>Applies Quick Edit text field values to the supplied slide in place.</summary>
    void ApplyEdits(
        PresentationProject project,
        PresentationSlide slide,
        QuickEditTextLayerDraft draft,
        IReadOnlyDictionary<string, string> textByLayerId);
}

/// <summary>Editable text-layer set used by the Show page Quick Edit flyout.</summary>
public sealed class QuickEditTextLayerDraft
{
    public IReadOnlyList<TextLayer> TextLayers { get; init; } = Array.Empty<TextLayer>();

    public bool CreatedDraftLayer { get; init; }
}

/// <inheritdoc />
public sealed class QuickEditTextLayerService(
    IThemeResolutionService themeResolution,
    IThemeLibraryService themeLibrary,
    IThemeApplicationService themeApplication) : IQuickEditTextLayerService
{
    private readonly IThemeResolutionService _themeResolution = themeResolution ?? throw new ArgumentNullException(nameof(themeResolution));
    private readonly IThemeLibraryService _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
    private readonly IThemeApplicationService _themeApplication = themeApplication ?? throw new ArgumentNullException(nameof(themeApplication));

    /// <inheritdoc />
    public async Task<QuickEditTextLayerDraft> BuildDraftAsync(
        PresentationProject project,
        PresentationSlide slide,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(slide);

        var existingTextLayers = slide.Layers.OfType<TextLayer>().ToList();
        if (existingTextLayers.Count > 0)
        {
            return new QuickEditTextLayerDraft
            {
                TextLayers = existingTextLayers
                    .Select(layer => PresentationModelUtilities.DeepClone(layer) ?? layer)
                    .ToList(),
            };
        }

        var draftLayer = await CreateDraftLayerFromThemeAsync(project, slide, cancellationToken)
                         ?? PresentationModelUtilities.CreateTextLayer(
                             string.Empty,
                             name: "Text",
                             slideSize: ResolveSlideSize(project));

        draftLayer.Id = Guid.NewGuid().ToString("N");
        draftLayer.Content = string.Empty;
        draftLayer.TextBinding ??= new ThemeTextBinding { Role = ResolveSlideRole(slide), FallbackIndex = 0 };
        draftLayer.TextBinding.TextBlockId = null;
        draftLayer.TextBinding.Role = string.IsNullOrWhiteSpace(draftLayer.TextBinding.Role)
            ? ResolveSlideRole(slide)
            : PresentationModelUtilities.NormalizeRole(draftLayer.TextBinding.Role);
        draftLayer.TextBinding.FallbackIndex ??= 0;
        draftLayer.TextBinding.PlaceholderText ??= FirstNonWhiteSpace(draftLayer.Content, draftLayer.Name, "Text");
        draftLayer.Name = string.IsNullOrWhiteSpace(draftLayer.Name) ? "Text" : draftLayer.Name.Trim();
        PresentationModelUtilities.NormalizeLayer(draftLayer, ResolveSlideSize(project));

        return new QuickEditTextLayerDraft
        {
            CreatedDraftLayer = true,
            TextLayers = [draftLayer],
        };
    }

    /// <inheritdoc />
    public void ApplyEdits(
        PresentationProject project,
        PresentationSlide slide,
        QuickEditTextLayerDraft draft,
        IReadOnlyDictionary<string, string> textByLayerId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(textByLayerId);

        var slideSize = ResolveSlideSize(project);
        PresentationModelUtilities.NormalizeSlide(slide, slideSize);

        foreach (var draftLayer in draft.TextLayers)
        {
            if (!textByLayerId.TryGetValue(draftLayer.Id, out var text))
                continue;

            var existingLayer = slide.Layers.OfType<TextLayer>().FirstOrDefault(layer =>
                string.Equals(layer.Id, draftLayer.Id, StringComparison.OrdinalIgnoreCase));
            if (existingLayer == null)
            {
                existingLayer = PresentationModelUtilities.DeepClone(draftLayer) ?? new TextLayer();
                existingLayer.Id = string.IsNullOrWhiteSpace(existingLayer.Id) ? Guid.NewGuid().ToString("N") : existingLayer.Id;
                slide.Layers.Add(existingLayer);
            }

            existingLayer.Content = text;
            existingLayer.TextBinding ??= new ThemeTextBinding { Role = ResolveSlideRole(slide), FallbackIndex = 0 };
            var textBlock = ResolveOrCreateTextBlock(slide, existingLayer, text);
            textBlock.Text = text;
            textBlock.UpdatedAt = DateTime.UtcNow.ToString("O");
            existingLayer.TextBinding.TextBlockId = textBlock.Id;
            existingLayer.TextBinding.Role = textBlock.Role;
            existingLayer.TextBinding.PlaceholderText ??= draftLayer.TextBinding?.PlaceholderText ?? draftLayer.Content;
        }

        slide.UpdatedAt = DateTime.UtcNow.ToString("O");
        PresentationModelUtilities.NormalizeSlide(slide, slideSize);
    }

    private async Task<TextLayer?> CreateDraftLayerFromThemeAsync(
        PresentationProject project,
        PresentationSlide slide,
        CancellationToken cancellationToken)
    {
        var resolved = _themeResolution.ResolveThemeSlide(project, slide);
        var theme = resolved.Theme;
        var themeSlide = resolved.ThemeSlide;

        if (themeSlide == null)
        {
            theme = await ResolveGlobalThemeAsync(project, slide, cancellationToken);
            themeSlide = theme == null ? null : ResolveThemeSlide(theme, project, slide);
        }

        if (theme == null || themeSlide == null)
            return null;

        PresentationModelUtilities.NormalizeTheme(theme);
        var templateTextLayer = PresentationModelUtilities.GetThemeSlideLayers(themeSlide).OfType<TextLayer>().FirstOrDefault();
        if (templateTextLayer == null)
            return null;

        var previewSlide = new PresentationSlide
        {
            Id = "quick-edit-draft",
            Type = slide.Type,
            LayoutType = slide.LayoutType,
            Section = slide.Section,
            SectionLabel = slide.SectionLabel,
            SectionIndex = slide.SectionIndex,
            Background = PresentationModelUtilities.DeepClone(slide.Background) ?? new TransparentSlideBackground(),
        };
        _themeApplication.ApplyThemeSlideToSlide(
            previewSlide,
            themeSlide,
            new ThemeApplyOptions
            {
                ScaleMode = "fit",
                SourceSize = theme.BaseSize,
                TargetSize = ResolveSlideSize(project),
            });

        return previewSlide.Layers.OfType<TextLayer>().FirstOrDefault(layer =>
                   string.Equals(layer.Id, templateTextLayer.Id, StringComparison.OrdinalIgnoreCase))
               ?? previewSlide.Layers.OfType<TextLayer>().FirstOrDefault();
    }

    private async Task<ThemeTemplate?> ResolveGlobalThemeAsync(
        PresentationProject project,
        PresentationSlide slide,
        CancellationToken cancellationToken)
    {
        var themeId = FirstNonWhiteSpace(
            slide.ThemeBinding?.ThemeId,
            project.Manifest.ThemeBinding?.ThemeId,
            project.Manifest.ThemeId);
        return string.IsNullOrWhiteSpace(themeId)
            ? null
            : await _themeLibrary.LoadThemeAsync(themeId, cancellationToken);
    }

    private static ThemeTemplateSlide? ResolveThemeSlide(ThemeTemplate theme, PresentationProject project, PresentationSlide slide)
    {
        var binding = slide.ThemeBinding ?? project.Manifest.ThemeBinding;
        if (!string.IsNullOrWhiteSpace(binding?.ThemeSlideId))
        {
            var byId = theme.Slides.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, binding.ThemeSlideId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        var role = ResolveSlideRole(slide);
        return theme.Slides.FirstOrDefault(candidate =>
                   string.Equals(PresentationModelUtilities.NormalizeRole(candidate.LayoutType), role, StringComparison.OrdinalIgnoreCase)
                   || candidate.Roles.Any(candidateRole =>
                       string.Equals(PresentationModelUtilities.NormalizeRole(candidateRole), role, StringComparison.OrdinalIgnoreCase))
                   || candidate.RoleAliases.Any(alias =>
                       string.Equals(PresentationModelUtilities.NormalizeRole(alias), role, StringComparison.OrdinalIgnoreCase)))
               ?? theme.Slides.FirstOrDefault();
    }

    private static SlideTextBlock ResolveOrCreateTextBlock(PresentationSlide slide, TextLayer layer, string text)
    {
        var block = PresentationModelUtilities.ResolveTextBlock(slide, layer.TextBinding, layer.TextBinding?.FallbackIndex ?? 0);
        if (block != null)
            return block;

        block = PresentationModelUtilities.CreateTextBlock(
            text,
            layer.TextBinding?.Role ?? ResolveSlideRole(slide),
            string.IsNullOrWhiteSpace(layer.Name) ? "Text" : layer.Name,
            layer.Id);
        slide.TextBlocks.Add(block);
        return block;
    }

    private static SlideSizeDto ResolveSlideSize(PresentationProject project) =>
        PresentationModelUtilities.GetBaseSlideSize(project.Manifest.AspectRatio, project.Manifest.SlideSize);

    private static string ResolveSlideRole(PresentationSlide slide) =>
        PresentationModelUtilities.NormalizeRole(FirstNonWhiteSpace(slide.LayoutType, slide.Section, slide.SectionLabel, "body"));

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
