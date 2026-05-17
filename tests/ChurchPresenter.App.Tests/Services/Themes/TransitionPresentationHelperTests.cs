



using FluentAssertions;



namespace ChurchPresenter.App.Tests.Services.Themes;



public sealed class TransitionPresentationHelperTests

{

    [Fact]

    public void GetDefaultTransitionForDisplay_WithOnlySlideTransitions_ReturnsNull()

    {

        var project = new PresentationProject

        {

            Arrangement = new PresentationArrangement(),

            Slides =

            [

                new PresentationSlide

                {

                    Id = "a",

                    Animations = new SlideAnimations

                    {

                        Transition = new SlideTransition { Type = "fade", Duration = 300, Easing = "ease-out" },

                    },

                },

                new PresentationSlide

                {

                    Id = "b",

                    Animations = new SlideAnimations

                    {

                        Transition = new SlideTransition { Type = "fade", Duration = 300, Easing = "ease-out" },

                    },

                },

            ],

        };



        PresentationModelUtilities.NormalizeProject(project);



        TransitionPresentationHelper.GetDefaultTransitionForDisplay(project).Should().BeNull();

        project.Arrangement.DefaultTransition.Should().BeNull();

    }



    [Fact]

    public void GetDefaultTransitionForDisplay_PrefersArrangementDefaultOverSlides()

    {

        var project = new PresentationProject

        {

            Arrangement = new PresentationArrangement

            {

                DefaultTransition = new SlideTransition { Type = "wipe", Duration = 500, Parameters = new Dictionary<string, string> { ["direction"] = "fromRight" } },

            },

            Slides =

            [

                new PresentationSlide

                {

                    Id = "a",

                    Animations = new SlideAnimations

                    {

                        Transition = new SlideTransition { Type = "fade", Duration = 300 },

                    },

                },

            ],

        };



        PresentationModelUtilities.NormalizeProject(project);



        var display = TransitionPresentationHelper.GetDefaultTransitionForDisplay(project);

        display.Should().NotBeNull();

        display!.Type.Should().Be("wipe");

    }



    [Fact]

    public void HasPresentationTransitionConfigured_ReflectsArrangementDefaultOnly()

    {

        var empty = new PresentationProject { Arrangement = new PresentationArrangement(), Slides = [] };

        PresentationModelUtilities.NormalizeProject(empty);

        TransitionPresentationHelper.HasPresentationTransitionConfigured(empty).Should().BeFalse();



        empty.Arrangement.DefaultTransition = new SlideTransition { Type = "fade", Duration = 300 };

        TransitionPresentationHelper.HasPresentationTransitionConfigured(empty).Should().BeTrue();

    }



    [Fact]

    public void NormalizeForStorage_LowercasesTypeAndEasing()

    {

        var t = TransitionStorageNormalizer.NormalizeForStorage(new SlideTransition

        {

            Type = "Fade",

            Duration = 250,

            Easing = "Ease-In",

        });



        t.Should().NotBeNull();

        t!.Type.Should().Be("fade");

        t.Easing.Should().Be("ease-in");

        t.Duration.Should().Be(250);

    }

}