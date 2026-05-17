
using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Documents;

public sealed class PresentationTextWorkflowServiceTests
{
    [Fact]
    public async Task SaveSlideTextAsync_updates_primary_text_layer_and_invalidates_cues()
    {
        PresentationProject project = CreateProject();
        FakePresentationProjectService projects = new(project);
        Mock<ICuePreparationService> cues = new();
        PresentationTextWorkflowService service = new(projects, cues.Object);

        PresentationTextDocument result = await service.SaveSlideTextAsync(
            "service.cpres",
            "slide-1",
            "Updated lyrics",
            "Pastor note");

        project.Slides[0].Layers.OfType<TextLayer>().Single().Content.Should().Be("Updated lyrics");
        project.Slides[0].Notes.Should().Be("Pastor note");
        projects.SaveCount.Should().Be(1);
        result.Slides.First(slide => slide.SlideId == "slide-1").Text.Should().Be("Updated lyrics");
        cues.Verify(service => service.InvalidatePresentationCues("service.cpres"), Times.Once);
    }

    [Fact]
    public async Task ReflowAsync_regenerates_slides_from_blank_line_blocks()
    {
        PresentationProject project = CreateProject();
        FakePresentationProjectService projects = new(project);
        PresentationTextWorkflowService service = new(projects, Mock.Of<ICuePreparationService>());

        PresentationTextDocument result = await service.ReflowAsync(
            "service.cpres",
            "Verse one\r\nline two\r\n\r\nChorus line");

        project.Slides.Should().HaveCount(2);
        project.Slides[0].Layers.OfType<TextLayer>().Single().Content.Should().Be($"Verse one{Environment.NewLine}line two");
        project.Slides[1].Layers.OfType<TextLayer>().Single().Content.Should().Be("Chorus line");
        project.Arrangement.Order.Should().Equal("slide-1", "slide-2");
        result.ReflowText.Should().Contain("Chorus line");
    }

    [Fact]
    public async Task ReflowAsync_preserves_slide_semantics_and_rebuilds_groups()
    {
        PresentationProject project = CreateProject();
        project.Manifest.ThemeId = "theme-main";
        project.EmbeddedThemes.Add(new BundleThemeEntry { FileName = "main.json", Template = new ThemeTemplate { Id = "theme-main" } });
        project.Arrangement = new PresentationArrangement
        {
            Sections =
            [
                new SectionGroup { Id = "verse-group", Section = "verse", Label = "Verse", SlideIds = ["slide-1"] },
                new SectionGroup { Id = "chorus-group", Section = "chorus", Label = "Chorus", SlideIds = ["slide-2"] },
            ],
            Arrangements =
            [
                new NamedArrangement
                {
                    Id = "arr-main",
                    Name = "Main",
                    Groups =
                    [
                        new ArrangementGroupRef { SectionGroupId = "verse-group" },
                        new ArrangementGroupRef { SectionGroupId = "chorus-group" },
                    ],
                },
            ],
            ActiveArrangementId = "arr-main",
        };
        project.Slides[0].Section = "verse";
        project.Slides[0].LayoutType = "lyrics";
        project.Slides[0].Actions.Add(new SlideActionDefinition { Id = "action-1", Type = "clearLayer" });
        project.Slides[0].MediaCues.Add(new SlideMediaCue { Id = "cue-1", MediaId = "media-1" });
        project.Slides[0].Layers.Add(new ShapeLayer { Id = "shape-1", Name = "Accent" });
        project.Slides[0].Notes = "Keep this cue note";

        FakePresentationProjectService projects = new(project);
        PresentationTextWorkflowService service = new(projects, Mock.Of<ICuePreparationService>());

        PresentationTextDocument result = await service.ReflowAsync(
            "service.cpres",
            "Updated verse\r\n\r\nUpdated chorus");

        PresentationSlide firstSlide = project.Slides[0];
        firstSlide.Layers.OfType<TextLayer>().Single().Content.Should().Be("Updated verse");
        firstSlide.Layers.OfType<ShapeLayer>().Single().Id.Should().Be("shape-1");
        firstSlide.Actions.Should().ContainSingle(action => action.Id == "action-1");
        firstSlide.MediaCues.Should().ContainSingle(cue => cue.Id == "cue-1");
        firstSlide.Notes.Should().Be("Keep this cue note");
        string[] expectedSlideIds = ["slide-1"];
        project.Arrangement.Sections.Should().Contain(section =>
            section.Id == "verse-group" && section.SlideIds.SequenceEqual(expectedSlideIds));
        project.Arrangement.Arrangements.Single().Groups.Should().HaveCount(2);
        result.ThemeId.Should().Be("theme-main");
        result.EmbeddedThemeCount.Should().Be(1);
        result.CueSummary.Should().Contain("1 slide action");
        result.Slides[0].MetadataSummary.Should().Contain("1 action");
        result.Slides[0].LayerCount.Should().Be(2);
    }

    private static PresentationProject CreateProject() => new()
    {
        SourcePath = "service.cpres",
        Manifest = new PresentationManifest { Title = "Sunday Service" },
        Slides =
        [
            new PresentationSlide
            {
                Id = "slide-1",
                SectionLabel = "Verse",
                Layers =
                [
                    new TextLayer
                    {
                        Id = "text-1",
                        Name = "Lyrics",
                        Content = "Original lyrics",
                    },
                ],
            },
            new PresentationSlide
            {
                Id = "slide-2",
                SectionLabel = "Chorus",
                Layers =
                [
                    new TextLayer
                    {
                        Id = "text-1",
                        Name = "Lyrics",
                        Content = "Original chorus",
                    },
                ],
            },
        ],
    };

    private sealed class FakePresentationProjectService(PresentationProject project) : IPresentationProjectService
    {
        public int SaveCount { get; private set; }

        public PresentationProject Open(string path)
        {
            path.Should().Be("service.cpres");
            return project;
        }

        public void Save(PresentationProject projectToSave, string path)
        {
            projectToSave.Should().BeSameAs(project);
            path.Should().Be("service.cpres");
            SaveCount++;
        }
    }
}