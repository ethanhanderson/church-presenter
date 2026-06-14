using ChurchPresenter.ViewModels;

namespace ChurchPresenter.Hosting;

/// <summary>Counts bound deck thumbnails at lifecycle moments for navigation timing diagnosis.</summary>
internal static class DeckThumbnailVisibilityDiagnostics
{
    public static void LogMoment(string moment, ShowViewModel show, string? detail = null)
    {
        var seen = new HashSet<ShowSlideDeckItem>();
        int total = 0;
        int bound = 0;
        int navigation = 0;
        int compositor = 0;
        foreach (ShowSlideDeckItem item in show.EnumerateDeckThumbnailItems())
        {
            if (!seen.Add(item))
                continue;

            total++;
            if (item.Thumbnail is null)
                continue;

            bound++;
            if (item.IsNavigationSnapshot)
                navigation++;
            else
                compositor++;
        }

        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        ShowNavigationDiagnostics.Log(
            "DeckThumb.Visible",
            $"moment={moment} total={total} bound={bound} nav={navigation} compositor={compositor}{suffix}");
    }

    public static void LogItem(string moment, ShowSlideDeckItem item)
    {
        string kind = item.Thumbnail is null
            ? "empty"
            : item.IsNavigationSnapshot
                ? "nav"
                : "compositor";
        ShowNavigationDiagnostics.Log(
            "DeckThumb.Item",
            $"moment={moment} slide={item.Slide.Id} kind={kind}");
    }
}
