
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Documents;

public sealed class QuickEditTextLayerServiceTests
{
    [Fact]
    public async Task BuildDraftAsync_uses_resolved_theme_text_layer_when_slide_has_no_text()
    {
        ThemeTemplate theme = CreateTheme();
        PresentationProject project = CreateProject(theme);
        PresentationSlide slide = project.Slides.Single();
        QuickEditTextLayerService service = CreateService();

        QuickEditTextLayerDraft draft = await service.BuildDraftAsync(project, slide);

        draft.CreatedDraftLayer.Should().BeTrue();
        TextLayer layer = draft.TextLayers.Should().ContainSingle().Subject;
        layer.Id.Should().NotBe("theme-text");
        layer.Name.Should().Be("Theme Body");
        layer.Transform.X.Should().Be(100);
        layer.Transform.Y.Should().Be(200);
        layer.Style!.Font.Family.Should().Be("Aptos");
        layer.Content.Should().BeEmpty();
        layer.TextBinding!.PlaceholderText.Should().Be("Theme placeholder");
    }

    [Fact]
    public async Task ApplyEdits_adds_draft_layer_and_text_block()
    {
        ThemeTemplate theme = CreateTheme();
        PresentationProject project = CreateProject(theme);
        PresentationSlide slide = project.Slides.Single();
        QuickEditTextLayerService service = CreateService();
        QuickEditTextLayerDraft draft = await service.BuildDraftAsync(project, slide);
        TextLayer draftLayer = draft.TextLayers.Single();

        service.ApplyEdits(project, slide, draft, new Dictionary<string, string>
        {
            [draftLayer.Id] = "New lyrics",
        });

        TextLayer layer = slide.Layers.OfType<TextLayer>().Should().ContainSingle().Subject;
        layer.Content.Should().Be("New lyrics");
        layer.TextBinding!.TextBlockId.Should().NotBeNullOrWhiteSpace();
        SlideTextBlock block = slide.TextBlocks.Should().ContainSingle().Subject;
        block.Text.Should().Be("New lyrics");
        block.SourceLayerId.Should().Be(layer.Id);
        block.Id.Should().Be(layer.TextBinding.TextBlockId);
    }

    [Fact]
    public async Task ApplyEdits_updates_existing_layer_and_text_block()
    {
        PresentationProject project = new()
        {
            Manifest = new PresentationManifest { AspectRatio = "16:9" },
            Slides =
            {
                PresentationModelUtilities.CreateSlide("content", "Original"),
            },
        };
        PresentationSlide slide = project.Slides.Single();
        QuickEditTextLayerService service = CreateService();
        QuickEditTextLayerDraft draft = await service.BuildDraftAsync(project, slide);
        TextLayer layer = draft.TextLayers.Single();

        service.ApplyEdits(project, slide, draft, new Dictionary<string, string>
        {
            [layer.Id] = "Updated",
        });

        TextLayer updatedLayer = slide.Layers.OfType<TextLayer>().Single();
        updatedLayer.Content.Should().Be("Updated");
        PresentationModelUtilities.ResolveTextBlock(slide, updatedLayer.TextBinding)!.Text.Should().Be("Updated");
    }

    private static QuickEditTextLayerService CreateService() =>
        new(new ThemeResolutionService(), new FakeThemeLibraryService(), new ThemeApplicationService());

    private static PresentationProject CreateProject(ThemeTemplate theme) =>
        new()
        {
            Manifest = new PresentationManifest
            {
                AspectRatio = "16:9",
                SlideSize = new SlideSizeDto { Width = 1920, Height = 1080 },
                ThemeBinding = new PresentationThemeBinding
                {
                    ThemeId = theme.Id,
                    EmbeddedSnapshotId = theme.Id,
                    ThemeSlideId = theme.Slides.Single().Id,
                },
            },
            EmbeddedThemes =
            {
                new BundleThemeEntry { FileName = "theme.json", Template = theme },
            },
            Slides =
            {
                new PresentationSlide
                {
                    Id = "slide-1",
                    Type = "content",
                    LayoutType = "body",
                    Background = new TransparentSlideBackground(),
                },
            },
        };

    private static ThemeTemplate CreateTheme() =>
        new()
        {
            Id = "theme-1",
            Name = "Theme",
            AspectRatio = "16:9",
            BaseSize = new SlideSizeDto { Width = 1920, Height = 1080 },
            Slides =
            {
                new ThemeTemplateSlide
                {
                    Id = "theme-slide",
                    Name = "Body",
                    LayoutType = "body",
                    Roles = { "body" },
                    Layers =
                    {
                        new TextLayer
                        {
                            Id = "theme-text",
                            Name = "Theme Body",
                            Content = "Theme placeholder",
                            TextBinding = new ThemeTextBinding { Role = "body", FallbackIndex = 0, PlaceholderText = "Theme placeholder" },
                            Transform = new LayerTransformModel { X = 100, Y = 200, Width = 1600, Height = 500, Opacity = 1 },
                            Style = new TextStyleModel
                            {
                                Font = new TextFontModel { Family = "Aptos", Size = 84, Weight = 700, LineHeight = 1.1 },
                                Color = "#FFFFFF",
                                Alignment = "center",
                                VerticalAlignment = "middle",
                                Shadow = new TextShadowModel(),
                                Outline = new TextOutlineModel(),
                                EffectsOrder = new List<string>(),
                            },
                            Fills = new List<LayerFillModel> { new() { Id = "fill", Color = "#FFFFFF", Enabled = true, Opacity = 1 } },
                            TextFit = "auto",
                            Padding = 8,
                        },
                    },
                },
            },
        };

    private sealed class FakeThemeLibraryService : IThemeLibraryService
    {
        public Task<IReadOnlyList<ThemeTemplate>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ThemeTemplate>>(Array.Empty<ThemeTemplate>());

        public Task<ThemeTemplate?> LoadThemeAsync(string themeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ThemeTemplate?>(null);

        public Task SaveAsync(IReadOnlyCollection<ThemeTemplate> themes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveThemeAsync(ThemeTemplate theme, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
