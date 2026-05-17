
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Settings;

/// <summary>Parity: single-instance / file activation hands off a path to the main window (see parity matrix: integrations).</summary>
public sealed class AppActivationServiceTests
{
    [Fact]
    public void ConsumePendingPresentationPath_returns_and_clears()
    {
        var svc = new AppActivationService();
        svc.SetPendingPresentationPath(@"C:\talks\svc.cpres");

        svc.PendingPresentationPath.Should().Be(@"C:\talks\svc.cpres");

        var first = svc.ConsumePendingPresentationPath();
        first.Should().Be(@"C:\talks\svc.cpres");

        svc.PendingPresentationPath.Should().BeNull();
        svc.ConsumePendingPresentationPath().Should().BeNull();
    }
}