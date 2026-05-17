
using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Settings;

public sealed class SettingsServiceTests
{
    // ── Factory helpers ───────────────────────────────────────────────────────

    private static (SettingsService svc, string root) CreateService()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (CreateService(root), root);
    }

    private static SettingsService CreateService(string root)
    {
        var paths = BuildPaths(root);
        var machineState = new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance);
        var sharedConfig = new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance);
        return new SettingsService(paths.Object, sharedConfig, machineState, NullLogger<SettingsService>.Instance);
    }

    private static Mock<IContentDirectoryService> BuildPaths(string root)
    {
        var machineStateDir = Path.Combine(root, "MachineState");
        var configDir = Path.Combine(root, "Configurations");
        Directory.CreateDirectory(machineStateDir);
        Directory.CreateDirectory(configDir);

        var paths = new Mock<IContentDirectoryService>();
        paths.Setup(p => p.GetAppDataDirectory()).Returns(root);
        paths.Setup(p => p.GetMachineStateDirectory()).Returns(machineStateDir);
        paths.Setup(p => p.GetMachineStatePath(It.IsAny<string>()))
             .Returns((string name) => Path.Combine(machineStateDir, $"{name}.json"));
        paths.Setup(p => p.GetConfigurationsDirectory()).Returns(configDir);
        paths.Setup(p => p.GetSharedConfigPath(It.IsAny<string>()))
             .Returns((string name) => Path.Combine(configDir, $"{name}.json"));
        paths.Setup(p => p.GetConfigurationsManifestPath())
             .Returns(Path.Combine(configDir, "Manifest.json"));
        paths.Setup(p => p.GetDocumentsDataDirectory()).Returns(Path.Combine(root, "Documents"));
        paths.Setup(p => p.GetDefaultDocumentsDataDirectory()).Returns(Path.Combine(root, "Documents"));
        return paths;
    }

    // ── Missing / invalid / null-section scenarios ────────────────────────────

    [Fact]
    public async Task LoadAsync_missing_file_uses_defaults()
    {
        var (svc, _) = CreateService();
        await svc.LoadAsync();

        svc.Settings.Theme.Should().Be("system");
    }

    [Fact]
    public async Task LoadAsync_invalid_json_uses_defaults()
    {
        var (svc, root) = CreateService();
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "{ not valid json");

        await svc.LoadAsync();

        svc.Settings.Theme.Should().Be("system");
        svc.Settings.Show.DefaultCenterView.Should().Be("slides");
        svc.Settings.Output.AudienceMonitorIds.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_null_sections_coalesces_defaults()
    {
        var (svc, root) = CreateService();

        var payload = """
            {
              "output": null,
              "editor": null,
              "show": null,
              "reflow": null,
              "integrations": null,
              "recentFiles": null,
              "updates": null
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), payload);

        await svc.LoadAsync();

        svc.Settings.Output.Should().NotBeNull();
        svc.Settings.Editor.Should().NotBeNull();
        svc.Settings.Show.Should().NotBeNull();
        svc.Settings.Show.DefaultCenterView.Should().Be("slides");
        svc.Settings.Reflow.Should().NotBeNull();
        svc.Settings.Integrations.Should().NotBeNull();
        svc.Settings.Integrations.MusicManager.Should().NotBeNull();
        svc.Settings.RecentFiles.Should().NotBeNull();
        svc.Settings.Updates.Should().NotBeNull();
    }

    // ── Roundtrip persistence ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrip()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s =>
        {
            s.Output.AudienceMonitorIds.Add("0");
        });

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Output.AudienceMonitorIds.Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_both_monitor_roles()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s =>
        {
            s.Output.AudienceMonitorIds.Add("1");
            s.Output.StageMonitorIds.Add("2");
        });

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Output.AudienceMonitorIds.Should().ContainSingle().Which.Should().Be("1");
        svc2.Settings.Output.StageMonitorIds.Should().ContainSingle().Which.Should().Be("2");
    }

    // ── Legacy migration ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_legacy_output_monitorIds_migrates_to_audienceMonitorIds()
    {
        var (svc, root) = CreateService();

        // Legacy settings.json with old-style monitorIds inside the output section.
        var payload = """
            {
              "output": {
                "monitorIds": ["2"],
                "scaling": "fill",
                "aspectRatio": "4:3"
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), payload);

        await svc.LoadAsync();

        // Legacy monitorIds must be promoted to audienceMonitorIds during NormalizeLegacy.
        svc.Settings.Output.AudienceMonitorIds.Should().ContainSingle().Which.Should().Be("2");
    }

    [Fact]
    public async Task LoadAsync_migrates_legacy_monitorIds_to_audienceMonitorIds_and_clears_legacy()
    {
        var (svc, root) = CreateService();

        var payload = """
            {
              "output": {
                "monitorIds": ["3"]
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), payload);

        await svc.LoadAsync();

        svc.Settings.Output.AudienceMonitorIds.Should().ContainSingle().Which.Should().Be("3",
            "legacy monitorIds must be promoted to audienceMonitorIds");
        svc.Settings.Output.LegacyMonitorIds.Should().BeNullOrEmpty(
            "legacy field must be cleared after migration");
    }

    [Fact]
    public async Task LoadAsync_does_not_overwrite_audienceMonitorIds_when_both_fields_present()
    {
        var (svc, root) = CreateService();

        // File has both fields — audienceMonitorIds takes precedence, legacy is ignored.
        var payload = """
            {
              "output": {
                "monitorIds": ["0"],
                "audienceMonitorIds": ["1"]
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), payload);

        await svc.LoadAsync();

        svc.Settings.Output.AudienceMonitorIds.Should().ContainSingle().Which.Should().Be("1",
            "audienceMonitorIds must win when it is non-empty, even if legacyMonitorIds is also present");
    }

    // ── Guard clauses ─────────────────────────────────────────────────────────

    [Fact]
    public void Update_throws_when_mutator_null()
    {
        var (svc, _) = CreateService();

        var act = () => svc.Update(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Show deck toolbar settings ────────────────────────────────────────────

    [Fact]
    public void ShowSettingsDto_defaults_are_sensible()
    {
        var settings = new AppSettingsDto();

        settings.Show.DeckViewMode.Should().Be("thumbnail");
        settings.Show.GroupBySection.Should().BeFalse();
        settings.Show.TransparentThumbnailBackgroundEnabled.Should().BeTrue();
        settings.Show.TransparentThumbnailColor.Should().Be("#000000");
        settings.Show.DeckScaleStep.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_deck_view_mode()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s => s.Show.DeckViewMode = "list");

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Show.DeckViewMode.Should().Be("list");
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_group_by_section()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s => s.Show.GroupBySection = true);

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Show.GroupBySection.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_transparent_thumbnail_background_enabled()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s => s.Show.TransparentThumbnailBackgroundEnabled = false);

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Show.TransparentThumbnailBackgroundEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_transparent_thumbnail_color()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s => s.Show.TransparentThumbnailColor = "#1A2B3C");

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Show.TransparentThumbnailColor.Should().Be("#1A2B3C");
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_deck_scale_step()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s => s.Show.DeckScaleStep = 4);

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Show.DeckScaleStep.Should().Be(4);
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrips_global_show_transitions()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s =>
        {
            s.Show.GlobalSlideTransition = new ShowToolbarTransitionDto
            {
                Mode = "custom",
                DissolveDurationMs = 350,
                Custom = new SlideTransition
                {
                    Type = "wipe",
                    Duration = 600,
                    Parameters = new Dictionary<string, string> { ["direction"] = "fromRight" },
                },
            };
            s.Show.GlobalMediaTransition = new ShowToolbarTransitionDto
            {
                Mode = "dissolve",
                DissolveDurationMs = 450,
            };
            s.Show.FavoriteTransitions = new List<string> { "wipe", "slide" };
            s.Show.RecentTransitions = new List<string> { "zoom-in", "cut" };
        });

        await svc.SaveAsync();

        var svc2 = CreateService(root);
        await svc2.LoadAsync();

        svc2.Settings.Show.GlobalSlideTransition.Mode.Should().Be("custom");
        svc2.Settings.Show.GlobalSlideTransition.Custom.Should().NotBeNull();
        svc2.Settings.Show.GlobalSlideTransition.Custom!.Type.Should().Be("wipe");
        svc2.Settings.Show.GlobalSlideTransition.Custom.Duration.Should().Be(600);
        svc2.Settings.Show.GlobalSlideTransition.Custom.Parameters!["direction"].Should().Be("fromRight");
        svc2.Settings.Show.GlobalMediaTransition.Mode.Should().Be("dissolve");
        svc2.Settings.Show.GlobalMediaTransition.DissolveDurationMs.Should().Be(450);
        svc2.Settings.Show.FavoriteTransitions.Should().Equal("wipe", "slide");
        svc2.Settings.Show.RecentTransitions.Should().Equal("zoom-in", "cut");
    }

    [Fact]
    public async Task SaveAsync_writes_global_show_transitions_to_portable_show_config()
    {
        var (_, root) = CreateService();
        var svc = CreateService(root);
        svc.Update(s =>
        {
            s.Show.GlobalSlideTransition = new ShowToolbarTransitionDto
            {
                Mode = "cut",
                DissolveDurationMs = 200,
            };
            s.Show.GlobalMediaTransition = new ShowToolbarTransitionDto
            {
                Mode = "custom",
                Custom = new SlideTransition
                {
                    Type = "fade",
                    Duration = 500,
                    Easing = "ease-out",
                },
            };
        });

        await svc.SaveAsync();

        var showJson = await File.ReadAllTextAsync(Path.Combine(root, "Configurations", "Show.json"));

        showJson.Should().Contain("\"globalSlideTransition\"");
        showJson.Should().Contain("\"globalMediaTransition\"");
        showJson.Should().Contain("\"mode\": \"cut\"");
        showJson.Should().Contain("\"mode\": \"custom\"");
    }

    [Fact]
    public async Task LoadAsync_missing_deck_fields_uses_defaults()
    {
        var (svc, root) = CreateService();

        var payload = """
            {
              "show": {
                "defaultCenterView": "slides"
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), payload);

        await svc.LoadAsync();

        svc.Settings.Show.DeckViewMode.Should().Be("thumbnail",
            "omitted field should default to thumbnail");
        svc.Settings.Show.GroupBySection.Should().BeFalse();
        svc.Settings.Show.TransparentThumbnailBackgroundEnabled.Should().BeTrue();
        svc.Settings.Show.TransparentThumbnailColor.Should().Be("#000000");
        svc.Settings.Show.DeckScaleStep.Should().Be(2);
    }
}