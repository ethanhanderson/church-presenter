using System.IO.Compression;

using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Support;

public sealed class SupportPackageServiceTests
{
    [Fact]
    public async Task ExportAsync_packages_configurations_without_machine_state()
    {
        string root = CreateRoot();
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        var sharedConfig = new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance);
        sharedConfig.UpdateOutput(output =>
        {
            output.Looks.Add(new OutputLookDefinition { Id = OutputLookIds.Custom, Name = "Custom" });
        });
        await sharedConfig.SaveAsync();

        var machineState = new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance);
        machineState.UpdateOutputBinding(binding => binding.AudienceMonitorIds.Add("monitor-1"));
        await machineState.SaveAsync();

        string packagePath = Path.Combine(root, "support.cpsupport");
        var service = new SupportPackageService(paths.Object);
        await service.ExportAsync(packagePath);

        using var archive = ZipFile.OpenRead(packagePath);
        archive.GetEntry("Configurations/Output.json").Should().NotBeNull();
        archive.GetEntry("Configurations/Show.json").Should().NotBeNull();
        archive.Entries.Should().NotContain(entry => entry.FullName.StartsWith("MachineState/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreviewImportAsync_reports_destructive_replaces_and_import_requires_opt_in()
    {
        string sourceRoot = CreateRoot();
        var sourcePaths = TestContentPaths.Create(sourceRoot);
        await sourcePaths.Object.EnsureDocumentsLayoutAsync();
        await File.WriteAllTextAsync(sourcePaths.Object.GetSharedConfigPath("Show"), """{"schemaVersion":3,"defaultCenterView":"media"}""");
        string packagePath = Path.Combine(sourceRoot, "support.cpsupport");
        await new SupportPackageService(sourcePaths.Object).ExportAsync(packagePath);

        string importRoot = CreateRoot();
        var importPaths = TestContentPaths.Create(importRoot);
        await importPaths.Object.EnsureDocumentsLayoutAsync();
        await File.WriteAllTextAsync(importPaths.Object.GetSharedConfigPath("Show"), """{"schemaVersion":3,"defaultCenterView":"slides"}""");
        var importer = new SupportPackageService(importPaths.Object);

        SupportPackagePreview preview = await importer.PreviewImportAsync(packagePath);

        preview.Changes.Should().Contain(change =>
            change.Kind == SupportPackageChangeKind.Replace
            && change.Path == "Configurations/Show.json"
            && change.IsDestructive);
        Func<Task> blockedImport = () => importer.ImportAsync(packagePath);
        await blockedImport.Should().ThrowAsync<InvalidOperationException>();

        await importer.ImportAsync(packagePath, new SupportPackageImportOptions { AllowDestructiveReplace = true });

        string importedShow = await File.ReadAllTextAsync(importPaths.Object.GetSharedConfigPath("Show"));
        importedShow.Should().Contain("media");
    }

    [Fact]
    public async Task PreviewImportAsync_warns_and_skips_machine_state_entries()
    {
        string root = CreateRoot();
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        await File.WriteAllTextAsync(paths.Object.GetSharedConfigPath("Show"), """{"schemaVersion":3}""");
        string packagePath = Path.Combine(root, "support.cpsupport");
        var service = new SupportPackageService(paths.Object);
        await service.ExportAsync(packagePath);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("MachineState/OutputBinding.json");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("""{"audienceMonitorIds":["not-portable"]}""");
        }

        SupportPackagePreview preview = await service.PreviewImportAsync(packagePath);

        preview.Changes.Should().Contain(change =>
            change.Kind == SupportPackageChangeKind.Warning
            && change.Path == "MachineState/OutputBinding.json"
            && !change.IsDestructive);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}