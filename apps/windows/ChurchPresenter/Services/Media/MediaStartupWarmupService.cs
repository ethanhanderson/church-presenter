using ChurchPresenter.Adapters.Media.Pipeline;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Content;
using ChurchPresenter.Models.Documents;
using ChurchPresenter.Models.Settings;
using ChurchPresenter.Services.Content;
using ChurchPresenter.Services.Output;

using Microsoft.Extensions.Logging;

using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace ChurchPresenter.Services.Media;

/// <inheritdoc />
public sealed class MediaStartupWarmupService(
    IMediaLibraryService mediaLibrary,
    ICuePreparationService cuePreparation,
    ICuePreparationCoordinator cuePreparationCoordinator,
    IMediaThumbnailCache thumbnailCache,
    WinUiMediaSlotRegistry mediaSlotRegistry,
    IOutputTopologyService outputTopology,
    IWorkspaceService workspace,
    ICatalogService catalog,
    IShowSessionCache sessionCache,
    ILogger<MediaStartupWarmupService> logger) : IMediaStartupWarmupService
{
    private const int MaxParallelItemWarmups = 3;
    private const uint StartupThumbnailSize = 768;
    private static readonly TimeSpan PlaybackWarmupTimeout = TimeSpan.FromSeconds(3);

    private readonly IMediaLibraryService _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly ICuePreparationCoordinator _cuePreparationCoordinator = cuePreparationCoordinator ?? throw new ArgumentNullException(nameof(cuePreparationCoordinator));
    private readonly IMediaThumbnailCache _thumbnailCache = thumbnailCache ?? throw new ArgumentNullException(nameof(thumbnailCache));
    private readonly WinUiMediaSlotRegistry _mediaSlotRegistry = mediaSlotRegistry ?? throw new ArgumentNullException(nameof(mediaSlotRegistry));
    private readonly IOutputTopologyService _outputTopology = outputTopology ?? throw new ArgumentNullException(nameof(outputTopology));
    private readonly IWorkspaceService _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IShowSessionCache _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
    private readonly ILogger<MediaStartupWarmupService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _allMediaGate = new(1, 1);
    private readonly SemaphoreSlim _itemGate = new(MaxParallelItemWarmups, MaxParallelItemWarmups);
    private readonly object _taskGate = new();
    private Task? _queuedAllMediaWarmup;
    private Task? _queuedStartupWarmup;

    /// <inheritdoc />
    public event EventHandler<MediaStartupWarmupChangedEventArgs>? Changed;

    /// <inheritdoc />
    public MediaStartupWarmupSnapshot Current { get; private set; } = new();

    /// <inheritdoc />
    public void QueueStartupWarmup()
    {
        lock (_taskGate)
        {
            if (_queuedStartupWarmup is { IsCompleted: false })
                return;

            _queuedStartupWarmup = Task.Run(async () =>
            {
                try
                {
                    await WarmStartupAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Queued tiered startup warmup failed.");
                }
            });
        }
    }

    /// <inheritdoc />
    public async Task WarmStartupAsync(CancellationToken cancellationToken = default)
    {
        // Tier 1 (priority): slide-action media in the selected presentation/playlist. Awaited first so
        // the decoder/IO budget is spent on cues the operator is about to take live.
        try
        {
            await WarmSelectedContextSlideActionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tier 1 slide-action warmup failed; continuing with library warmup.");
        }

        // Tier 2 (secondary): the rest of the media-drawer library. Already-warm slots short-circuit, so
        // tier 1 work is reused rather than repeated.
        await WarmAllMediaAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WarmSelectedContextSlideActionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> presentationPaths = ResolveSelectedContextPresentationPaths();
        if (presentationPaths.Count == 0)
        {
            _logger.LogDebug("No selected presentation/playlist to warm slide actions for.");
            return;
        }

        foreach (string presentationPath in presentationPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WarmPresentationSlideActionsAsync(presentationPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the ordered presentation paths for the active selection: every presentation in the
    /// selected playlist, or the single selected presentation.
    /// </summary>
    private IReadOnlyList<string> ResolveSelectedContextPresentationPaths()
    {
        WorkspaceDto workspace = _workspace.Workspace;

        if (!string.IsNullOrWhiteSpace(workspace.SelectedPlaylistId))
        {
            PlaylistDto? playlist = _catalog.Catalog.Playlists
                .FirstOrDefault(p => string.Equals(p.Id, workspace.SelectedPlaylistId, StringComparison.OrdinalIgnoreCase));
            if (playlist != null)
            {
                List<string> paths = playlist.Items
                    .Select(item => item.Path)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Ensure the currently open presentation (if any) is warmed first.
                if (!string.IsNullOrWhiteSpace(workspace.SelectedPresentationPath))
                {
                    paths.RemoveAll(path => string.Equals(path, workspace.SelectedPresentationPath, StringComparison.OrdinalIgnoreCase));
                    paths.Insert(0, workspace.SelectedPresentationPath!);
                }

                return paths;
            }
        }

        return string.IsNullOrWhiteSpace(workspace.SelectedPresentationPath)
            ? Array.Empty<string>()
            : [workspace.SelectedPresentationPath!];
    }

    /// <summary>
    /// Warms the retained playback slots and cue records for every slide in one presentation that
    /// carries a media action, so those slide cues start instantly when taken live.
    /// </summary>
    private async Task WarmPresentationSlideActionsAsync(string presentationPath, CancellationToken cancellationToken)
    {
        PresentationDocument? document = _sessionCache.GetOrLoad(presentationPath);
        IReadOnlyList<PresentationSlide>? slides = document?.Project?.Slides;
        if (document == null || slides == null || slides.Count == 0)
        {
            _logger.LogDebug("Skipping slide-action warmup; {Path} is unavailable or has no slides.", presentationPath);
            return;
        }

        foreach (PresentationSlide slide in slides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slide.Disabled || slide.MediaCues.Count == 0 || string.IsNullOrWhiteSpace(slide.Id))
                continue;

            try
            {
                PreparedSlideCue? prepared = _cuePreparation.GetPreparedSlideCue(presentationPath, slide.Id, slide.Id)
                    ?? await _cuePreparation
                        .PrepareSlideCueAsync(presentationPath, slide.Id, slide.Id, document, cancellationToken)
                        .ConfigureAwait(false);

                if (prepared != null)
                    await WarmPreparedSlideMediaAsync(prepared, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not warm slide-action media for slide {SlideId} in {Path}.", slide.Id, presentationPath);
            }
        }
    }

    /// <inheritdoc />
    public void QueueWarmAllMedia()
    {
        lock (_taskGate)
        {
            if (_queuedAllMediaWarmup is { IsCompleted: false })
                return;

            _queuedAllMediaWarmup = Task.Run(async () =>
            {
                try
                {
                    await WarmAllMediaAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Queued media startup warmup failed.");
                }
            });
        }
    }

    /// <inheritdoc />
    public async Task WarmAllMediaAsync(CancellationToken cancellationToken = default)
    {
        if (!await _allMediaGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        try
        {
            Publish(new MediaStartupWarmupSnapshot
            {
                IsRunning = true,
                Phase = MediaStartupWarmupPhase.LoadingLibrary,
                StatusMessage = "Loading media library for startup warmup...",
                StartedAtUtc = startedAt,
            });

            IReadOnlyList<MediaLibraryItem> items = await LoadAllMediaItemsAsync(cancellationToken).ConfigureAwait(false);
            await WarmDistinctItemsAsync(items, startedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(Current with
            {
                IsRunning = false,
                Phase = MediaStartupWarmupPhase.Failed,
                StatusMessage = "Media startup warmup was canceled.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media startup warmup failed.");
            Publish(Current with
            {
                IsRunning = false,
                Phase = MediaStartupWarmupPhase.Failed,
                StatusMessage = $"Media startup warmup failed: {ex.Message}",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        finally
        {
            _allMediaGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WarmMediaItemsAsync(IEnumerable<MediaLibraryItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        await WarmDistinctItemsAsync(items, startedAt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WarmPreparedSlideMediaAsync(PreparedSlideCue cue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cue);

        await _cuePreparationCoordinator.PrepareSlideAsync(cue, cancellationToken).ConfigureAwait(false);

        (string Target, OutputLayerMedia? Media)[] mediaLayers =
        [
            ("mediaUnderlay", cue.MediaLayers.MediaUnderlay),
            ("mediaOverlay", cue.MediaLayers.MediaOverlay),
            ("audio", cue.MediaLayers.Audio),
        ];

        foreach ((string target, OutputLayerMedia? media) in mediaLayers.Where(static entry => entry.Media != null))
            await WarmOutputLayerMediaAsync(target, media!, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MediaLibraryItem>> LoadAllMediaItemsAsync(CancellationToken cancellationToken)
    {
        List<MediaLibraryItem> items = new();
        IReadOnlyList<MediaLibraryItem> rootItems = await _mediaLibrary.GetRootItemsAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(rootItems);

        IReadOnlyList<MediaPlaylistManifest> playlists = await _mediaLibrary.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false);
        foreach (MediaPlaylistManifest playlist in playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.AddRange(playlist.Items);
        }

        return items;
    }

    private async Task WarmDistinctItemsAsync(
        IEnumerable<MediaLibraryItem> items,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        MediaLibraryItem[] distinctItems = items
            .Where(static item => item != null)
            .DistinctBy(BuildLibraryItemWarmupKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Publish(new MediaStartupWarmupSnapshot
        {
            IsRunning = true,
            Phase = MediaStartupWarmupPhase.WarmingMedia,
            TotalItems = distinctItems.Length,
            StartedAtUtc = startedAt,
            StatusMessage = distinctItems.Length == 0
                ? "No media assets are available to warm."
                : $"Warming {distinctItems.Length} media asset(s)...",
        });

        int warmed = 0;
        int missing = 0;
        int failed = 0;

        await WarmDistinctThumbnailsAsync(distinctItems, cancellationToken).ConfigureAwait(false);

        Task[] tasks = distinctItems.Select(async item =>
        {
            await _itemGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                MediaWarmupItemResult result = await WarmLibraryItemAsync(item, cancellationToken).ConfigureAwait(false);
                switch (result)
                {
                    case MediaWarmupItemResult.Warmed:
                        Interlocked.Increment(ref warmed);
                        break;
                    case MediaWarmupItemResult.Missing:
                        Interlocked.Increment(ref missing);
                        break;
                    case MediaWarmupItemResult.Failed:
                        Interlocked.Increment(ref failed);
                        break;
                }
            }
            finally
            {
                _itemGate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        Publish(new MediaStartupWarmupSnapshot
        {
            IsRunning = false,
            Phase = MediaStartupWarmupPhase.Completed,
            TotalItems = distinctItems.Length,
            WarmedItems = warmed,
            MissingItems = missing,
            FailedItems = failed,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            StatusMessage = BuildCompletionStatus(distinctItems.Length, warmed, missing, failed),
        });
    }

    private async Task<MediaWarmupItemResult> WarmLibraryItemAsync(MediaLibraryItem item, CancellationToken cancellationToken)
    {
        string resolvedPath = string.Empty;
        try
        {
            resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                return MediaWarmupItemResult.Missing;

            string effectiveType = MediaInference.ResolveEffectiveMediaType(item.Type, resolvedPath);
            _ = await WarmThumbnailAsync(resolvedPath, effectiveType, cancellationToken).ConfigureAwait(false);

            _ = await WarmFileMetadataAsync(item, resolvedPath, effectiveType, cancellationToken).ConfigureAwait(false);

            _ = await WarmPreparedCueAsync(item, cancellationToken).ConfigureAwait(false);

            _ = await WarmRetainedPlaybackSlotsAsync(item, resolvedPath, effectiveType, cancellationToken).ConfigureAwait(false);

            return MediaWarmupItemResult.Warmed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not warm media item {ItemId} ({Path}).", item.Id, item.Path);
            return MediaWarmupItemResult.Failed;
        }
    }

    private async Task WarmOutputLayerMediaAsync(string target, OutputLayerMedia media, CancellationToken cancellationToken)
    {
        string path = await ResolveOutputLayerMediaPathAsync(media, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
            return;

        string effectiveType = MediaInference.ResolveEffectiveMediaType(media.MediaType, path);
        _ = await WarmThumbnailAsync(path, effectiveType, cancellationToken).ConfigureAwait(false);

        bool isAudio = string.Equals(target, "audio", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(effectiveType, "image", StringComparison.OrdinalIgnoreCase))
        {
            // Populate the exact retained playback slot (audience screen x layer x source x loop) that
            // take-live reuses, using this slide cue's loop/muted so the prepared key matches. When no
            // audience screens are configured yet, fall back to a throwaway decoder warm for the file cache.
            OutputLayerKind layerKind = isAudio ? OutputLayerKind.Audio : OutputLayerKind.Media;
            (int _, int total) = await PreloadRetainedAudienceSlotsAsync(
                path, layerKind, media.Loop, media.Muted, cancellationToken).ConfigureAwait(false);
            if (total == 0)
                _ = await WarmPlaybackDecoderAsync(path, effectiveType, media.Loop, cancellationToken).ConfigureAwait(false);
        }

        if (isAudio)
            return;

        PreparedMediaCue prepared = new()
        {
            Target = SlideMediaLayerBuilder.MapCueTarget(target),
            Media = CloneOutputLayerMedia(media, path, effectiveType),
        };
        await _cuePreparationCoordinator.PrepareMediaAsync(prepared, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WarmThumbnailAsync(string resolvedPath, string mediaType, CancellationToken cancellationToken) =>
        await _thumbnailCache.GetOrLoadAsync(resolvedPath, mediaType, StartupThumbnailSize, cancellationToken)
            .ConfigureAwait(false) != null;

    private async Task WarmDistinctThumbnailsAsync(
        IReadOnlyCollection<MediaLibraryItem> items,
        CancellationToken cancellationToken)
    {
        Task[] thumbnailTasks = items
            .Select(item => WarmLibraryItemThumbnailAsync(item, cancellationToken))
            .ToArray();
        await Task.WhenAll(thumbnailTasks).ConfigureAwait(false);
    }

    private async Task WarmLibraryItemThumbnailAsync(MediaLibraryItem item, CancellationToken cancellationToken)
    {
        try
        {
            string resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                return;

            string effectiveType = MediaInference.ResolveEffectiveMediaType(item.Type, resolvedPath);
            _ = await WarmThumbnailAsync(resolvedPath, effectiveType, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not warm thumbnail for media item {ItemId} ({Path}).", item.Id, item.Path);
        }
    }

    private async Task<bool> WarmFileMetadataAsync(
        MediaLibraryItem item,
        string resolvedPath,
        string effectiveType,
        CancellationToken cancellationToken)
    {
        if (effectiveType is not ("video" or "audio"))
            return true;

        if (string.IsNullOrWhiteSpace(item.Id))
            return false;

        MediaFileMetadata? metadata = await MediaFileMetadataReader.TryReadAsync(resolvedPath, effectiveType, cancellationToken)
            .ConfigureAwait(false);
        if (metadata is not { } value)
            return false;

        await _mediaLibrary.UpdateMediaItemFileMetadataAsync(
            item.Id,
            value.DurationSeconds,
            value.Width,
            value.Height,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> WarmPreparedCueAsync(MediaLibraryItem item, CancellationToken cancellationToken)
    {
        PreparedMediaCue? preparedCue = _cuePreparation.PrepareMediaCue(item);
        if (preparedCue == null)
            return false;

        await _cuePreparationCoordinator.PrepareMediaAsync(preparedCue, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<string> WarmRetainedPlaybackSlotsAsync(
        MediaLibraryItem item,
        string resolvedPath,
        string effectiveType,
        CancellationToken cancellationToken)
    {
        if (string.Equals(effectiveType, "image", StringComparison.OrdinalIgnoreCase))
            return "skipped-image";

        string target = SlideMediaLayerBuilder.MapCueTarget(item.CueDefaults.Target);
        OutputLayerKind layerKind = string.Equals(target, "audio", StringComparison.OrdinalIgnoreCase)
            ? OutputLayerKind.Audio
            : OutputLayerKind.Media;

        (int ready, int total) = await PreloadRetainedAudienceSlotsAsync(
            resolvedPath,
            layerKind,
            item.CueDefaults.Loop,
            item.CueDefaults.Muted,
            cancellationToken).ConfigureAwait(false);

        if (total == 0)
            return "skipped-no-audience-screens";

        return ready == total
            ? "retained-slots-ready"
            : $"retained-slots-partial:{ready}/{total}";
    }

    /// <summary>
    /// Preloads the retained playback slot for one source on every configured audience screen so the
    /// decoder is paused at its first frame and reused verbatim at take-live. Returns the number of
    /// slots that became ready and the number of audience screens considered.
    /// </summary>
    private async Task<(int Ready, int Total)> PreloadRetainedAudienceSlotsAsync(
        string resolvedPath,
        OutputLayerKind layerKind,
        bool loop,
        bool muted,
        CancellationToken cancellationToken)
    {
        OutputScreen[] audienceScreens = _outputTopology
            .GetSnapshot()
            .Screens
            .Values
            .Where(static screen => screen.Kind == OutputScreenKind.Audience)
            .ToArray();
        if (audienceScreens.Length == 0)
            return (0, 0);

        int ready = 0;
        Task[] preloadTasks = audienceScreens.Select(async screen =>
        {
            try
            {
                LayerSlotId slotId = LayerSlotId.For(screen.Id, layerKind);
                WinUiMediaSlotAdapter binding = _mediaSlotRegistry.GetOrCreatePrepared(
                    slotId,
                    resolvedPath,
                    resolvedPath,
                    loop);
                await binding.PreloadAsync(
                    resolvedPath,
                    loop,
                    muted ? 0d : 1d,
                    PlaybackWarmupTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (binding.IsReady)
                    Interlocked.Increment(ref ready);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Best effort: a single screen failing to preload should not abort the others.
            }
        }).ToArray();

        await Task.WhenAll(preloadTasks).ConfigureAwait(false);
        return (ready, audienceScreens.Length);
    }

    private async Task<string> ResolveOutputLayerMediaPathAsync(
        OutputLayerMedia media,
        CancellationToken cancellationToken)
    {
        string path = FirstNonWhiteSpace(media.ResolvedSourcePath, media.MediaId) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!Path.IsPathRooted(path))
                path = _mediaLibrary.ResolveStoredMediaPath(path);

            if (File.Exists(path))
                return path;
        }

        if (string.IsNullOrWhiteSpace(media.MediaId))
            return string.Empty;

        MediaAsset? asset = await _mediaLibrary.ResolveAssetAsync(media.MediaId, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(asset?.ResolvedPath) && File.Exists(asset.ResolvedPath)
            ? asset.ResolvedPath
            : string.Empty;
    }

    private async Task<string> WarmPlaybackDecoderAsync(
        string resolvedPath,
        string effectiveType,
        bool loop,
        CancellationToken cancellationToken)
    {
        if (string.Equals(effectiveType, "image", StringComparison.OrdinalIgnoreCase))
            return "skipped-image";

        TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaPlayer player = new() { Volume = 0 };
        MediaPlaybackList playbackList = new()
        {
            AutoRepeatEnabled = loop,
            MaxPlayedItemsToKeepOpen = 1,
        };

        void OnItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args) =>
            opened.TrySetResult();

        void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
            opened.TrySetResult();

        playbackList.ItemOpened += OnItemOpened;
        player.MediaFailed += OnMediaFailed;

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(resolvedPath).AsTask(cancellationToken)
                .ConfigureAwait(false);
            MediaSource source = MediaSource.CreateFromStorageFile(file);
            playbackList.Items.Add(new MediaPlaybackItem(source));
            player.Source = playbackList;
            player.Play();

            await opened.Task.WaitAsync(PlaybackWarmupTimeout, cancellationToken).ConfigureAwait(false);
            return "opened";
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("Timed out warming media decoder for {Path}.", resolvedPath);
            return "timeout";
        }
        finally
        {
            playbackList.ItemOpened -= OnItemOpened;
            player.MediaFailed -= OnMediaFailed;
            player.Pause();
            player.Source = null;
            player.Dispose();
        }
    }

    private void Publish(MediaStartupWarmupSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, new MediaStartupWarmupChangedEventArgs(snapshot));
    }

    private static OutputLayerMedia CloneOutputLayerMedia(
        OutputLayerMedia media,
        string resolvedPath,
        string effectiveType) =>
        new()
        {
            MediaId = string.IsNullOrWhiteSpace(media.MediaId) ? resolvedPath : media.MediaId,
            MediaType = effectiveType,
            DisplayName = media.DisplayName,
            Fit = media.Fit,
            Loop = media.Loop,
            Muted = media.Muted,
            Autoplay = media.Autoplay,
            Transition = media.Transition,
            ResolvedSourcePath = resolvedPath,
        };

    private static string BuildLibraryItemWarmupKey(MediaLibraryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Id))
            return string.Concat("id:", item.Id.Trim());

        return string.Concat("path:", item.Path?.Trim().Replace('\\', '/') ?? string.Empty);
    }

    private static string BuildCompletionStatus(int total, int warmed, int missing, int failed)
    {
        if (total == 0)
            return "No media assets are available to warm.";

        if (missing == 0 && failed == 0)
            return $"Media warmup complete. {warmed} asset(s) ready.";

        return $"Media warmup complete. {warmed} ready, {missing} missing, {failed} failed.";
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private enum MediaWarmupItemResult
    {
        Warmed,
        Missing,
        Failed,
    }

}
