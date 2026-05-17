
using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Show;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public void Show_output_panel_width_contract_matches_interface_resize_range()
    {
        WorkspaceDto.ShowOutputPanelMinWidthDpi.Should().Be(360);
        WorkspaceDto.ShowOutputPanelDefaultWidthDpi.Should().Be(460);
        WorkspaceDto.ShowOutputPanelMaxWidthDpi.Should().Be(560);
    }

    [Fact]
    public async Task LoadAsync_missing_file_uses_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.GetAppDataDirectory()).Returns(root);

        var svc = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        await svc.LoadAsync();

        svc.Workspace.ActivePage.Should().Be("show");
        svc.Workspace.SelectedLibraryId.Should().BeNull();
        svc.Workspace.ShowOutputPanelWidth.Should().Be(WorkspaceDto.ShowOutputPanelDefaultWidthDpi);
    }

    [Fact]
    public async Task LoadAsync_invalid_json_uses_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "workspace.json"), "{ not valid json");

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.GetAppDataDirectory()).Returns(root);

        var svc = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        await svc.LoadAsync();

        svc.Workspace.ActivePage.Should().Be("show");
        svc.Workspace.SelectedPresentationPath.Should().BeNull();
        svc.Workspace.ShowOutputPanelWidth.Should().Be(WorkspaceDto.ShowOutputPanelDefaultWidthDpi);
    }

    [Fact]
    public async Task LoadAsync_blank_active_page_normalizes_to_show()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "workspace.json"),
            """
            {
              "activePage": "  ",
              "selectedLibraryId": "lib-1"
            }
            """);

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.GetAppDataDirectory()).Returns(root);

        var svc = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        await svc.LoadAsync();

        svc.Workspace.ActivePage.Should().Be("show");
        svc.Workspace.SelectedLibraryId.Should().Be("lib-1");
        svc.Workspace.ShowOutputPanelWidth.Should().Be(WorkspaceDto.ShowOutputPanelDefaultWidthDpi);
    }

    [Fact]
    public async Task LoadAsync_clamps_legacy_small_output_panel_width_to_min()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "workspace.json"),
            """
            {
              "activePage": "show",
              "showOutputPanelWidth": 260
            }
            """);

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.GetAppDataDirectory()).Returns(root);

        var svc = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        await svc.LoadAsync();

        svc.Workspace.ActivePage.Should().Be("show");
        svc.Workspace.ShowOutputPanelWidth.Should().Be(WorkspaceDto.ShowOutputPanelMinWidthDpi);
    }

    [Fact]
    public async Task SaveAsync_round_trips_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.GetAppDataDirectory()).Returns(root);

        var svc = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        svc.Update(ws =>
        {
            ws.SelectedLibraryId = "lib-1";
            ws.SelectedPlaylistId = null;
            ws.SelectedPresentationPath = @"C:\x\a.cpres";
            ws.ShowOutputPanelWidth = 420;
        });
        await svc.SaveAsync();

        var svc2 = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        await svc2.LoadAsync();

        svc2.Workspace.SelectedLibraryId.Should().Be("lib-1");
        svc2.Workspace.SelectedPresentationPath.Should().Be(@"C:\x\a.cpres");
        svc2.Workspace.ShowOutputPanelWidth.Should().Be(420);
    }
}