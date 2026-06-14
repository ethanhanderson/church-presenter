#nullable enable

using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Pipeline.HostAdapter;

/// <summary>
/// Shared host adapter surface lifecycle contract.
/// </summary>
public interface IHostAdapter
{
    /// <summary>Raised when host health changes.</summary>
    event EventHandler<HostHealthEvent>? HostHealthRaised;

    /// <summary>Creates or reconciles persistent per-layer surfaces.</summary>
    Task EnsureSurfacesAsync(ScreenConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>Tears down host surfaces and players.</summary>
    Task ShutdownAsync(string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies audience frames to native composition surfaces.
/// </summary>
public interface IAudienceHostAdapter : IHostAdapter
{
    /// <summary>Applies one audience frame diff per layer slot.</summary>
    Task ApplyFrameAsync(AudienceRenderFrame frame, CancellationToken cancellationToken = default);

    /// <summary>Binds a media player to the requested layer slot.</summary>
    Task<IMediaSlotBinding?> BindMediaSlotAsync(LayerSlotId slotId, RenderPayloadDescriptor payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies stage frames to native composition surfaces.
/// </summary>
public interface IStageHostAdapter : IHostAdapter
{
    /// <summary>Applies one stage frame diff.</summary>
    Task ApplyFrameAsync(StageRenderFrame frame, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies lightweight thumbnail render intents.
/// </summary>
public interface IThumbnailHostAdapter : IHostAdapter
{
    /// <summary>Applies one thumbnail scene snapshot.</summary>
    Task ApplySceneAsync(SlideScene scene, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies editor canvas scenes with host-only adorners.
/// </summary>
public interface IEditorHostAdapter : IHostAdapter
{
    /// <summary>Applies one editor scene snapshot.</summary>
    Task ApplySceneAsync(SlideScene scene, EditorHostOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies reflow preview scenes.
/// </summary>
public interface IReflowPreviewHostAdapter : IHostAdapter
{
    /// <summary>Applies one reflow preview scene snapshot.</summary>
    Task ApplySceneAsync(SlideScene scene, CancellationToken cancellationToken = default);
}
