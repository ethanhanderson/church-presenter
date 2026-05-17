
namespace ChurchPresenter.Services.Show;

/// <summary>
/// Picks the initial library, playlist, and presentation when the workspace is empty.
/// </summary>
public static class StartupWorkspaceSelector
{
    /// <summary>
    /// Returns updated workspace fields, or <c>null</c> if no heuristic applies.
    /// </summary>
    public static WorkspaceDto? TrySelectInitial(CatalogDto catalog, AppSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);

        var startOfToday = DateTime.Today;
        var upcoming = catalog.Playlists
            .Select(p => (Playlist: p, ServiceDate: TryParseServiceDate(p)))
            .Where(x => x.ServiceDate.HasValue && x.ServiceDate.Value.Date >= startOfToday)
            .OrderBy(x => x.ServiceDate)
            .FirstOrDefault();

        if (upcoming.Playlist != null && upcoming.ServiceDate.HasValue)
        {
            var pl = upcoming.Playlist;
            return new WorkspaceDto
            {
                ActivePage = "show",
                SelectedPlaylistId = pl.Id,
                SelectedLibraryId = null,
                SelectedPresentationPath = pl.Items.FirstOrDefault()?.Path,
            };
        }

        var recentPlaylist = catalog.Playlists
            .OrderByDescending(p => ParseTimestamp(p.UpdatedAt))
            .FirstOrDefault();
        if (recentPlaylist != null)
        {
            return new WorkspaceDto
            {
                ActivePage = "show",
                SelectedPlaylistId = recentPlaylist.Id,
                SelectedLibraryId = null,
                SelectedPresentationPath = recentPlaylist.Items.FirstOrDefault()?.Path,
            };
        }

        foreach (var recent in settings.RecentFiles)
        {
            var match = FindLibraryPresentation(catalog, recent.Path);
            if (match != null)
            {
                return new WorkspaceDto
                {
                    ActivePage = "show",
                    SelectedLibraryId = match.Value.Library.Id,
                    SelectedPlaylistId = null,
                    SelectedPresentationPath = match.Value.Presentation.Path,
                };
            }
        }

        PresentationRefDto? best = null;
        LibraryDto? bestLib = null;
        foreach (var library in catalog.Libraries)
        {
            foreach (var pres in library.Presentations)
            {
                if (best == null || ParseTimestamp(pres.UpdatedAt) > ParseTimestamp(best.UpdatedAt))
                {
                    best = pres;
                    bestLib = library;
                }
            }
        }

        if (bestLib != null && best != null)
        {
            return new WorkspaceDto
            {
                ActivePage = "show",
                SelectedLibraryId = bestLib.Id,
                SelectedPlaylistId = null,
                SelectedPresentationPath = best.Path,
            };
        }

        return null;
    }

    private static DateTime? TryParseServiceDate(PlaylistDto playlist)
    {
        var raw = playlist.ExternalSet?.ServiceDate;
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTime.TryParse(raw, out var dt) ? dt : null;
    }

    private static (LibraryDto Library, PresentationRefDto Presentation)? FindLibraryPresentation(CatalogDto catalog, string path)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var library in catalog.Libraries)
        {
            foreach (var pres in library.Presentations)
            {
                var p = pres.Path.Replace('\\', '/');
                if (string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return (library, pres);
            }
        }

        return null;
    }

    private static long ParseTimestamp(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return 0;
        return DateTime.TryParse(iso, out var dt) ? dt.Ticks : 0;
    }
}