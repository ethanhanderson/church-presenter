using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Documents;

public sealed class PresentationProjectPersistenceTests
{
    [Fact]
    public async Task PresentationProjectService_save_and_open_round_trips_typed_slides_and_embedded_themes()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var service = new PresentationProjectService(
            paths.Object,
            cpres,
            NullLogger<PresentationProjectService>.Instance);

        var textLayer = new TextLayer
        {
            Id = "text-1",
            Name = "Lyrics",
            Content = "Hello church",
            Transform = new LayerTransformModel
            {
                X = 120,
                Y = 240,
                Width = 1680,
                Height = 320,
                Rotation = 0,
                Opacity = 1,
            },
            Style = PresentationModelUtilities.CreateDefaultTextStyle(),
            Fills =
            [
                new LayerFillModel
                {
                    Id = "fill-1",
                    Color = "#FFFFFF",
                    Opacity = 1,
                    Enabled = true,
                },
            ],
        };

        var shapeLayer = new ShapeLayer
        {
            Id = "shape-1",
            Name = "Accent",
            ShapeType = "rectangle",
            Transform = new LayerTransformModel
            {
                X = 80,
                Y = 880,
                Width = 1760,
                Height = 96,
                Rotation = 0,
                Opacity = 0.9,
            },
            Style = new ShapeStyleModel
            {
                Fill = "#112233",
                FillOpacity = 0.8,
                Stroke = "#445566",
                StrokeOpacity = 0.9,
                StrokeWidth = 4,
                CornerRadius = 12,
            },
            Fills =
            [
                new LayerFillModel
                {
                    Id = "shape-fill-1",
                    Color = "#112233",
                    Opacity = 0.8,
                    Enabled = true,
                },
            ],
            Strokes =
            [
                new LayerStrokeModel
                {
                    Id = "shape-stroke-1",
                    Color = "#445566",
                    Opacity = 0.9,
                    Width = 4,
                    Position = "inside",
                    Sides = "all",
                    Enabled = true,
                },
            ],
        };

