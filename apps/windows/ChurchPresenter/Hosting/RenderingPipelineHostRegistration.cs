#nullable enable

using ChurchPresenter.Adapters.Media.Pipeline;
using ChurchPresenter.Adapters.Rendering.Pipeline;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Controls.Rendering.Engine;

using Microsoft.Extensions.DependencyInjection;

namespace ChurchPresenter.Hosting;

internal static class RenderingPipelineHostRegistration
{
    internal static IServiceCollection AddRenderingPipelineHost(this IServiceCollection services)
    {
        // One app-lifetime render service owns the single canonical render per logical screen and serves
        // as both the audience and stage host adapter. Audience windows, stage windows, and the operator
        // preview are all passive CompositionVisualSurface mirrors of these renders (IProgramRenderService),
        // so there is no per-target render duplication and no page-owned composition tree.
        services.AddSingleton<WinUiMediaSlotRegistry>();

        services.AddSingleton<ProgramRenderService>();
        services.AddSingleton<IProgramRenderService>(static sp => sp.GetRequiredService<ProgramRenderService>());
        services.AddSingleton<IAudienceHostAdapter>(static sp => sp.GetRequiredService<ProgramRenderService>());
        services.AddSingleton<IStageHostAdapter>(static sp => sp.GetRequiredService<ProgramRenderService>());
        services.AddSingleton<OperatorPreviewSession>();

        // Thumbnails, editor canvas, reflow, and theme previews render through ThumbnailEngine /
        // SceneCanvasView on the shared rendering core rather than dedicated host adapters.
        services.AddSingleton<ThumbnailEngine>();

        services.AddSingleton<WinUiAudioRoutingAdapter>();
        services.AddSingleton<WinUiLiveVideoAdapter>();

        return services;
    }
}
