using System.Collections.ObjectModel;


using CommunityToolkit.Mvvm.ComponentModel;

namespace ChurchPresenter.ViewModels;

/// <summary>Single presentation row under a library or playlist in the Show page source tree.</summary>
public sealed partial class ShowPresentationTreeItem : ObservableObject
{
    public PresentationRefDto Presentation { get; }

    public string? LibraryId { get; }

    public string? PlaylistId { get; }

    public int PlaylistIndex { get; }

    public int PlaylistCount { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ShowPresentationTreeItem(
        PresentationRefDto presentation,
        string? libraryId = null,
        string? playlistId = null,
        int playlistIndex = -1,
        int playlistCount = 0)
    {
        Presentation = presentation;
        LibraryId = libraryId;
        PlaylistId = playlistId;
        PlaylistIndex = playlistIndex;
        PlaylistCount = playlistCount;
    }

    public bool IsLibraryPresentation => !string.IsNullOrWhiteSpace(LibraryId);

    public bool IsPlaylistPresentation => !string.IsNullOrWhiteSpace(PlaylistId);

    public bool CanMovePlaylistUp => IsPlaylistPresentation && PlaylistIndex > 0;

    public bool CanMovePlaylistDown => IsPlaylistPresentation && PlaylistIndex >= 0 && PlaylistIndex < PlaylistCount - 1;
}

/// <summary>
/// Library node in the Show sources pane: header row plus a flat list of presentations from
/// <see cref="LibraryDto.Presentations"/>.
/// </summary>
public sealed partial class ShowLibraryTreeItem : ObservableObject
{
    public LibraryDto Library { get; }

    public ObservableCollection<ShowPresentationTreeItem> PresentationRows { get; } = new();

    [ObservableProperty]
    private bool _isHighlighted;

    public ShowLibraryTreeItem(LibraryDto library)
    {
        Library = library;
    }

    /// <summary>Sidebar row uses a button when true; read-only chrome when empty.</summary>
    public bool HasPresentations => Library.Presentations.Count > 0;
}

/// <summary>
/// Playlist node in the Show sources pane: header row plus presentation rows from <see cref="PlaylistDto.Items"/> with
/// optional external-set summary and playlist index on each row for reorder.
/// </summary>
public sealed partial class ShowPlaylistTreeItem : ObservableObject
{
    public PlaylistDto Playlist { get; }

    public ObservableCollection<ShowPresentationTreeItem> PresentationRows { get; } = new();

    [ObservableProperty]
    private bool _isHighlighted;

    public ShowPlaylistTreeItem(PlaylistDto playlist)
    {
        Playlist = playlist;
    }

    /// <summary>Sidebar row uses a button when true; read-only chrome when empty.</summary>
    public bool HasPresentations => Playlist.Items.Count > 0;

    public bool HasExternalSet => Playlist.ExternalSet != null;

    public string? ExternalSetSummary =>
        Playlist.ExternalSet == null
            ? null
            : string.IsNullOrWhiteSpace(Playlist.ExternalSet.ServiceDate)
                ? "Sunday Manager"
                : $"Sunday • {Playlist.ExternalSet.ServiceDate}";
}