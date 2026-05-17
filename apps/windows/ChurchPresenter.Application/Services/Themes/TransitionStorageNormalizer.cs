
namespace ChurchPresenter.Services.Themes;

/// <summary>
/// Produces canonical <see cref="SlideTransition"/> values for JSON persistence so the catalog,
/// resolver, and Show UI agree on type keys, duration, and easing.
/// </summary>
public static class TransitionStorageNormalizer
{
    /// <summary>
    /// Returns a normalized transition suitable for writing to disk, or <c>null</c> when the
    /// input is empty or not meaningful after validation.
    /// </summary>
    public static SlideTransition? NormalizeForStorage(SlideTransition? transition)
    {
        if (transition == null || string.IsNullOrWhiteSpace(transition.Type))
            return null;

        var def = TransitionCatalog.Find(transition.Type.Trim());
        var key = def?.Key ?? transition.Type.Trim().ToLowerInvariant();

        if (string.Equals(key, "cut", StringComparison.OrdinalIgnoreCase))
        {
            return new SlideTransition
            {
                Type = "cut",
                Duration = 0,
            };
        }

        var duration = transition.Duration <= 0 ? 400 : transition.Duration;
        var stored = new SlideTransition
        {
            Type = key,
            Duration = duration,
            Easing = NormalizeEasing(transition.Easing),
        };

        if (transition.Parameters is { Count: > 0 })
        {
            stored.Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in transition.Parameters)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;
                stored.Parameters[kv.Key.Trim().ToLowerInvariant()] = kv.Value?.Trim() ?? "";
            }
        }

        return TransitionResolver.Normalize(stored) == null ? null : stored;
    }

    private static string? NormalizeEasing(string? easing)
    {
        if (string.IsNullOrWhiteSpace(easing))
            return null;
        return easing.Trim().ToLowerInvariant();
    }
}