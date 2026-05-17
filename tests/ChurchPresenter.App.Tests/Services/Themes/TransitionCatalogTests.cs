
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Themes;

public sealed class TransitionCatalogTests
{
    [Fact]
    public void TransitionCatalog_exposes_cut_transition()
    {
        TransitionCatalog.All.Select(definition => definition.Key)
            .Should()
            .Contain(key => string.Equals(key, "cut", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TransitionCatalog_defaults_to_fade_for_unknown_transition()
    {
        TransitionCatalog.Find("cut").Should().NotBeNull();
        TransitionCatalog.FindOrDefault("unknown-transition").Key.Should().Be("fade");
    }
}