
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Documents;

public sealed class PresentationModelUtilitiesTests
{
    [Fact]
    public void CreateSlide_creates_transparent_blank_slide_without_content()
    {
        var slide = PresentationModelUtilities.CreateSlide("blank");

        slide.Background.Should().BeOfType<TransparentSlideBackground>();
        slide.Layers.Should().BeEmpty();
        slide.TextBlocks.Should().BeEmpty();
    }

    [Fact]
    public void ReconcileArrangement_allows_presentation_without_slides()
    {
        var project = new PresentationProject
        {
            Slides = [],
            Arrangement = new PresentationArrangement
            {
                Order = ["deleted-slide"],
                Sections =
                [
                    new SectionGroup
                    {
                        Id = "deleted-section",
                        Section = "verse",
                        Label = "Verse",
                        SlideIds = ["deleted-slide"],
                    },
                ],
            },
        };

        PresentationModelUtilities.ReconcileArrangement(project);

        project.Slides.Should().BeEmpty();
        project.Arrangement.Order.Should().BeEmpty();
        project.Arrangement.Sections.Should().BeEmpty();
        project.Arrangement.Arrangements.Should().ContainSingle(arrangement => arrangement.IsNatural)
            .Which.Groups.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlaybackSequence_falls_back_to_natural_sequence_when_section_group_ids_are_duplicated()
    {
        var project = new PresentationProject
        {
            Slides =
            [
                new PresentationSlide { Id = "slide-intro", Section = "intro" },
                new PresentationSlide { Id = "slide-verse-1", Section = "verse", SectionIndex = 0 },
                new PresentationSlide { Id = "slide-verse-2", Section = "verse", SectionIndex = 1 },
            ],
            Arrangement = new PresentationArrangement
            {
                ActiveArrangementId = "default",
                Sections =
                [
                    new SectionGroup
                    {
                        Id = "intro-group",
                        Section = "intro",
                        Label = "Intro",
                        SlideIds = ["slide-intro"],
                    },
                    new SectionGroup
                    {
                        Id = "verse-group",
                        Section = "verse",
                        Label = "Verse",
                        SlideIds = ["slide-verse-1"],
                    },
                    new SectionGroup
                    {
                        Id = "verse-group",
                        Section = "verse",
                        Label = "Verse",
                        SlideIds = ["slide-verse-2"],
                    },
                ],
                Arrangements =
                [
                    new NamedArrangement
                    {
                        Id = "natural",
                        Name = "Master",
                        IsNatural = true,
                        Groups =
                        [
                            new ArrangementGroupRef { SectionGroupId = "intro-group" },
                            new ArrangementGroupRef { SectionGroupId = "verse-group" },
                            new ArrangementGroupRef { SectionGroupId = "verse-group" },
                        ],
                    },
                    new NamedArrangement
                    {
                        Id = "default",
                        Name = "Default",
                        Groups = [new ArrangementGroupRef { SectionGroupId = "verse-group" }],
                    },
                ],
            },
        };

        var sequence = PresentationModelUtilities.BuildPlaybackSequence(project);

        sequence.ActiveArrangementId.Should().BeNull();
        sequence.Instances.Select(instance => instance.SlideId).Should().Equal("slide-intro", "slide-verse-1", "slide-verse-2");
        sequence.Instances.Select(instance => instance.InstanceKey).Should().Equal("slide-intro", "slide-verse-1", "slide-verse-2");
    }

    [Fact]
    public void NormalizeSlide_trims_media_cue_display_names_and_normalizes_transitions()
    {
        var slide = new PresentationSlide
        {
            Id = "slide-1",
            MediaCues =
            [
                new SlideMediaCue
                {
                    Id = "cue-1",
                    MediaId = "media-1",
                    MediaType = "video",
                    DisplayName = " Walk In ",
                    Transition = new SlideTransition
                    {
                        Type = "Fade",
                        Duration = 250,
                    },
                },
            ],
        };

        PresentationModelUtilities.NormalizeSlide(slide, slideSize: null);

        slide.MediaCues.Should().ContainSingle();
        slide.MediaCues[0].DisplayName.Should().Be("Walk In");
        slide.MediaCues[0].Transition.Should().NotBeNull();
        slide.MediaCues[0].Transition!.Type.Should().Be("fade");
        slide.MediaCues[0].Transition!.Duration.Should().Be(250);
    }
}