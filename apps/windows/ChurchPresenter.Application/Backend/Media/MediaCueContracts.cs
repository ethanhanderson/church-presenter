namespace ChurchPresenter.Backend.Media;

/// <summary>
/// Minimal transition contract reserved for media inspector overrides and future playback policies.
/// </summary>
public sealed record MediaTransition
{
    public string? TransitionId { get; init; }

    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Asset-level cue defaults that seed new media or audio cue placements.
/// </summary>
public sealed record MediaCueProfile
{
    public MediaCueRole Role { get; init; } = MediaCueRole.Background;

    public MediaScalingMode Scaling { get; init; } = MediaScalingMode.ScaleToFill;

    public MediaPlaybackMode PlaybackMode { get; init; } = MediaPlaybackMode.Stop;

    public bool AutoPlay { get; init; } = true;

    /// <summary>
    /// Optional explicit retrigger policy. When unset, ProPresenter-style role/playback rules apply.
    /// </summary>
    public bool? Retrigger { get; init; }

    public TimeSpan? Duration { get; init; }

    public TimeSpan? InPoint { get; init; }

    public TimeSpan? OutPoint { get; init; }

    public TimeSpan? Delay { get; init; }

    public double PlaybackRate { get; init; } = 1d;

    public MediaCropRegion? Crop { get; init; }

    public MediaTransition? Transition { get; init; }

    public MediaEffectSettings Effects { get; init; } = new();

    public bool Muted { get; init; }

    public double Volume { get; init; } = 1d;

    public AudioRoutingMetadata AudioRouting { get; init; } = new();

    public bool ResolveRetriggerBehavior()
    {
        if (Retrigger.HasValue)
            return Retrigger.Value;

        return Role switch
        {
            MediaCueRole.Foreground => true,
            MediaCueRole.Background when PlaybackMode == MediaPlaybackMode.Loop => false,
            _ => true,
        };
    }

    public MediaCueProfile Apply(MediaCueOverride? overrides)
    {
        if (overrides is null)
            return this;

        return this with
        {
            Role = overrides.Role ?? Role,
            Scaling = overrides.Scaling ?? Scaling,
            PlaybackMode = overrides.PlaybackMode ?? PlaybackMode,
            AutoPlay = overrides.AutoPlay ?? AutoPlay,
            Retrigger = overrides.Retrigger ?? Retrigger,
            Duration = overrides.Duration ?? Duration,
            InPoint = overrides.InPoint ?? InPoint,
            OutPoint = overrides.OutPoint ?? OutPoint,
            Delay = overrides.Delay ?? Delay,
            PlaybackRate = overrides.PlaybackRate ?? PlaybackRate,
            Crop = overrides.Crop ?? Crop,
            Transition = overrides.Transition ?? Transition,
            Effects = overrides.Effects ?? Effects,
            Muted = overrides.Muted ?? Muted,
            Volume = overrides.Volume ?? Volume,
            AudioRouting = overrides.AudioRouting ?? AudioRouting,
        };
    }
}

/// <summary>
/// Cue-local overrides kept separate from the owning asset defaults.
/// </summary>
public sealed record MediaCueOverride
{
    public MediaCueRole? Role { get; init; }

    public MediaScalingMode? Scaling { get; init; }

    public MediaPlaybackMode? PlaybackMode { get; init; }

    public bool? AutoPlay { get; init; }

    public bool? Retrigger { get; init; }

    public TimeSpan? Duration { get; init; }

    public TimeSpan? InPoint { get; init; }

    public TimeSpan? OutPoint { get; init; }

    public TimeSpan? Delay { get; init; }

    public double? PlaybackRate { get; init; }

    public MediaCropRegion? Crop { get; init; }

    public MediaTransition? Transition { get; init; }

    public MediaEffectSettings? Effects { get; init; }

    public bool? Muted { get; init; }

    public double? Volume { get; init; }

    public AudioRoutingMetadata? AudioRouting { get; init; }
}

/// <summary>
/// Placement of an asset into a slide, theme, macro, playlist, or automation surface.
/// </summary>
public sealed record MediaCue
{
    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? OwnerReferenceId { get; init; }

    public MediaCueOverride Overrides { get; init; } = new();

    public ResolvedMediaCue Resolve(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!string.Equals(AssetId, asset.AssetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cue '{CueId}' targets asset '{AssetId}' but received '{asset.AssetId}'.");

        MediaCueProfile effectiveCue = asset.DefaultCue.Apply(Overrides);
        return new ResolvedMediaCue
        {
            CueId = CueId,
            AssetId = asset.AssetId,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? asset.DisplayName : DisplayName,
            AssetKind = asset.Kind,
            OwnerReferenceId = OwnerReferenceId,
            ResolvedPath = asset.ResolvedPath,
            Availability = asset.Availability,
            EffectiveCue = effectiveCue,
            RetriggersOnTake = effectiveCue.ResolveRetriggerBehavior(),
        };
    }
}

/// <summary>
/// Fully resolved cue used by future playback, command, and UI-facade layers.
/// </summary>
public sealed record ResolvedMediaCue
{
    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MediaAssetKind AssetKind { get; init; }

    public string? OwnerReferenceId { get; init; }

    public string? ResolvedPath { get; init; }

    public MediaAvailability Availability { get; init; } = MediaAvailability.Available();

    public MediaCueProfile EffectiveCue { get; init; } = new();

    public bool RetriggersOnTake { get; init; }

    public bool IsPlayable =>
        Availability.IsPlayable
        && (AssetKind == MediaAssetKind.LiveVideoInput || !string.IsNullOrWhiteSpace(ResolvedPath));
}

/// <summary>
/// Audio-specific cue placement with the same override model as media cues.
/// </summary>
public sealed record AudioCue
{
    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? OwnerReferenceId { get; init; }

    public MediaCueOverride Overrides { get; init; } = new()
    {
        Role = MediaCueRole.Foreground,
        Scaling = MediaScalingMode.ScaleToFit,
    };

    public ResolvedAudioCue Resolve(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!string.Equals(AssetId, asset.AssetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Audio cue '{CueId}' targets asset '{AssetId}' but received '{asset.AssetId}'.");

        if (asset.Kind != MediaAssetKind.Audio)
            throw new InvalidOperationException($"Audio cue '{CueId}' requires an audio asset but received '{asset.Kind}'.");

        MediaCueProfile effectiveCue = asset.DefaultCue.Apply(Overrides);
        return new ResolvedAudioCue
        {
            CueId = CueId,
            AssetId = asset.AssetId,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? asset.DisplayName : DisplayName,
            OwnerReferenceId = OwnerReferenceId,
            ResolvedPath = asset.ResolvedPath,
            Availability = asset.Availability,
            EffectiveCue = effectiveCue,
            RetriggersOnTake = effectiveCue.ResolveRetriggerBehavior(),
        };
    }
}

/// <summary>
/// Fully resolved audio cue used by playback coordination and diagnostics.
/// </summary>
public sealed record ResolvedAudioCue
{
    public string CueId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? OwnerReferenceId { get; init; }

    public string? ResolvedPath { get; init; }

    public MediaAvailability Availability { get; init; } = MediaAvailability.Available();

    public MediaCueProfile EffectiveCue { get; init; } = new();

    public bool RetriggersOnTake { get; init; }

    public bool IsPlayable => Availability.IsPlayable && !string.IsNullOrWhiteSpace(ResolvedPath);
}