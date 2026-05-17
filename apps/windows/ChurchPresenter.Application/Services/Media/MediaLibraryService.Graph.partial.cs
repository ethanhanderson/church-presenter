using ChurchPresenter.Backend.Media;

namespace ChurchPresenter.Services.Media;

public sealed partial class MediaLibraryService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaAsset>> GetAssetsAsync(CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        Dictionary<string, MediaAsset> assets = new(StringComparer.OrdinalIgnoreCase);

        foreach (var item in index.Items)
        {
            ct.ThrowIfCancellationRequested();
            AddAssetIfAbsent(assets, item);
        }

        foreach (var playlist in await GetPlaylistsAsync(ct).ConfigureAwait(false))
        {
            foreach (var item in playlist.Items)
            {
                ct.ThrowIfCancellationRequested();
                AddAssetIfAbsent(assets, item);
            }
        }

        return assets.Values.ToList();
    }

    /// <inheritdoc />
    public async Task<MediaAsset?> ResolveAssetAsync(string assetIdOrPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetIdOrPath);

        var lookupKey = NormalizeGraphLookupKey(assetIdOrPath);
        foreach (var item in await EnumerateLibraryItemsAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var asset = ToMediaAsset(item);
            if (MatchesAsset(asset, item, lookupKey))
                return asset;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<MediaPlaybackRequest?> ResolvePlaybackRequestAsync(
        SlideMediaCue cue,
        string? ownerReferenceId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (string.IsNullOrWhiteSpace(cue.MediaId))
            return null;

        var asset = await ResolveAssetAsync(cue.MediaId, ct).ConfigureAwait(false);
        if (asset is null)
            return null;

        MediaPlaybackLayerTarget target = MediaPlaybackLayerTargetNames.FromLayerName(cue.Target);
        if (target == MediaPlaybackLayerTarget.Audio && asset.Kind == MediaAssetKind.Audio)
        {
            var audioCue = new AudioCue
            {
                CueId = ResolveCueId(cue),
                AssetId = asset.AssetId,
                DisplayName = cue.DisplayName,
                OwnerReferenceId = ownerReferenceId,
                Overrides = CreateCueOverrides(cue, target),
            };

            return MediaPlaybackRequest.FromResolvedAudioCue(audioCue.Resolve(asset));
        }

        var mediaCue = new MediaCue
        {
            CueId = ResolveCueId(cue),
            AssetId = asset.AssetId,
            DisplayName = cue.DisplayName,
            OwnerReferenceId = ownerReferenceId,
            Overrides = CreateCueOverrides(cue, target),
        };

        return MediaPlaybackRequest.FromResolvedCue(mediaCue.Resolve(asset), target);
    }

    /// <inheritdoc />
    public async Task<MediaCleanupReferenceGraph> BuildCleanupReferenceGraphAsync(
        IEnumerable<PresentationProject> presentations,
        IEnumerable<MediaReferenceNode>? additionalNodes = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(presentations);

        List<MediaReferenceNode> nodes = new();
        foreach (var playlist in await GetPlaylistsAsync(ct).ConfigureAwait(false))
        {
            HashSet<string> assetIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (var item in playlist.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(item.Id))
                    assetIds.Add(item.Id);
            }

            if (assetIds.Count > 0)
            {
                nodes.Add(new MediaReferenceNode
                {
                    NodeId = $"media-playlist:{playlist.Id}",
                    DisplayName = playlist.Name,
                    Surface = MediaReferenceSurface.MediaPlaylist,
                    AssetIds = assetIds,
                });
            }
        }

        foreach (var presentation in presentations)
        {
            ct.ThrowIfCancellationRequested();
            var assetIds = await ResolvePresentationAssetIdsAsync(presentation, ct).ConfigureAwait(false);
            if (assetIds.Count == 0)
                continue;

            string presentationId = FirstNonWhiteSpace(presentation.Manifest.PresentationId, presentation.SourcePath, presentation.Manifest.Title)
                ?? Guid.NewGuid().ToString("N");
            nodes.Add(new MediaReferenceNode
            {
                NodeId = $"presentation:{presentationId}",
                DisplayName = FirstNonWhiteSpace(presentation.Manifest.Title, Path.GetFileNameWithoutExtension(presentation.SourcePath), presentationId) ?? presentationId,
                Surface = MediaReferenceSurface.Presentation,
                AssetIds = assetIds,
            });
        }

        if (additionalNodes is not null)
            nodes.AddRange(additionalNodes);

        return new MediaCleanupReferenceGraph { Nodes = nodes };
    }

    private async Task<HashSet<string>> ResolvePresentationAssetIdsAsync(PresentationProject presentation, CancellationToken ct)
    {
        HashSet<string> assetIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (var slide in presentation.Slides)
        {
            foreach (var cue in slide.MediaCues)
            {
                ct.ThrowIfCancellationRequested();
                await AddResolvedAssetIdAsync(assetIds, cue.MediaId, ct).ConfigureAwait(false);
            }

            switch (slide.Background)
            {
                case ImageSlideBackground image:
                    await AddResolvedAssetIdAsync(assetIds, image.MediaId, ct).ConfigureAwait(false);
                    break;
                case VideoSlideBackground video:
                    await AddResolvedAssetIdAsync(assetIds, video.MediaId, ct).ConfigureAwait(false);
                    break;
            }

            foreach (var mediaLayer in slide.Layers.OfType<MediaLayer>())
                await AddResolvedAssetIdAsync(assetIds, mediaLayer.MediaId, ct).ConfigureAwait(false);
        }

        return assetIds;
    }

    private async Task AddResolvedAssetIdAsync(HashSet<string> assetIds, string? assetIdOrPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assetIdOrPath))
            return;

        var asset = await ResolveAssetAsync(assetIdOrPath, ct).ConfigureAwait(false);
        if (asset is not null)
            assetIds.Add(asset.AssetId);
    }

    private async Task<IReadOnlyList<MediaLibraryItem>> EnumerateLibraryItemsAsync(CancellationToken ct)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        List<MediaLibraryItem> items = index.Items.Select(CloneItem).ToList();
        foreach (var playlist in await GetPlaylistsAsync(ct).ConfigureAwait(false))
            items.AddRange(playlist.Items.Select(CloneItem));

        return items;
    }

    private void AddAssetIfAbsent(IDictionary<string, MediaAsset> assets, MediaLibraryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || assets.ContainsKey(item.Id))
            return;

        assets[item.Id] = ToMediaAsset(item);
    }

    private MediaAsset ToMediaAsset(MediaLibraryItem item)
    {
        string resolvedPath = ResolveStoredMediaPath(item.Path);
        bool hasResolvedPath = !string.IsNullOrWhiteSpace(resolvedPath);
        bool isAvailable = hasResolvedPath && File.Exists(resolvedPath);
        MediaAvailability availability = isAvailable
            ? MediaAvailability.Available(resolvedPath, DateTimeOffset.UtcNow)
            : MediaAvailability.Missing(
                hasResolvedPath ? resolvedPath : item.Path,
                "Media file is missing.",
                DateTimeOffset.UtcNow,
                ContentAccessFailureKind.Missing);

        return new MediaAsset
        {
            AssetId = item.Id,
            DisplayName = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
            Kind = ResolveGraphAssetKind(item.Type),
            StoragePolicy = IsManagedMediaRelativePath(item.Path) ? MediaStoragePolicy.Managed : MediaStoragePolicy.Referenced,
            OriginalSourcePath = item.Path,
            ResolvedPath = availability.IsPlayable ? resolvedPath : null,
            Availability = availability,
            DefaultCue = ToMediaCueProfile(item.CueDefaults),
        };
    }

    private bool MatchesAsset(MediaAsset asset, MediaLibraryItem item, string lookupKey)
    {
        return string.Equals(NormalizeGraphLookupKey(asset.AssetId), lookupKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeGraphLookupKey(item.Path), lookupKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeGraphLookupKey(asset.ResolvedPath), lookupKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeGraphLookupKey(ResolveStoredMediaPath(item.Path)), lookupKey, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaCueProfile ToMediaCueProfile(MediaCueDefaults defaults)
    {
        MediaPlaybackLayerTarget target = MediaPlaybackLayerTargetNames.FromLayerName(defaults.Target);
        return new MediaCueProfile
        {
            Role = target == MediaPlaybackLayerTarget.MediaUnderlay ? MediaCueRole.Background : MediaCueRole.Foreground,
            Scaling = ResolveScalingMode(defaults.Fit) ?? MediaScalingMode.ScaleToFill,
            PlaybackMode = defaults.Loop ? MediaPlaybackMode.Loop : MediaPlaybackMode.Stop,
            AutoPlay = defaults.Autoplay,
            Muted = defaults.Muted,
            Transition = ToMediaTransition(defaults.Transition),
            Volume = defaults.Muted ? 0d : 1d,
        };
    }

    private static MediaCueOverride CreateCueOverrides(SlideMediaCue cue, MediaPlaybackLayerTarget target) => new()
    {
        Role = target == MediaPlaybackLayerTarget.MediaUnderlay ? MediaCueRole.Background : MediaCueRole.Foreground,
        Scaling = ResolveScalingMode(cue.Fit),
        PlaybackMode = cue.Loop.HasValue ? cue.Loop.Value ? MediaPlaybackMode.Loop : MediaPlaybackMode.Stop : null,
        AutoPlay = cue.Autoplay,
        Muted = cue.Muted,
        Transition = ToMediaTransition(cue.Transition),
        Volume = cue.Muted == true ? 0d : null,
    };

    private static MediaTransition? ToMediaTransition(SlideTransition? transition)
    {
        if (transition is null)
            return null;

        return new MediaTransition
        {
            TransitionId = transition.Type,
            Duration = TimeSpan.FromMilliseconds(transition.Duration),
        };
    }

    private static MediaScalingMode? ResolveScalingMode(string? fit)
    {
        return fit?.Trim().ToLowerInvariant() switch
        {
            "contain" or "fit" or "scale-to-fit" => MediaScalingMode.ScaleToFit,
            "fill" or "stretch" or "stretch-to-fill" => MediaScalingMode.StretchToFill,
            "blur" or "scaleandblur" or "scale-and-blur" => MediaScalingMode.ScaleAndBlur,
            "cover" or "scale-to-fill" => MediaScalingMode.ScaleToFill,
            _ => null,
        };
    }

    private static MediaAssetKind ResolveGraphAssetKind(string? mediaType) =>
        string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? MediaAssetKind.Video
            : string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase)
                ? MediaAssetKind.Audio
                : MediaAssetKind.Image;

    private static string ResolveCueId(SlideMediaCue cue) =>
        FirstNonWhiteSpace(cue.Id, cue.MediaId) ?? Guid.NewGuid().ToString("N");

    private static string NormalizeGraphLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed).Replace('\\', '/')
            : trimmed.Replace('\\', '/');
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
