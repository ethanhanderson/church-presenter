
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Show;

public sealed class ShowTransitionToolbarTests
{
    [Fact]
    public void ToSlideTransition_returns_null_when_toolbar_transition_is_missing()
    {
        ShowTransitionToolbar.ToSlideTransition(null).Should().BeNull();
    }

    [Fact]
    public void ToSlideTransition_returns_null_when_toolbar_mode_is_unset()
    {
        var dto = new ShowToolbarTransitionDto
        {
            Mode = string.Empty,
            DissolveDurationMs = 200,
        };

        ShowTransitionToolbar.ToSlideTransition(dto).Should().BeNull();
    }

    [Fact]
    public void ToSlideTransition_returns_dissolve_when_explicitly_selected()
    {
        var dto = new ShowToolbarTransitionDto
        {
            Mode = "dissolve",
            DissolveDurationMs = 300,
        };

        var transition = ShowTransitionToolbar.ToSlideTransition(dto);

        transition.Should().NotBeNull();
        transition!.Type.Should().Be("fade");
        transition.Duration.Should().Be(300);
    }
}