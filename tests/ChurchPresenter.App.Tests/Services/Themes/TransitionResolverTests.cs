
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Themes;

/// <summary>
/// Verifies transition precedence, normalization, and round-trip persistence.
/// </summary>
public sealed class TransitionResolverTests
{
    // ── Normalize ────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_returns_null_for_null_input()
    {
        TransitionResolver.Normalize(null).Should().BeNull();
    }

    [Fact]
    public void Normalize_returns_null_for_empty_type()
    {
        var t = new SlideTransition { Type = "   ", Duration = 300 };
        TransitionResolver.Normalize(t).Should().BeNull();
    }

    [Fact]
    public void Normalize_passes_cut_through_regardless_of_duration()
    {
        var t = new SlideTransition { Type = "cut", Duration = 0 };
        TransitionResolver.Normalize(t).Should().BeSameAs(t);
    }

    [Fact]
    public void Normalize_returns_null_for_non_cut_with_zero_duration()
    {
        var t = new SlideTransition { Type = "fade", Duration = 0 };
        TransitionResolver.Normalize(t).Should().BeNull();
    }

    [Fact]
    public void Normalize_returns_transition_for_non_cut_with_positive_duration()
    {
        var t = new SlideTransition { Type = "fade", Duration = 400 };
        TransitionResolver.Normalize(t).Should().BeSameAs(t);
    }

    // ── Resolve – null propagation ───────────────────────────────────────────

    [Fact]
    public void Resolve_returns_null_when_slide_and_arrangement_are_null()
    {
        TransitionResolver.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_null_when_arrangement_has_no_default()
    {
        var arrangement = new PresentationArrangement();
        TransitionResolver.Resolve(null, arrangement).Should().BeNull();
    }

    // ── Resolve – presentation default ──────────────────────────────────────

    [Fact]
    public void Resolve_uses_presentation_default_when_slide_has_no_override()
    {
        var defaultTransition = new SlideTransition { Type = "fade", Duration = 500 };
        var arrangement = new PresentationArrangement { DefaultTransition = defaultTransition };

        var result = TransitionResolver.Resolve(null, arrangement);

        result.Should().NotBeNull();
        result!.Type.Should().Be("fade");
    }

    [Fact]
    public void Resolve_uses_presentation_default_when_slide_animations_is_null()
    {
        var defaultTransition = new SlideTransition { Type = "wipe", Duration = 600 };
        var arrangement = new PresentationArrangement { DefaultTransition = defaultTransition };
        var slide = new PresentationSlide { Id = "s1", Animations = null };

        var result = TransitionResolver.Resolve(slide, arrangement);

        result.Should().NotBeNull();
        result!.Type.Should().Be("wipe");
    }

    [Fact]
    public void Resolve_uses_presentation_default_when_slide_has_null_transition()
    {
        var defaultTransition = new SlideTransition { Type = "slide", Duration = 300 };
        var arrangement = new PresentationArrangement { DefaultTransition = defaultTransition };
        var slide = new PresentationSlide
        {
            Id = "s1",
            Animations = new SlideAnimations { Transition = null },
        };

        var result = TransitionResolver.Resolve(slide, arrangement);

        result.Should().NotBeNull();
        result!.Type.Should().Be("slide");
    }

    // ── Resolve – slide override takes precedence ─────────────────────────────

    [Fact]
    public void Resolve_prefers_slide_override_over_presentation_default()
    {
        var presentationDefault = new SlideTransition { Type = "fade", Duration = 400 };
        var slideOverride = new SlideTransition { Type = "zoom-in", Duration = 600 };
        var arrangement = new PresentationArrangement { DefaultTransition = presentationDefault };
        var slide = new PresentationSlide
        {
            Id = "s1",
            Animations = new SlideAnimations { Transition = slideOverride },
        };

        var result = TransitionResolver.Resolve(slide, arrangement);

        result.Should().NotBeNull();
        result!.Type.Should().Be("zoom-in");
    }

    [Fact]
    public void Resolve_slide_override_wins_even_when_presentation_default_exists()
    {
        var presentationDefault = new SlideTransition { Type = "wipe", Duration = 500 };
        var slideOverride = new SlideTransition { Type = "cut", Duration = 0 };
        var arrangement = new PresentationArrangement { DefaultTransition = presentationDefault };
        var slide = new PresentationSlide
        {
            Id = "s1",
            Animations = new SlideAnimations { Transition = slideOverride },
        };

        var result = TransitionResolver.Resolve(slide, arrangement);

        result.Should().NotBeNull();
        result!.Type.Should().Be("cut");
    }

    // ── Resolve – cut behaviour ───────────────────────────────────────────────

    [Fact]
    public void Resolve_returns_cut_when_presentation_default_is_cut()
    {
        var cutTransition = new SlideTransition { Type = "cut", Duration = 0 };
        var arrangement = new PresentationArrangement { DefaultTransition = cutTransition };

        var result = TransitionResolver.Resolve(null, arrangement);

        result.Should().NotBeNull();
        result!.Type.Should().Be("cut");
    }

    // ── Resolve – easing and parameters pass through ─────────────────────────

    [Fact]
    public void Resolve_preserves_easing_from_slide_override()
    {
        var slideOverride = new SlideTransition { Type = "fade", Duration = 400, Easing = "ease-out" };
        var slide = new PresentationSlide
        {
            Id = "s1",
            Animations = new SlideAnimations { Transition = slideOverride },
        };

        var result = TransitionResolver.Resolve(slide, null);

        result.Should().NotBeNull();
        result!.Easing.Should().Be("ease-out");
    }

    [Fact]
    public void Resolve_preserves_direction_parameter_from_presentation_default()
    {
        var defaultTransition = new SlideTransition
        {
            Type = "wipe",
            Duration = 400,
            Parameters = new Dictionary<string, string> { ["direction"] = "fromRight" },
        };
        var arrangement = new PresentationArrangement { DefaultTransition = defaultTransition };

        var result = TransitionResolver.Resolve(null, arrangement);

        result.Should().NotBeNull();
        result!.GetParameter("direction").Should().Be("fromRight");
    }

    [Fact]
    public void Resolve_uses_global_fallback_when_slide_and_arrangement_have_no_transition()
    {
        var global = new SlideTransition { Type = "fade", Duration = 222 };
        var arrangement = new PresentationArrangement();
        var slide = new PresentationSlide { Id = "s1", Animations = new SlideAnimations { Transition = null } };

        var result = TransitionResolver.Resolve(slide, arrangement, global);

        result.Should().NotBeNull();
        result!.Duration.Should().Be(222);
    }

    [Fact]
    public void Resolve_global_fallback_does_not_override_slide_or_arrangement()
    {
        var global = new SlideTransition { Type = "zoom-in", Duration = 900 };
        var arrangement = new PresentationArrangement
        {
            DefaultTransition = new SlideTransition { Type = "fade", Duration = 100 },
        };
        var slide = new PresentationSlide
        {
            Id = "s1",
            Animations = new SlideAnimations { Transition = new SlideTransition { Type = "cut", Duration = 0 } },
        };

        TransitionResolver.Resolve(slide, arrangement, global)!.Type.Should().Be("cut");
        TransitionResolver.Resolve(
            new PresentationSlide { Id = "s2", Animations = new SlideAnimations { Transition = null } },
            arrangement,
            global)!.Type.Should().Be("fade");
    }
}