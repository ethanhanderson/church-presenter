namespace ChurchPresenter.App.Tests.Reference;

/// <summary>
/// Trait categories aligned with architecture and ProPresenter reference docs.
/// Filter in Test Explorer: <c>Trait=ReferenceArea&amp;Value=ContentManagement</c>.
/// </summary>
public static class ReferenceAreas
{
    public const string TraitName = "ReferenceArea";

    /// <summary><c>docs/architecture/content-management.md</c> — libraries, playlists, graph-safe mutations.</summary>
    public const string ContentManagement = "ContentManagement";

    /// <summary><c>docs/architecture/backend-application.md</c> — command bus, layer routing, live session.</summary>
    public const string LivePipeline = "LivePipeline";

    /// <summary><c>docs/reference/propresenter/output-system/layers-and-looks.md</c> — Looks, clears, stage vs audience.</summary>
    public const string OutputRouting = "OutputRouting";

    /// <summary><c>docs/architecture/file-access-and-cache-resilience.md</c> — stamps, invalidation, relink.</summary>
    public const string CacheAndRecovery = "CacheAndRecovery";

    /// <summary><c>docs/reference/propresenter/features/media-audio-and-live-inputs.md</c> — cues, playback, relink.</summary>
    public const string MediaCues = "MediaCues";

    /// <summary><c>docs/reference/propresenter/operator-workflows/show-controls-and-recovery.md</c> — Show operator semantics.</summary>
    public const string ShowWorkflow = "ShowWorkflow";

    /// <summary>Scene compile, transparency preview, frame composition.</summary>
    public const string Rendering = "Rendering";

    public static IReadOnlyList<string> All { get; } =
    [
        ContentManagement,
        LivePipeline,
        OutputRouting,
        CacheAndRecovery,
        MediaCues,
        ShowWorkflow,
        Rendering,
    ];
}
