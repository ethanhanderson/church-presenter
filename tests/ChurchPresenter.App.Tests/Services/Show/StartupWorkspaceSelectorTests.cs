
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Show;

public sealed class StartupWorkspaceSelectorTests
{
    [Fact]
    public void TrySelectInitial_picks_newest_library_presentation()
    {
        var catalog = new CatalogDto
        {
            Libraries =
            {
                new LibraryDto
                {
                    Id = "L1",
                    Name = "Main",
                    Presentations =
                    {
                        new PresentationRefDto
                        {
                            Path = @"C:\a.cpres",
                            Title = "Old",
                            UpdatedAt = "2020-01-01T00:00:00Z",
                        },
                        new PresentationRefDto
                        {
                            Path = @"C:\b.cpres",
                            Title = "New",
                            UpdatedAt = "2025-06-01T00:00:00Z",
                        },
                    },
                },
            },
        };
        var settings = new AppSettingsDto();
        var result = StartupWorkspaceSelector.TrySelectInitial(catalog, settings);
        result.Should().NotBeNull();
        result!.SelectedPresentationPath.Should().Be(@"C:\b.cpres");
        result.SelectedLibraryId.Should().Be("L1");
    }
}