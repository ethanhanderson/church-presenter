namespace ChurchPresenter.Backend.Media;

/// <summary>
/// Platform-neutral target owned by the media/audio playback coordinator.
/// </summary>
public enum MediaPlaybackLayerTarget
{
    MediaUnderlay,
    MediaOverlay,
    Audio,
}

/// <summary>
/// Declares whether a playback surface is authoritative for shared transport state.
/// </summary>
public enum MediaPlaybackAuthority
{
    Mirror,
    Authority,
}

/// <summary>
/// Layer-target helper methods shared by cue preparation and diagnostics.
/// </summary>
public static class MediaPlaybackLayerTargetNames
{
    public const string MediaUnderlay = "mediaUnderlay";
    public const string MediaOverlay = "mediaOverlay";
    public const string Audio = "audio";

    public static string ToLayerName(MediaPlaybackLayerTarget target) =>
        target switch
        {
            MediaPlaybackLayerTarget.MediaOverlay => MediaOverlay,
            MediaPlaybackLayerTarget.Audio => Audio,
            _ => MediaUnderlay,
        };

    public static MediaPlaybackLayerTarget FromLayerName(string? target) =>
        target switch
        {
            MediaOverlay => MediaPlaybackLayerTarget.MediaOverlay,
            Audio => MediaPlaybackLayerTarget.Audio,
            _ => MediaPlaybackLayerTarget.MediaUnderlay,
        };
}

/// <summary>
/// Immutable request for a media host to own playback for one resolved cue on one output layer.
/// </summary>
public sealed record MediaPlaybackRequest
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MediaAssetKind AssetKind { get; init; }

    public MediaPlaybackLayerTarget LayerTarget { get; init; } = MediaPlaybackLayerTarget.MediaUnderlay;

    public string? ResolvedPath { get; init; }

    public MediaAvailability Availability { get; init; } = MediaAvailability.Available();

    public MediaCueProfile EffectiveCue { get; init; } = new();

    public bool IsPlayable { get; init; }

    public string LayerName => MediaPlaybackLayerTargetNames.ToLayerName(LayerTarget);

    public static MediaPlaybackRequest FromResolvedCue(
        ResolvedMediaCue cue,
        MediaPlaybackLayerTarget layerTarget)
    {
        ArgumentNullException.ThrowIfNull(cue);

        return new MediaPlaybackRequest
        {
            CueId = cue.CueId,
            AssetId = cue.AssetId,
            DisplayName = cue.DisplayName,
            AssetKind = cue.AssetKind,
            LayerTarget = layerTarget,
            ResolvedPath = cue.ResolvedPath,
            Availability = cue.Availability,
            EffectiveCue = cue.EffectiveCue,
            IsPlayable = cue.IsPlayable,
        };
    }

    public static MediaPlaybackRequest FromResolvedAudioCue(ResolvedAudioCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);

        return new MediaPlaybackRequest
        {
            CueId = cue.CueId,
            AssetId = cue.AssetId,
            DisplayName = cue.DisplayName,
            AssetKind = MediaAssetKind.Audio,
            LayerTarget = MediaPlaybackLayerTarget.Audio,
            ResolvedPath = cue.ResolvedPath,
            Availability = cue.Availability,
            EffectiveCue = cue.EffectiveCue,
            IsPlayable = cue.IsPlayable,
        };
    }
}

/// <summary>
/// Transport-state snapshot that avoids depending on WinUI or Windows media-player types.
/// </summary>
public sealed record MediaPlaybackCoordinationSnapshot
{
    public MediaPlaybackRequest? ActiveRequest { get; init; }

    public MediaPlaybackAuthority Authority { get; init; } = MediaPlaybackAuthority.Mirror;

    public int ActivePlayerCount { get; init; }

    public bool IsPlaying { get; init; }

    public TimeSpan Position { get; init; }

    public TimeSpan Duration { get; init; }

    public bool HasActiveCue => ActiveRequest is not null && ActivePlayerCount > 0;
}