using System.Text.Json;

using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Settings;

public sealed class MachineStateServiceTests
{
    [Fact]
    public async Task SaveAsync_excludes_portable_looks_from_machine_local_output_binding()
    {
        string root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        var service = new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance);

        service.UpdateOutputBinding(binding =>
        {
            binding.AudienceMonitorIds.Add("1");
            binding.ActiveLookId = OutputLookIds.Custom;
            binding.Looks.Add(new OutputLookDefinition { Id = OutputLookIds.Custom, Name = "Custom" });
        });

        await service.SaveAsync();

        string json = await File.ReadAllTextAsync(paths.Object.GetMachineStatePath("OutputBinding"));
        using JsonDocument document = JsonDocument.Parse(json);

        document.RootElement.TryGetProperty("audienceMonitorIds", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("activeLookId", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("looks", out _).Should().BeFalse(
            "Looks are portable support settings stored in Configurations/Output.json");
    }

    [Fact]
    public async Task SaveAsync_persists_machine_local_bindings_credentials_caches_and_diagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        var service = new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance);

        service.UpdateDeviceBindings(bindings =>
        {
            bindings.AudioOutputDeviceIds.Add("speakers-1");
            bindings.VideoInputDeviceIds.Add("camera-1");
            bindings.CommunicationDeviceBindings["atem"] = "usb-1";
        });
        service.UpdateCredentials(credentials => credentials.CredentialRefsByIntegration["planning-center"] = "windows-credential://planning-center");
        service.UpdateCaches(caches =>
        {
            caches.MediaSearchRoots.Add("D:\\Media");
            caches.RecentRelinkHints.Add("D:\\Media\\song.mp4");
        });
        service.UpdateDiagnostics(diagnostics =>
        {
            diagnostics.LastKnownMonitorIds.Add("monitor-1");
            diagnostics.LastMessages.Add("Endpoint missing");
        });

        await service.SaveAsync();

        File.Exists(paths.Object.GetMachineStatePath("DeviceBindings")).Should().BeTrue();
        File.Exists(paths.Object.GetMachineStatePath("Credentials")).Should().BeTrue();
        File.Exists(paths.Object.GetMachineStatePath("Caches")).Should().BeTrue();
        File.Exists(paths.Object.GetMachineStatePath("Diagnostics")).Should().BeTrue();

        var loaded = new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance);
        await loaded.LoadAsync();
        loaded.DeviceBindings.AudioOutputDeviceIds.Should().ContainSingle(id => id == "speakers-1");
        loaded.Credentials.CredentialRefsByIntegration.Should().ContainKey("planning-center");
        loaded.Caches.MediaSearchRoots.Should().ContainSingle(rootPath => rootPath == "D:\\Media");
        loaded.Diagnostics.LastKnownMonitorIds.Should().ContainSingle(id => id == "monitor-1");
    }
}