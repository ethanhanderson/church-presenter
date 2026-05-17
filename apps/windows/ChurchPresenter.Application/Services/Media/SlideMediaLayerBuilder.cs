
namespace ChurchPresenter.Services.Media;

/// <summary>
/// Builds audience media-layer state from slide media cues for preview and live rendering.
/// </summary>
public static class SlideMediaLayerBuilder
{
    /// <summary>
    /// Builds media-layer state from a typed presentation slide.
    /// </summary>
    public static MediaLayersState Build(PresentationSlide? slide)
    {
        if (slide?.MediaCues == null)
            return new MediaLayersState();

        return BuildFromCues(slide.MediaCues.Select(c => new NormalizedCue(
            c.MediaId, c.MediaType, c.DisplayName, c.Target, c.Fit, c.Loop, c.Muted, c.Autoplay, c.Transition)));
    }

    /// <summary>
    /// Builds media-layer state from a lightweight presentation DTO slide.
    /// </summary>
    public static MediaLayersState Build(SlideDto? slide)
    {
        if (slide?.MediaCues == null)
            return new MediaLayersState();

        return BuildFromCues(slide.MediaCues.Select(c => new NormalizedCue(
            c.MediaId, c.MediaType, c.DisplayName, c.Target, c.Fit, c.Loop, c.Muted, c.Autoplay, c.Transition)));
    }

    /// <summary>
    /// Returns a cloned base state with slide cues layered on top.
    /// </summary>
    public static MediaLayersState Merge(MediaLayersState? baseState, PresentationSlide? slide)
    {
        var merged = Clone(baseState);
        if (slide?.MediaCues == null)
            return merged;

        foreach (var cue in slide.MediaCues)
        {
            var target = MapCueTarget(cue.Target);
            var layer = BuildLayerFromNormalizedCue(new NormalizedCue(
                cue.MediaId, cue.MediaType, cue.DisplayName, cue.Target, cue.Fit, cue.Loop, cue.Muted, cue.Autoplay, cue.Transition));
            AssignLayer(merged, target, layer);
        }

        return merged;
    }

    /// <summary>
    /// Clones the current media-layer state.
    /// </summary>
    public static MediaLayersState Clone(MediaLayersState? source)
    {
        return new MediaLayersState
        {
            MediaUnderlay = source?.MediaUnderlay == null ? null : CloneLayer(source.MediaUnderlay),
            MediaOverlay = source?.MediaOverlay == null ? null : CloneLayer(source.MediaOverlay),
            Audio = source?.Audio == null ? null : CloneLayer(source.Audio),
        };
    }

    /// <summary>
    /// Returns a cloned base state with non-null overlay layers replacing matching base layers.
    /// </summary>
    public static MediaLayersState Overlay(MediaLayersState? baseState, MediaLayersState? overlayState)
    {
        var merged = Clone(baseState);
        if (overlayState == null)
            return merged;

        if (overlayState.MediaUnderlay != null)
            merged.MediaUnderlay = CloneLayer(overlayState.MediaUnderlay);
        if (overlayState.MediaOverlay != null)
            merged.MediaOverlay = CloneLayer(overlayState.MediaOverlay);
        if (overlayState.Audio != null)
            merged.Audio = CloneLayer(overlayState.Audio);

        return merged;
    }

    /// <summary>
    /// Normalizes legacy cue target names to the current underlay/overlay model.
    /// </summary>
    public static string MapCueTarget(string? target)
    {
        return target switch
        {
            "slideBackgroundMedia" => "mediaUnderlay",
            "slideForegroundMedia" => "mediaOverlay",
            _ => string.IsNullOrWhiteSpace(target) ? "mediaUnderlay" : target,
        };
    }

    private static void AssignLayer(MediaLayersState state, string target, OutputLayerMedia layer)
    {
        switch (target)
        {
            case "mediaUnderlay":
                state.MediaUnderlay = layer;
                break;
            case "mediaOverlay":
                state.MediaOverlay = layer;
                break;
            case "audio":
                state.Audio = layer;
                break;
        }
    }

