namespace ChurchPresenter.ViewModels;

/// <summary>Output view model for the audience feed.</summary>
public sealed class AudienceOutputViewModel(
    Adapters.Rendering.Pipeline.IProgramRenderService renderService,
    Services.Pipeline.CuePipelineRuntime pipeline)
{
    private readonly Adapters.Rendering.Pipeline.IProgramRenderService _renderService =
        renderService ?? throw new ArgumentNullException(nameof(renderService));

    private readonly Services.Pipeline.CuePipelineRuntime _pipeline =
        pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <summary>Points the audience window's mirror at its logical screen's canonical render.</summary>
    public Task AttachPreviewAsync(
        Controls.Rendering.Engine.MirrorSurfaceView mirror,
        string screenId = "audience",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        mirror.Bind(_renderService, screenId);
        // Nudge a compose so the canonical render for this screen is created if it does not exist yet
        // (the mirror rebinds via ScreensChanged once the surface appears).
        return _pipeline.InvalidateTransitionDefaultsAsync(cancellationToken);
    }

    /// <summary>Resolves the native render width for the logical audience screen.</summary>
    public double ResolveFrameWidth(string screenId) =>
        ResolveScreen(screenId)?.RenderSize.Width ?? 1920;

    /// <summary>Resolves the native render height for the logical audience screen.</summary>
    public double ResolveFrameHeight(string screenId) =>
        ResolveScreen(screenId)?.RenderSize.Height ?? 1080;

    private global::ChurchPresenter.Backend.Output.OutputScreen? ResolveScreen(string screenId) =>
        _pipeline.Snapshot.SessionState.Screens.TryGetValue(screenId, out global::ChurchPresenter.Backend.Output.OutputScreen? screen)
            ? screen
            : null;
}

/// <summary>Output view model for the stage feed.</summary>
public sealed class StageOutputViewModel(
    Adapters.Rendering.Pipeline.IProgramRenderService renderService,
    Services.Pipeline.CuePipelineRuntime pipeline)
{
    private readonly Adapters.Rendering.Pipeline.IProgramRenderService _renderService =
        renderService ?? throw new ArgumentNullException(nameof(renderService));

    private readonly Services.Pipeline.CuePipelineRuntime _pipeline =
        pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <summary>Points the stage window's mirror at its logical screen's canonical render.</summary>
    public Task AttachPreviewAsync(
        Controls.Rendering.Engine.MirrorSurfaceView mirror,
        string screenId = "stage",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        mirror.Bind(_renderService, screenId);
        return _pipeline.InvalidateTransitionDefaultsAsync(cancellationToken);
    }

    /// <summary>Resolves the native render width for the logical stage screen.</summary>
    public double ResolveFrameWidth(string screenId) =>
        ResolveScreen(screenId)?.RenderSize.Width ?? 1920;

    /// <summary>Resolves the native render height for the logical stage screen.</summary>
    public double ResolveFrameHeight(string screenId) =>
        ResolveScreen(screenId)?.RenderSize.Height ?? 1080;

    private global::ChurchPresenter.Backend.Output.OutputScreen? ResolveScreen(string screenId) =>
        _pipeline.Snapshot.SessionState.Screens.TryGetValue(screenId, out global::ChurchPresenter.Backend.Output.OutputScreen? screen)
            ? screen
            : null;
}