        var themeLayer = new TextLayer
        {
            Id = "theme-text-1",
            Name = "Theme Text",
            Content = "Theme Preview",
            Transform = new LayerTransformModel
            {
                X = 100,
                Y = 120,
                Width = 1720,
                Height = 220,
                Rotation = 0,
                Opacity = 1,
            },
            Style = PresentationModelUtilities.CreateDefaultTextStyle(),
        };

        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                PresentationId = "fixture-presentation",
                Title = "Fixture Presentation",
                CreatedAt = "2026-01-01T00:00:00.000Z",
                UpdatedAt = "2026-01-01T00:00:00.000Z",
                AspectRatio = "16:9",
                OutputScaleMode = "fill",
                SlideSize = new SlideSizeDto { Width = 1920, Height = 1080 },
            },
            Slides =
            [
                new PresentationSlide
                {
                    Id = "slide-1",
                    Type = "song",
                    Section = "verse",
                    SectionLabel = "Verse 1",
                    Disabled = true,
                    HotKey = "F2",
                    GoToNextTimerId = "timer-1",
                    Background = new VideoSlideBackground
                    {
                        MediaId = "video-1",
                        Fit = "cover",
                        Loop = true,
                        Muted = true,
                        Opacity = 0.75,
                    },
                    Layers = [textLayer, shapeLayer],
                    MediaCues =
                    [
                        new SlideMediaCue
                        {
                            Id = "cue-1",
                            MediaId = "overlay-1",
                            MediaType = "video",
                            DisplayName = "Walk In",
                            Target = "mediaOverlay",
                            Fit = "contain",
                            Loop = true,
                            Muted = false,
                            Autoplay = true,
                        },
                    ],
                    Actions =
                    [
                        new SlideActionDefinition
                        {
                            Id = "action-1",
                            Type = "clearMedia",
                            Label = "Clear Media",
                        },
                    ],
                },
            ],
            Arrangement = new PresentationArrangement
            {
                Order = ["slide-1"],
            },
            EmbeddedThemes =
            [
                new BundleThemeEntry
                {
                    FileName = "themes/main-theme.json",
                    Template = new ThemeTemplate
                    {
                        Id = "theme-1",
                        Name = "Main Theme",
                        CreatedAt = "2026-01-01T00:00:00.000Z",
                        UpdatedAt = "2026-01-01T00:00:00.000Z",
                        AspectRatio = "16:9",
                        BaseSize = new SlideSizeDto { Width = 1920, Height = 1080 },
                        Slides =
                        [
                            new ThemeTemplateSlide
                            {
                                Id = "theme-slide-1",
                                Name = "Verse Theme",
                                LayoutType = "song",
                                Background = new SolidSlideBackground { Color = "#000000" },
                                Layers = [themeLayer],
                                MediaCues = [],
                            },
                        ],
                    },
                },
            ],
        };

        service.Save(project, Path.Combine("presentations", "fixture.cpres"));

        var reopened = service.Open(Path.Combine("presentations", "fixture.cpres"));

        reopened.SourcePath.Should().Be(Path.Combine(root, "presentations", "fixture.cpres"));
        reopened.Manifest.Title.Should().Be("Fixture Presentation");
        reopened.Manifest.OutputScaleMode.Should().Be("fill");
        reopened.Slides.Should().ContainSingle();
        reopened.Slides[0].SectionLabel.Should().Be("Verse 1");
        reopened.Slides[0].Disabled.Should().BeTrue();
        reopened.Slides[0].HotKey.Should().Be("F2");
        reopened.Slides[0].GoToNextTimerId.Should().Be("timer-1");
        reopened.Slides[0].Actions.Should().ContainSingle();
        reopened.Slides[0].Actions[0].Type.Should().Be("clearMedia");
        reopened.Slides[0].Background.Should().BeOfType<VideoSlideBackground>();
        ((VideoSlideBackground)reopened.Slides[0].Background!).MediaId.Should().Be("video-1");
        reopened.Slides[0].MediaCues.Should().ContainSingle();
        reopened.Slides[0].MediaCues[0].DisplayName.Should().Be("Walk In");
        reopened.Slides[0].MediaCues[0].Target.Should().Be("mediaOverlay");
        reopened.Slides[0].Layers.Should().HaveCount(2);
        reopened.Slides[0].Layers.OfType<TextLayer>().Single().Content.Should().Be("Hello church");
        reopened.Slides[0].TextBlocks.Should().ContainSingle();
        reopened.Slides[0].TextBlocks[0].Text.Should().Be("Hello church");
        reopened.Slides[0].TextBlocks[0].SourceLayerId.Should().Be("text-1");
        reopened.Slides[0].Layers.OfType<ShapeLayer>().Single().Style.Fill.Should().Be("#112233");
        reopened.Arrangement.Order.Should().ContainInOrder("slide-1");
        reopened.EmbeddedThemes.Should().ContainSingle();
        var embeddedTemplate = reopened.EmbeddedThemes[0].Template;
        embeddedTemplate.Should().NotBeNull();
        embeddedTemplate!.Name.Should().Be("Main Theme");
        embeddedTemplate.Slides.Should().ContainSingle();
        embeddedTemplate.Slides[0].Layers.OfType<TextLayer>().Single().Content.Should().Be("Theme Preview");
    }

    [Fact]
    public async Task PresentationProjectService_open_migrates_legacy_text_layers_to_raw_text_blocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var service = new PresentationProjectService(
            paths.Object,
            cpres,
            NullLogger<PresentationProjectService>.Instance);

        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                PresentationId = "legacy-text",
                Title = "Legacy Text",
                AspectRatio = "16:9",
            },
            Slides =
            [
                new PresentationSlide
                {
                    Id = "slide-1",
                    Type = "song",
                    Section = "verse",
                    Layers =
                    [
                        new TextLayer
                        {
                            Id = "legacy-text-1",
                            Name = "Lyrics",
                            Content = "Raw migrated text",
                            Style = PresentationModelUtilities.CreateDefaultTextStyle(),
                        },
                    ],
                },
            ],
            Arrangement = new PresentationArrangement { Order = ["slide-1"] },
        };

        service.Save(project, Path.Combine("presentations", "legacy-text.cpres"));
        var reopened = service.Open(Path.Combine("presentations", "legacy-text.cpres"));

        reopened.Slides[0].TextBlocks.Should().ContainSingle();
        reopened.Slides[0].TextBlocks[0].Role.Should().Be("verse");
        reopened.Slides[0].TextBlocks[0].Text.Should().Be("Raw migrated text");
        reopened.Slides[0].Layers.OfType<TextLayer>().Single().TextBinding.Should().NotBeNull();
        reopened.Slides[0].Layers.OfType<TextLayer>().Single().TextBinding!.TextBlockId.Should().Be(reopened.Slides[0].TextBlocks[0].Id);
    }

    [Fact]
    public async Task ThemeLibraryService_save_and_load_round_trips_templates()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var service = new ThemeLibraryService(
            paths.Object,
            NullLogger<ThemeLibraryService>.Instance);

        var themes = new[]
        {
            new ThemeTemplate
            {
                Id = "theme-1",
                Name = "Song Theme",
                Version = "theme-version-1",
                Description = "Theme for song slides",
                Tags = ["song", "lyrics"],
                CreatedAt = "2026-01-01T00:00:00.000Z",
                UpdatedAt = "2026-01-01T00:00:00.000Z",
                AspectRatio = "16:9",
                BaseSize = new SlideSizeDto { Width = 1920, Height = 1080 },
                SupportedAspectRatios = ["16:9"],
                RoleAliases =
                [
                    new ThemeRoleAlias
                    {
                        Role = "verse",
                        Aliases = ["v", "lyrics"],
                    },
                ],
                Slides =
                [
                    new ThemeTemplateSlide
                    {
                        Id = "theme-slide-1",
                        Name = "Verse",
                        LayoutType = "song",
                        Roles = ["verse"],
                        Background = new SolidSlideBackground { Color = "#050505" },
                        Layers =
                        [
                            new TextLayer
                            {
                                Id = "theme-text-1",
                                Name = "Lyrics",
                                Content = "Amazing grace",
                                TextBinding = new ThemeTextBinding { Role = "verse", FallbackIndex = 0 },
                                Transform = new LayerTransformModel
                                {
                                    X = 120,
                                    Y = 200,
                                    Width = 1680,
                                    Height = 280,
                                    Rotation = 0,
                                    Opacity = 1,
                                },
                                Style = PresentationModelUtilities.CreateDefaultTextStyle(),
                            },
                        ],
                        MediaCues = [],
                    },
                ],
            },
        };

        await service.SaveAsync(themes);

        var reopened = await service.LoadAsync();

        reopened.Should().ContainSingle();
        reopened[0].Name.Should().Be("Song Theme");
        reopened[0].Version.Should().Be("theme-version-1");
        reopened[0].Tags.Should().Contain("lyrics");
        reopened[0].RoleAliases.Should().ContainSingle(alias => alias.Role == "verse");
        reopened[0].Slides.Should().ContainSingle();
        reopened[0].Slides[0].Roles.Should().Contain("verse");
        reopened[0].Slides[0].Background.Should().BeOfType<SolidSlideBackground>();
        ((SolidSlideBackground)reopened[0].Slides[0].Background).Color.Should().Be("#050505");
        reopened[0].Slides[0].Layers.OfType<TextLayer>().Single().Content.Should().Be("Amazing grace");
        reopened[0].Slides[0].Layers.OfType<TextLayer>().Single().TextBinding!.Role.Should().Be("verse");
        File.Exists(paths.Object.GetThemesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetThemeFilePath("theme-1")).Should().BeTrue();

        var reopenedSingle = await service.LoadThemeAsync("theme-1");
        reopenedSingle!.Name.Should().Be("Song Theme");
    }

    [Fact]
    public void ThemeResolutionService_resolves_linked_slide_by_role_mapping()
    {
        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                PresentationId = "theme-resolution",
                ThemeBinding = new PresentationThemeBinding
                {
                    ThemeId = "theme-1",
                    ThemeVersion = "1",
                    RoleMappings =
                    [
                        new ThemeRoleMapping
                        {
                            SlideRole = "verse",
                            ThemeSlideId = "theme-verse",
                        },
                    ],
                },
            },
            Slides =
            [
                new PresentationSlide
                {
                    Id = "slide-1",
                    Section = "verse",
                    TextBlocks = [PresentationModelUtilities.CreateTextBlock("Verse text", "verse")],
                },
            ],
            EmbeddedThemes =
            [
                new BundleThemeEntry
                {
                    FileName = "themes/theme-1.json",
                    Template = new ThemeTemplate
                    {
                        Id = "theme-1",
                        Version = "1",
                        Slides =
                        [
                            new ThemeTemplateSlide
                            {
                                Id = "theme-verse",
                                Name = "Verse",
                                LayoutType = "verse",
                                Layers =
                                [
                                    new TextLayer
                                    {
                                        Id = "theme-text",
                                        Content = "Placeholder",
                                        TextBinding = new ThemeTextBinding { Role = "verse" },
                                    },
                                ],
                            },
                        ],
                    },
                },
            ],
        };
        PresentationModelUtilities.NormalizeProject(project);

        var result = new ThemeResolutionService().ResolveThemeSlide(project, project.Slides[0]);

        result.IsResolved.Should().BeTrue();
        result.ThemeSlide!.Id.Should().Be("theme-verse");
    }

    [Fact]
    public void ThemeApplicationService_apply_linked_theme_embeds_snapshot_and_tracks_source_version()
    {
        var project = new PresentationProject
        {
            Manifest = new PresentationManifest { PresentationId = "apply-theme" },
            Slides = [PresentationModelUtilities.CreateSlide(type: "song", content: "Lyrics", section: "verse")],
        };
        var theme = new ThemeTemplate
        {
            Id = "theme-1",
            Version = "source-v1",
            Name = "Song Theme",
            Slides =
            [
                new ThemeTemplateSlide
                {
                    Id = "theme-verse",
                    LayoutType = "verse",
                    Layers = [PresentationModelUtilities.CreateTextLayer("Placeholder")],
                },
            ],
        };

        new ThemeApplicationService().ApplyLinkedTheme(project, project.Slides[0], theme, theme.Slides[0]);

        project.Slides[0].ThemeBinding.Should().NotBeNull();
        project.Slides[0].ThemeBinding!.Mode.Should().Be(ThemeBindingModes.Linked);
        project.Slides[0].ThemeBinding!.ThemeVersion.Should().Be("source-v1");
        project.EmbeddedThemes.Should().ContainSingle(entry => entry.Template != null && entry.Template.Id == "theme-1");
    }

    [Fact]
    public void NormalizeProject_defaults_output_scale_mode_to_fit()
    {
        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                AspectRatio = "16:9",
                OutputScaleMode = string.Empty,
            },
            Slides = [],
        };

        PresentationModelUtilities.NormalizeProject(project);

        project.Manifest.OutputScaleMode.Should().Be(PresentationModelUtilities.DefaultOutputScaleMode);
    }

    // ── New arrangement / auto-advance / transition persistence tests ────────

    [Fact]
    public async Task PresentationProjectService_round_trips_named_arrangements_and_active_arrangement()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var service = new PresentationProjectService(
            paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);

        var project = BuildTwoGroupProject("arr-test");

        // Build section groups and add a custom arrangement.
        PresentationModelUtilities.ReconcileArrangement(project);
        var chorusGroup = project.Arrangement.Sections.First(g => g.Section == "chorus");

        var customArr = new NamedArrangement
        {
            Id = "custom-1",
            Name = "Short Version",
            IsNatural = false,
            Groups =
            [
                new ArrangementGroupRef { SectionGroupId = chorusGroup.Id },
                new ArrangementGroupRef { SectionGroupId = chorusGroup.Id },
            ],
        };
        project.Arrangement.Arrangements.Add(customArr);
        project.Arrangement.ActiveArrangementId = "custom-1";

        service.Save(project, Path.Combine("presentations", "arr-test.cpres"));
        var reopened = service.Open(Path.Combine("presentations", "arr-test.cpres"));

        reopened.Arrangement.ActiveArrangementId.Should().Be("custom-1");
        reopened.Arrangement.Arrangements.Should().HaveCountGreaterThanOrEqualTo(2);

        var saved = reopened.Arrangement.Arrangements.First(a => a.Id == "custom-1");
        saved.Name.Should().Be("Short Version");
        saved.IsNatural.Should().BeFalse();
        saved.Groups.Should().HaveCount(2,
            "the chorus is referenced twice so it appears twice in the arrangement");
        saved.Groups.Should().OnlyContain(r => r.SectionGroupId == chorusGroup.Id);
    }

    [Fact]
    public async Task PresentationProjectService_round_trips_auto_advance_seconds()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var service = new PresentationProjectService(
            paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);

        var project = BuildTwoGroupProject("aa-test");
        project.Arrangement.AutoAdvanceSeconds = 10;

        service.Save(project, Path.Combine("presentations", "aa-test.cpres"));
        var reopened = service.Open(Path.Combine("presentations", "aa-test.cpres"));

        reopened.Arrangement.AutoAdvanceSeconds.Should().Be(10);
    }

    [Fact]
    public async Task PresentationProjectService_round_trips_default_transition()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var service = new PresentationProjectService(
            paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);

        var project = BuildTwoGroupProject("tr-test");
        project.Arrangement.DefaultTransition = new SlideTransition
        {
            Type = "fade",
            Duration = 600,
            Parameters = new Dictionary<string, string> { ["direction"] = "fromLeft" },
        };

        service.Save(project, Path.Combine("presentations", "tr-test.cpres"));
        var reopened = service.Open(Path.Combine("presentations", "tr-test.cpres"));

        reopened.Arrangement.DefaultTransition.Should().NotBeNull();
        reopened.Arrangement.DefaultTransition!.Type.Should().Be("fade");
        reopened.Arrangement.DefaultTransition.Duration.Should().Be(600);
        reopened.Arrangement.DefaultTransition.GetParameter("direction", "fromRight").Should().Be("fromLeft");
    }

    [Fact]
    public void ReconcileArrangement_preserves_stable_group_ids_across_calls()
    {
        var project = BuildTwoGroupProject("reconcile-test");
        PresentationModelUtilities.ReconcileArrangement(project);

        var firstVerseId = project.Arrangement.Sections.First(g => g.Section == "verse").Id;

        // Simulate a slide mutation → reconcile again.
        PresentationModelUtilities.ReconcileArrangement(project);

        var secondVerseId = project.Arrangement.Sections.First(g => g.Section == "verse").Id;

        firstVerseId.Should().Be(secondVerseId,
            "group IDs must remain stable so custom arrangements keep their references");
    }

    [Fact]
    public void ReconcileArrangement_creates_natural_arrangement_entry()
    {
        var project = BuildTwoGroupProject("natural-arr");
        PresentationModelUtilities.ReconcileArrangement(project);

        var natural = project.Arrangement.Arrangements.FirstOrDefault(a => a.IsNatural);

        natural.Should().NotBeNull();
        natural!.Name.Should().Be("Master");
        natural.Groups.Should().HaveCount(project.Arrangement.Sections.Count);
    }

    [Fact]
    public void BuildPlaybackSequence_natural_order_equals_slide_order()
    {
        var project = BuildTwoGroupProject("seq-natural");
        PresentationModelUtilities.ReconcileArrangement(project);

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        seq.Instances.Select(i => i.SlideId)
            .Should().ContainInOrder(project.Slides.Select(s => s.Id));
    }

    [Fact]
    public void BuildPlaybackSequence_repeated_group_produces_distinct_instance_keys()
    {
        var project = BuildTwoGroupProject("seq-repeat");
        PresentationModelUtilities.ReconcileArrangement(project);

        var chorusGroup = project.Arrangement.Sections.First(g => g.Section == "chorus");

        // Custom arrangement: chorus, verse, chorus (chorus repeated)
        var verseGroup = project.Arrangement.Sections.First(g => g.Section == "verse");
        var customArr = new NamedArrangement
        {
            Id = "repeat-arr",
            Name = "Repeat Chorus",
            IsNatural = false,
            Groups =
            [
                new ArrangementGroupRef { SectionGroupId = chorusGroup.Id },
                new ArrangementGroupRef { SectionGroupId = verseGroup.Id },
                new ArrangementGroupRef { SectionGroupId = chorusGroup.Id },
            ],
        };
        project.Arrangement.Arrangements.Add(customArr);
        project.Arrangement.ActiveArrangementId = "repeat-arr";

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        // Total count: chorusSlides × 2 + verseSlides × 1
        var chorusSlideCount = chorusGroup.SlideIds.Count;
        var verseSlideCount = verseGroup.SlideIds.Count;
        seq.Count.Should().Be(chorusSlideCount * 2 + verseSlideCount);

        // All instance keys must be unique.
        var keys = seq.Instances.Select(i => i.InstanceKey).ToList();
        keys.Should().OnlyHaveUniqueItems("each repeated position needs a distinct instance key");

        // Two occurrences of the same chorus slide should share SlideId but differ in InstanceKey.
        var chorusSlideId = chorusGroup.SlideIds[0];
        var chorusInstances = seq.Instances.Where(i => i.SlideId == chorusSlideId).ToList();
        chorusInstances.Should().HaveCountGreaterThanOrEqualTo(2);
        chorusInstances.Select(i => i.OccurrenceIndex).Should().Contain(0).And.Contain(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PresentationProject BuildTwoGroupProject(string id) =>
        new()
        {
            Manifest = new PresentationManifest
            {
                PresentationId = id,
                Title = "Test Song",
                CreatedAt = "2026-01-01T00:00:00.000Z",
                UpdatedAt = "2026-01-01T00:00:00.000Z",
                AspectRatio = "16:9",
            },
            Slides =
            [
                new PresentationSlide { Id = "v1", Type = "song", Section = "verse",  SectionLabel = "Verse 1" },
                new PresentationSlide { Id = "v2", Type = "song", Section = "verse",  SectionLabel = "Verse 1" },
                new PresentationSlide { Id = "c1", Type = "song", Section = "chorus", SectionLabel = "Chorus"  },
                new PresentationSlide { Id = "c2", Type = "song", Section = "chorus", SectionLabel = "Chorus"  },
            ],
            Arrangement = new PresentationArrangement { Order = ["v1", "v2", "c1", "c2"] },
        };
}