    private static OutputLayerMedia CloneLayer(OutputLayerMedia layer)
    {
        return new OutputLayerMedia
        {
            MediaId = layer.MediaId,
            MediaType = layer.MediaType,
            DisplayName = layer.DisplayName,
            Fit = layer.Fit,
            Loop = layer.Loop,
            Muted = layer.Muted,
            Autoplay = layer.Autoplay,
            Transition = CloneTransition(layer.Transition),
            ResolvedSourcePath = layer.ResolvedSourcePath,
        };
    }

    /// <summary>
    /// Ordinal-ignore-case equality for playback identity (paths, ids, and flags).
    /// </summary>
    public static bool OutputLayerMediaEquals(OutputLayerMedia? a, OutputLayerMedia? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;

        static string Norm(string? s) => s?.Trim() ?? "";

        return string.Equals(Norm(a.MediaId), Norm(b.MediaId), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Norm(a.MediaType), Norm(b.MediaType), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Norm(a.DisplayName), Norm(b.DisplayName), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Norm(a.Fit), Norm(b.Fit), StringComparison.OrdinalIgnoreCase)
            && a.Loop == b.Loop
            && a.Muted == b.Muted
            && a.Autoplay == b.Autoplay
            && TransitionsEqual(a.Transition, b.Transition)
            && string.Equals(Norm(a.ResolvedSourcePath), Norm(b.ResolvedSourcePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when program <paramref name="current"/> layers differ from slide media cues alone
    /// (e.g. operator / media-panel layers are active).
    /// </summary>
    public static bool HasProgramMediaBeyondSlideCues(SlideDto? slide, MediaLayersState? current)
    {
        current ??= new MediaLayersState();
        return !MediaLayersStateEquals(Build(slide), current);
    }

    /// <summary>
    /// True when underlay, overlay, and audio slots match pairwise.
    /// </summary>
    public static bool MediaLayersStateEquals(MediaLayersState? a, MediaLayersState? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        a ??= new MediaLayersState();
        b ??= new MediaLayersState();
        return OutputLayerMediaEquals(a.MediaUnderlay, b.MediaUnderlay)
            && OutputLayerMediaEquals(a.MediaOverlay, b.MediaOverlay)
            && OutputLayerMediaEquals(a.Audio, b.Audio);
    }

    // ── Shared internal normalization path ────────────────────────────────────

    /// <summary>
    /// Carrier record used to bridge typed and DTO cue fields through the shared build path.
    /// </summary>
    private readonly record struct NormalizedCue(
        string MediaId,
        string MediaType,
        string? DisplayName,
        string? Target,
        string? Fit,
        bool? Loop,
        bool? Muted,
        bool? Autoplay,
        SlideTransition? Transition);

    private static MediaLayersState BuildFromCues(IEnumerable<NormalizedCue> cues)
    {
        var state = new MediaLayersState();
        foreach (var cue in cues)
        {
            var target = MapCueTarget(cue.Target);
            AssignLayer(state, target, BuildLayerFromNormalizedCue(cue));
        }
        return state;
    }

    private static OutputLayerMedia BuildLayerFromNormalizedCue(NormalizedCue cue) => new()
    {
        MediaId = cue.MediaId,
        MediaType = cue.MediaType,
        DisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName),
        Fit = cue.Fit,
        Loop = cue.Loop ?? false,
        Muted = cue.Muted ?? false,
        Autoplay = cue.Autoplay ?? false,
        Transition = CloneTransition(cue.Transition),
    };

    private static SlideTransition? CloneTransition(SlideTransition? transition)
    {
        if (transition == null)
            return null;

        return new SlideTransition
        {
            Type = transition.Type,
            Duration = transition.Duration,
            Easing = transition.Easing,
            Parameters = transition.Parameters == null
                ? null
                : new Dictionary<string, string>(transition.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static bool TransitionsEqual(SlideTransition? a, SlideTransition? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return a is null && b is null;

        if (!string.Equals(a.Type?.Trim(), b.Type?.Trim(), StringComparison.OrdinalIgnoreCase)
            || a.Duration != b.Duration
            || !string.Equals(a.Easing?.Trim(), b.Easing?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var aParameters = a.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bParameters = b.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (aParameters.Count != bParameters.Count)
            return false;

        foreach (var pair in aParameters)
        {
            if (!bParameters.TryGetValue(pair.Key, out var otherValue))
                return false;

            if (!string.Equals(pair.Value?.Trim(), otherValue?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}