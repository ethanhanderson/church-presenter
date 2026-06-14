#nullable enable

namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// A selectable live video input source (local camera, capture card, or future NDI/SDI feed).
/// </summary>
public sealed record LiveInputSource
{
    /// <summary>Stable source id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Fit/crop/stretch policy applied when compositing the source.</summary>
    public string Fit { get; init; } = "cover";

    /// <summary>Whether the source carries an audio track.</summary>
    public bool HasAudio { get; init; }
}

/// <summary>
/// Resolves live video input sources for the <see cref="CueKind.LiveVideo"/> prepare path.
/// </summary>
public interface ILiveInputCatalog
{
    /// <summary>Resolves a source by id, or null when no such source exists.</summary>
    LiveInputSource? Resolve(string sourceId);

    /// <summary>Lists all known live input sources.</summary>
    IReadOnlyList<LiveInputSource> List();
}

/// <summary>
/// In-memory v1 live input catalog. Exposes a single default camera source so the live-video
/// cue prepare path is wired and testable; physical-device enumeration (MediaCapture /
/// MediaFrameSourceGroup) and NDI/SDI sources attach to this catalog later.
/// </summary>
public sealed class LiveInputCatalog : ILiveInputCatalog
{
    /// <summary>Stable id of the default local camera source.</summary>
    public const string DefaultCameraId = "camera:default";

    private readonly Dictionary<string, LiveInputSource> _sources;

    /// <summary>Initializes the catalog with the built-in v1 sources.</summary>
    public LiveInputCatalog()
    {
        _sources = new Dictionary<string, LiveInputSource>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultCameraId] = new LiveInputSource
            {
                Id = DefaultCameraId,
                DisplayName = "Default Camera",
                Fit = "cover",
                HasAudio = false,
            },
        };
    }

    /// <inheritdoc />
    public LiveInputSource? Resolve(string sourceId) =>
        string.IsNullOrWhiteSpace(sourceId)
            ? null
            : _sources.GetValueOrDefault(sourceId.Trim());

    /// <inheritdoc />
    public IReadOnlyList<LiveInputSource> List() => _sources.Values.ToArray();
}
