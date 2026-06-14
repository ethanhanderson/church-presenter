using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Runtime;

/// <summary>
/// Covers slide-action runtime integration points.
/// </summary>
public sealed class SlideActionExecutionServiceTests
{
    [Fact]
    public void ExecuteForSlide_routes_clear_actions_to_backend_command_pipeline()
    {
        FakeCueDispatcher cueDispatcher = new();
        Mock<IShowTimerService> timers = new();
        SlideActionExecutionService service = new(
            cueDispatcher,
            timers.Object,
            NullLogger<SlideActionExecutionService>.Instance);
        PresentationSlide slide = new()
        {
            Actions =
            [
                new SlideActionDefinition { Type = "clearPresentation" },
                new SlideActionDefinition { Type = "clearMedia" },
            ],
        };

        service.ExecuteForSlide(slide);

        LiveCommand[] commands = cueDispatcher.CommandBatches.SelectMany(batch => batch).ToArray();
        commands.Should().HaveCount(2);
        commands[0].Clear!.Layers.Should().Equal(BackendOutputLayerKind.Slide);
        commands[1].Clear!.Layers.Should().Equal(
            BackendOutputLayerKind.Media,
            BackendOutputLayerKind.Audio);
    }

    [Fact]
    public void ExecuteForSlide_routes_generated_overlay_actions_to_backend_command_pipeline()
    {
        FakeCueDispatcher cueDispatcher = new();
        Mock<IShowTimerService> timers = new();
        SlideActionExecutionService service = new(
            cueDispatcher,
            timers.Object,
            NullLogger<SlideActionExecutionService>.Instance);
        PresentationSlide slide = new()
        {
            Id = "slide-1",
            Actions =
            [
                new SlideActionDefinition { Id = "msg-1", Type = "showMessage", Label = "Greeting", Value = "Welcome!" },
                new SlideActionDefinition { Id = "prop-1", Type = "showProp", Label = "Lower Third", Value = "Pastor Ethan" },
                new SlideActionDefinition { Id = "ann-1", Type = "showAnnouncement", Label = "Lobby", Value = "Join us after service" },
                new SlideActionDefinition { Id = "stage-1", Type = "showStageMessage", Value = "Stand by" },
            ],
        };

        service.ExecuteForSlide(slide);

        LiveCommand[] commands = cueDispatcher.CommandBatches.SelectMany(batch => batch).ToArray();
        commands.Should().HaveCount(4);
        commands.Should().AllSatisfy(command => command.Source.Kind.Should().Be(LiveCommandSourceKind.SlideAction));
        commands.Any(command =>
            command.Overlay != null
            && command.Overlay.Kind == OverlayContentKind.Message
            && command.Overlay.Id == "msg-1"
            && command.Overlay.IsVisible
            && command.Overlay.Text == "Welcome!")
            .Should().BeTrue();
        commands.Any(command =>
            command.Overlay != null
            && command.Overlay.Kind == OverlayContentKind.Prop
            && command.Overlay.Id == "prop-1"
            && command.Overlay.IsVisible
            && command.Overlay.Text == "Pastor Ethan")
            .Should().BeTrue();
        commands.Any(command =>
            command.Overlay != null
            && command.Overlay.Kind == OverlayContentKind.Announcement
            && command.Overlay.Id == "ann-1"
            && command.Overlay.IsVisible
            && command.Overlay.Text == "Join us after service")
            .Should().BeTrue();
        commands.Any(command =>
            command.Overlay != null
            && command.Overlay.Kind == OverlayContentKind.StageMessage
            && command.Overlay.Id == "stage-1"
            && command.Overlay.IsVisible
            && command.Overlay.Text == "Stand by")
            .Should().BeTrue();
    }

    [Fact]
    public void ExecuteForSlide_routes_macro_action_to_backend_macro_pipeline()
    {
        FakeCueDispatcher cueDispatcher = new();
        Mock<IShowTimerService> timers = new();
        SlideActionExecutionService service = new(
            cueDispatcher,
            timers.Object,
            NullLogger<SlideActionExecutionService>.Instance);
        PresentationSlide slide = new()
        {
            Id = "slide-1",
            Actions =
            [
                new SlideActionDefinition
                {
                    Id = "macro-action",
                    Type = "triggerMacro",
                    Label = "Walk In",
                    Value = "walk-in",
                    ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
                    {
                        ["commands"] = System.Text.Json.JsonSerializer.SerializeToElement(new[]
                        {
                            LiveCommandExecutor.SetOverlay(new OverlayContentState
                            {
                                Id = "walk-in-message",
                                Name = "Walk In",
                                Kind = OverlayContentKind.Message,
                                IsVisible = true,
                                Text = "Welcome",
                            }),
                        }),
                    },
                },
            ],
        };

        service.ExecuteForSlide(slide);

        cueDispatcher.LastMacro.Should().NotBeNull();
        cueDispatcher.LastMacro!.Id.Should().Be("walk-in");
        cueDispatcher.LastMacro.Commands.Count(command =>
            command.Overlay != null
            && command.Overlay.Kind == OverlayContentKind.Message
            && command.Overlay.Id == "walk-in-message")
            .Should().Be(1);
    }
}
