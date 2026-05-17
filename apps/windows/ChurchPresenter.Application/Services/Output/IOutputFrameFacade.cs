using ChurchPresenter.Backend.Rendering;

using BackendAudienceFrame = ChurchPresenter.Backend.Rendering.AudienceRenderFrame;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// UI-facing adapter that exposes one output-surface snapshot built from the synchronized backend state.
/// </summary>
public interface IOutputFrameFacade
{
    /// <summary>Raised when the adapted output snapshot changes.</summary>
    event EventHandler<OutputFrameChangedEventArgs>? Changed;

    /// <summary>The current output snapshot for the surface.</summary>
    OutputFrameSnapshot Current { get; }
}

/// <summary>Adapted output snapshot used by WinUI view models.</summary>
public sealed record OutputFrameSnapshot
{
    /// <summary>An empty snapshot.</summary>
    public static readonly OutputFrameSnapshot Empty = new();

    /// <summary>The logical screen/feed id, or <c>null</c> for the shared program preview.</summary>
    public string? ScreenId { get; init; }

    /// <summary>Resolved legacy-compatible render frame for the surface.</summary>
    public RenderFrame Frame { get; init; } = RenderFrame.Empty;

    /// <summary>Resolved legacy-compatible scene for the surface.</summary>
    public OutputScene Scene { get; init; } = OutputScene.Empty;

    /// <summary>Operator-facing program title.</summary>
    public string ProgramTitle { get; init; } = string.Empty;

    /// <summary>Current routed backend frame for this surface, when present.</summary>
    public BackendAudienceFrame? AudienceFrame { get; init; }

    /// <summary>Current output diagnostics for this surface, when it maps to a logical screen.</summary>
    public OutputScreenDiagnostics? ScreenDiagnostics { get; init; }

    /// <summary>Resolved backend layer routing flags for this output surface.</summary>
    public IReadOnlyList<OutputLayerRouteState> LayerRoutes { get; init; } = Array.Empty<OutputLayerRouteState>();

    /// <summary>Whether presentation content is routed to this surface.</summary>
    public bool RoutesPresentation { get; init; } = true;

    /// <summary>Whether media content is routed to this surface.</summary>
    public bool RoutesMedia { get; init; } = true;
}

/// <summary>Event args for <see cref="IOutputFrameFacade.Changed"/>.</summary>
public sealed class OutputFrameChangedEventArgs : EventArgs
{
    /// <summary>The updated output snapshot.</summary>
    public OutputFrameSnapshot Snapshot { get; init; } = OutputFrameSnapshot.Empty;
}