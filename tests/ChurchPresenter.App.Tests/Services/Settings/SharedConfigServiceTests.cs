using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Settings;

/// <summary>
/// Tests for portable shared configuration files under the managed content root.
/// </summary>
public sealed class SharedConfigServiceTests
{
    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_portable_stage_configuration()
    {
        string root = CreateRoot();
        SharedConfigService service = CreateService(root);
        service.UpdateStage(stage =>
        {
            stage.Layouts =
            [
                new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "current",
                            Kind = StageLayoutElementKind.CurrentSlideText,
                            Label = "Current",
                        },
                        new StageLayoutElement
                        {
                            Id = "timer",
                            Kind = StageLayoutElementKind.Timer,
                            SourceId = "service-start",
                        },
                    ],
                },
            ];
            stage.DefaultLayoutIdsByScreenId["stage"] = "confidence";
        });

        await service.SaveAsync();

        SharedConfigService loaded = CreateService(root);
        await loaded.LoadAsync();

        loaded.Stage.Layouts.Should().ContainSingle(layout => layout.Id == "confidence");
        loaded.Stage.Layouts[0].Elements.Should().HaveCount(2);
        loaded.Stage.DefaultLayoutIdsByScreenId.Should().ContainKey("stage");
        loaded.Stage.DefaultLayoutIdsByScreenId["stage"].Should().Be("confidence");
    }

    [Fact]
    public async Task LoadAsync_missing_stage_file_uses_portable_defaults()
    {
        SharedConfigService service = CreateService(CreateRoot());

        await service.LoadAsync();

        service.Stage.SchemaVersion.Should().Be(1);
        service.Stage.Layouts.Should().BeEmpty();
        service.Stage.DefaultLayoutIdsByScreenId.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_portable_output_looks()
    {
        string root = CreateRoot();
        SharedConfigService service = CreateService(root);
        service.UpdateOutput(output =>
        {
            output.Looks =
            [
                new OutputLookDefinition
                {
                    Id = "custom",
                    Name = "Custom",
                    Routes =
                    [
                        new OutputLookFeedRouting
                        {
                            FeedId = OutputFeedIds.Stream,
                            Slide = true,
                            Media = false,
                        },
                    ],
                },
            ];
        });

        await service.SaveAsync();

        SharedConfigService loaded = CreateService(root);
        await loaded.LoadAsync();

        loaded.Output.Looks.Should().ContainSingle(look => look.Id == "custom");
        loaded.Output.Looks[0].Routes.Should().ContainSingle(route => route.FeedId == OutputFeedIds.Stream)
            .Which.Media.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_portable_support_boundaries()
    {
        string root = CreateRoot();
        SharedConfigService service = CreateService(root);
        service.UpdateOutput(output =>
        {
            output.LogicalScreens.Add(new LogicalScreenDefinition { Id = "main", Name = "Main", Kind = "audience" });
            output.Looks.Add(new OutputLookDefinition
            {
                Id = "stream",
                Name = "Stream",
                ClearGroups =
                [
                    new OutputLookClearGroupDefinition { Id = "lyrics", Name = "Lyrics", Layers = ["slide", "messages"] },
                ],
            });
        });
        service.UpdateShow(show =>
        {
            show.Timers.Add(new ShowTimerDefinition { Id = "countdown", Name = "Countdown", DurationSeconds = 300 });
            show.Messages.Add(new ShowMessageDefinition { Id = "welcome", Name = "Welcome", Template = "Welcome {{name}}" });
            show.Props.Add(new ShowPropDefinition { Id = "qr", Name = "QR", AssetReference = "Media/Files/qr.png" });
            show.Macros.Add(new ShowMacroDefinition { Id = "clear", Name = "Clear", CommandIds = ["clear-slide"] });
        });
        service.UpdateSupport(support =>
        {
            support.ThemeBindings.Add(new ThemeBindingDefinition { SurfaceId = "stream", ThemeId = "lower-third" });
            support.Labels.Add(new SupportLabelDefinition { Id = "worship", Name = "Worship" });
        });

        await service.SaveAsync();

        SharedConfigService loaded = CreateService(root);
        await loaded.LoadAsync();

        loaded.Output.LogicalScreens.Should().ContainSingle(screen => screen.Id == "main");
        loaded.Output.Looks.Single().ClearGroups.Should().ContainSingle(group => group.Id == "lyrics");
        loaded.Show.Timers.Should().ContainSingle(timer => timer.Id == "countdown");
        loaded.Show.Messages.Should().ContainSingle(message => message.Id == "welcome");
        loaded.Show.Props.Should().ContainSingle(prop => prop.Id == "qr");
        loaded.Show.Macros.Should().ContainSingle(macro => macro.Id == "clear");
        loaded.Support.ThemeBindings.Should().ContainSingle(binding => binding.ThemeId == "lower-third");
        loaded.Support.Labels.Should().ContainSingle(label => label.Id == "worship");
    }

    private static SharedConfigService CreateService(string root)
    {
        return new SharedConfigService(
            TestContentPaths.Create(root).Object,
            NullLogger<SharedConfigService>.Instance);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Configurations"));
        return root;
    }
}