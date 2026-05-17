namespace ChurchPresenter.Models.Media;

/// <summary>Human-readable labels for <see cref="SlideTransition"/> in media library UI (catalog names).</summary>
public static class MediaCueTransitionFormatter
{
    /// <summary>Returns empty when <paramref name="transition"/> is null; otherwise catalog name and optional direction.</summary>
    public static string FormatLabel(SlideTransition? transition)
    {
        if (transition == null)
        {
            return "";
        }

        var def = TransitionCatalog.FindOrDefault(transition.Type);
        var name = def.Name;

        foreach (var param in def.Params)
        {
            if (!string.Equals(param.Key, "direction", StringComparison.Ordinal)
                || param.Options == null)
            {
                continue;
            }

            var raw = transition.GetParameter("direction", "");
            foreach (var opt in param.Options)
            {
                if (string.Equals(opt.Value, raw, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Concat(name, " · ", opt.Label);
                }
            }
        }

        return name;
    }
}