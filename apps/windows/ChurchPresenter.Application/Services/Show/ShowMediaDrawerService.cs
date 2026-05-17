using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
public sealed class ShowMediaDrawerService(
    IMediaLibraryService mediaLibrary,
    ICuePreparationService cuePreparation,
    IPlaybackEngine playback,
    ILiveProductionFacade? liveProduction = null) : IShowMediaDrawerService
{
    private readonly IMediaLibraryService _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly IPlaybackEngine _playback = playback ?? throw new ArgumentNullException(nameof(playback));
    private readonly ILiveProductionFacade? _liveProduction = liveProduction;

    /// <inheritdoc />
    public async Task<ShowMediaDrawerSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MediaLibraryItem> items = await _mediaLibrary.GetRootItemsAsync(cancellationToken).ConfigureAwait(false);
        ShowMediaDrawerItem[] projected = items
            .Select(ProjectItem)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ShowMediaDrawerSnapshot
        {
            Items = projected,
            StatusMessage = projected.Length == 0
                ? "No media assets are available in All Media yet."
                : $"{projected.Length} media asset(s) are ready in All Media.",
        };
    }

    /// <inheritdoc />
    public async Task<bool> TakeMediaLiveAsync(string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        IReadOnlyList<MediaLibraryItem> items = await _mediaLibrary.GetRootItemsAsync(cancellationToken).ConfigureAwait(false);
        MediaLibraryItem? item = items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return false;

        PreparedMediaCue? cue = _cuePreparation.PrepareMediaCue(item);
        if (cue == null)
            return false;

        _playback.EnterPreparedMediaCue(cue);
        _liveProduction?.ReleaseClearedLayers([OutputLayerKind.Media, OutputLayerKind.Audio]);
        return true;
    }

    private ShowMediaDrawerItem ProjectItem(MediaLibraryItem item)
    {
        string resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
        bool available = !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath);
        return new ShowMediaDrawerItem
        {
            Id = item.Id,
            Name = string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(item.Path) : item.Name,
            Type = MediaInference.ResolveEffectiveMediaType(item.Type, resolvedPath),
            Path = item.Path,
            IsAvailable = available,
            AvailabilitySummary = available ? "Ready" : "Missing file",
        };
    }
}