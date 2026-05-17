using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

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
        Mock<ILiveSessionService> live = new();
        Mock<IShowTimerService> timers = new();
        FakeLiveProductionFacade liveProduction = new();
        SlideActionExecutionService service = new(
            live.Object,
            timers.Object,
            NullLogger<SlideActionExecutionService>.Instance,
            liveProduction);
        PresentationSlide slide = new()
        {
            Actions =
            [
                new SlideActionDefinition { Type = "clearPresentation" },
                new SlideActionDefinition { Type = "clearMedia" },
            ],
        };

        service.ExecuteForSlide(slide);

        liveProduction.CommandBatches.Should().HaveCount(1);
        liveProduction.CommandBatches[0].Should().HaveCount(2);
        liveProduction.CommandBatches[0][0].Clear!.Layers.Should().Equal(BackendOutputLayerKind.Slide);
        liveProduction.CommandBatches[0][1].Clear!.Layers.Should().Equal(
            BackendOutputLayerKind.Media,
            BackendOutputLayerKind.Audio);
        live.Verify(session => session.ClearPresentation(), Times.Never);
        live.Verify(session => session.ClearMedia(), Times.Never);
    }

    [Fact]
    public void ExecuteForSlide_routes_generated_overlay_actions_to_backend_command_pipeline()
    {
        Mock<ILiveSessionService> live = new();
        Mock<IShowTimerService> timers = new();
        FakeLiveProductionFacade liveProduction = new();
        SlideActionExecutionService service = new(
            live.Object,
            timers.Object,
            NullLogger<SlideActionExecutionService>.Instance,
            liveProduction);
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

        liveProduction.CommandBatches.Should().ContainSingle();
        LiveCommand[] commands = liveProduction.CommandBatches.Single().ToArray();
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
        Mock<ILiveSessionService> live = new();
        Mock<IShowTimerService> timers = new();
        FakeLiveProductionFacade liveProduction = new();
        SlideActionExecutionService service = new(
            live.Object,
            timers.Object,
            NullLogger<SlideActionExecutionService>.Instance,
            liveProduction);
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

        liveProduction.Macros.Should().ContainSingle();
        liveProduction.Macros[0].Id.Should().Be("walk-in");
        liveProduction.Macros[0].Commands.Count(command =>
            command.Overlay != null
            && command.Overlay.Kind == OverlayContentKind.Message
            && command.Overlay.Id == "walk-in-message")
            .Should().Be(1);
    }

    private sealed class FakeLiveProductionFacade : ILiveProductionFacade
    {
        public event EventHandler<LiveProductionChangedEventArgs>? Changed;

        public LiveProductionSnapshot Current { get; } = LiveProductionSnapshot.Empty;

        public List<IReadOnlyList<LiveCommand>> CommandBatches { get; } = [];

        public List<LiveMacroDefinition> Macros { get; } = [];

        public Task SetLookAsync(string lookId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ActionResult SetOverlay(OverlayContentState overlay) => new() { Succeeded = true };

        public ActionResult SetTimer(TimerSnapshot timer) => new() { Succeeded = true };

        public ActionResult SetCaptureSession(CaptureSessionState captureSession) => new() { Succeeded = true };

        public void ReportOutputHostFeedback(OutputHostFrameFeedbackState feedback)
        {
        }

        public void ReportMediaPlayerFailure(MediaPlayerFailureState failure)
        {
        }

        public ActionResult ExecuteCommands(
            IEnumerable<LiveCommand> commands,
            LiveCommandSource? source = null,
            string? macroId = null)
        {
            CommandBatches.Add(commands.ToArray());
            Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
            return new ActionResult { Succeeded = true };
        }

        public ActionResult ExecuteMacro(LiveMacroDefinition macro, LiveCommandSource? source = null)
        {
            Macros.Add(macro);
            Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
            return new ActionResult { Succeeded = true };
        }

        public ActionResult ClearGroup(string clearGroupId) => new() { Succeeded = true };

        public ActionResult ClearLayers(IEnumerable<BackendOutputLayerKind> layers)
        {
            Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
            return new ActionResult { Succeeded = true };
        }

        public ActionResult ReleaseClearedLayers(IEnumerable<BackendOutputLayerKind> layers)
        {
            Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
            return new ActionResult { Succeeded = true };
        }

        public ActionResult SetStageLayout(
            string screenId,
            string stageLayoutId,
            StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience)
        {
            return new ActionResult { Succeeded = true };
        }
    }
}