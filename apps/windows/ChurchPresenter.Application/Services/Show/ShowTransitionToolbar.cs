
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Maps persisted <see cref="ShowToolbarTransitionDto"/> to engine <see cref="SlideTransition"/> values.
/// </summary>
public static class ShowTransitionToolbar
{
    /// <summary>
    /// Builds a normalized transition for playback, or <c>null</c> when the mode yields no animation.
    /// </summary>
    public static SlideTransition? ToSlideTransition(ShowToolbarTransitionDto? dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Mode))
            return null;

        return dto.Mode.Trim().ToLowerInvariant() switch
        {
            "cut" => new SlideTransition { Type = "cut", Duration = 0 },
            "custom" => dto.Custom == null
                ? null
                : TransitionStorageNormalizer.NormalizeForStorage(dto.Custom),
            "dissolve" => DissolveTransition(dto.DissolveDurationMs),
            _ => null,
        };
    }

    /// <summary>Dissolve / cross-fade using catalog <c>fade</c>.</summary>
    public static SlideTransition DissolveTransition(int dissolveDurationMs)
    {
        var ms = Math.Clamp(dissolveDurationMs, 50, 10_000);
        return TransitionStorageNormalizer.NormalizeForStorage(new SlideTransition
        {
            Type = "fade",
            Duration = ms,
            Easing = "ease-in-out",
        }) ?? new SlideTransition { Type = "fade", Duration = ms, Easing = "ease-in-out" };
    }
}