using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Content;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Backend;

public sealed class ContractSurfaceTests
{
    [Fact]
    public void Backend_contracts_use_application_layer_namespaces()
    {
        Type[] canonicalContractTypes =
        [
            typeof(LiveCommand),
            typeof(ActionBatch),
            typeof(ContentPresentation),
            typeof(MediaAsset),
            typeof(OutputScreen),
            typeof(OutputLayerKind),
            typeof(StageLayout),
            typeof(SupportPackagePreview),
        ];

        canonicalContractTypes.Select(type => type.Namespace).Should().BeEquivalentTo(
            [
                "ChurchPresenter.Backend.Commands",
                "ChurchPresenter.Backend.Commands",
                "ChurchPresenter.Backend.Content",
                "ChurchPresenter.Backend.Media",
                "ChurchPresenter.Backend.Output",
                "ChurchPresenter.Backend.Rendering",
                "ChurchPresenter.Backend.Stage",
                "ChurchPresenter.Models.Support",
            ]);
    }

    [Fact]
    public void Application_assembly_does_not_publish_placeholder_contract_namespace()
    {
        typeof(LiveCommand).Assembly
            .GetExportedTypes()
            .Should()
            .NotContain(type => string.Equals(type.Namespace, "ChurchPresenter.Contracts", StringComparison.Ordinal));
    }
}
