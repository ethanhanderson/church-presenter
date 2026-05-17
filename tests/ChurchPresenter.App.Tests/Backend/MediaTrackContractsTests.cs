using ChurchPresenter.Backend.Media;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Backend;

public sealed class MediaTrackContractsTests
{
    [Fact]
    public void Missing_asset_can_be_relinked_without_changing_stable_identity()
    {
        MediaAsset asset = new()
        {
            AssetId = "walk-in-loop",
            DisplayName = "Walk-In Loop",
            Kind = MediaAssetKind.Video,
            StoragePolicy = MediaStoragePolicy.Referenced,
            OriginalSourcePath = @"D:\Media\WalkIn\Announcements\loop.mp4",
            ResolvedPath = @"D:\Media\WalkIn\Announcements\loop.mp4",
            Availability = MediaAvailability.Available(@"D:\Media\WalkIn\Announcements\loop.mp4"),
        };

        MediaAsset missing = asset.MarkMissing("Original drive is offline.");
        MediaRelinkResult relink = MediaRelinker.TryRelink(
            missing,
            searchRoots:
            [
                new MediaSearchRoot
                {
                    RootId = "media-backup",
                    RootPath = @"E:\MediaBackup",
                    Priority = 1,
                },
                new MediaSearchRoot
                {
                    RootId = "loose-clips",
                    RootPath = @"F:\LooseClips",
                    Priority = 0,
                },
            ],
            discoveredPaths:
            [
                @"F:\LooseClips\loop.mp4",
                @"E:\MediaBackup\WalkIn\Announcements\loop.mp4",
            ]);

        MediaAsset relinked = missing.ApplyRelink(relink);

        relinked.AssetId.Should().Be("walk-in-loop");
        relinked.ResolvedPath.Should().Be(@"E:\MediaBackup\WalkIn\Announcements\loop.mp4");
        relinked.Availability.Status.Should().Be(MediaAvailabilityStatus.Relinked);
        relinked.Availability.RelinkedFromPath.Should().Be(@"D:\Media\WalkIn\Announcements\loop.mp4");
        relinked.Availability.SearchRootId.Should().Be("media-backup");
    }

    [Fact]
    public void Cue_overrides_resolve_without_mutating_asset_defaults()
    {
        MediaAsset asset = new()
        {
            AssetId = "announcement-bed",
            DisplayName = "Announcement Bed",
            Kind = MediaAssetKind.Video,
            DefaultCue = new MediaCueProfile
            {
                Role = MediaCueRole.Background,
                Scaling = MediaScalingMode.ScaleAndBlur,
                PlaybackMode = MediaPlaybackMode.Loop,
                Transition = new MediaTransition
                {
                    TransitionId = "dissolve",
                    Duration = TimeSpan.FromSeconds(1),
                },
                AudioRouting = new AudioRoutingMetadata
                {
                    InternalChannelCount = 4,
                    TargetInternalChannels = [1, 2],
                },
                Effects = new MediaEffectSettings
                {
                    Opacity = 0.75,
                    Contrast = 1.1,
                    Saturation = 0.9,
                },
                Volume = 0.8,
            },
        };

        MediaCue cue = new()
        {
            CueId = "slide-cue-17",
            AssetId = asset.AssetId,
            Overrides = new MediaCueOverride
            {
                Role = MediaCueRole.Foreground,
                Delay = TimeSpan.FromSeconds(2),
                Transition = new MediaTransition
                {
                    TransitionId = "cut",
                    Duration = TimeSpan.FromMilliseconds(250),
                },
                Crop = new MediaCropRegion
                {
                    Left = 0.1,
                    Top = 0.2,
                    Right = 0.3,
                    Bottom = 0.4,
                },
                Volume = 0.65,
            },
        };

        ResolvedMediaCue resolved = cue.Resolve(asset);

        asset.DefaultCue.Role.Should().Be(MediaCueRole.Background);
        asset.DefaultCue.Delay.Should().BeNull();
        asset.DefaultCue.Transition?.TransitionId.Should().Be("dissolve");
        asset.DefaultCue.AudioRouting.TargetInternalChannels.Should().Equal(1, 2);

        resolved.EffectiveCue.Role.Should().Be(MediaCueRole.Foreground);
        resolved.EffectiveCue.Scaling.Should().Be(MediaScalingMode.ScaleAndBlur);
        resolved.EffectiveCue.Delay.Should().Be(TimeSpan.FromSeconds(2));
        resolved.EffectiveCue.Transition?.TransitionId.Should().Be("cut");
        resolved.EffectiveCue.AudioRouting.TargetInternalChannels.Should().Equal(1, 2);
        resolved.EffectiveCue.Crop.Should().NotBeNull();
        resolved.EffectiveCue.Crop!.Left.Should().Be(0.1);
        resolved.EffectiveCue.Effects.Opacity.Should().Be(0.75);
        resolved.EffectiveCue.Volume.Should().Be(0.65);
        resolved.RetriggersOnTake.Should().BeTrue();
    }

    [Fact]
    public void Asset_reference_resolves_identity_case_insensitively_and_checks_expected_kind()
    {
        MediaAsset asset = new()
        {
            AssetId = "Walk-In-Loop",
            DisplayName = "Walk-In Loop",
            Kind = MediaAssetKind.Video,
            StoragePolicy = MediaStoragePolicy.ImportedPackage,
        };

        AssetReference reference = new()
        {
            ReferenceId = "slide-media-cue",
            AssetId = "walk-in-loop",
            ExpectedKind = MediaAssetKind.Video,
            Surface = MediaReferenceSurface.Slide,
        };

        AssetReference wrongKind = reference with { ExpectedKind = MediaAssetKind.Audio };

        reference.Resolve([asset]).Should().BeSameAs(asset);
        wrongKind.Resolve([asset]).Should().BeNull();
        asset.OwnsManagedPayload.Should().BeTrue();
    }

    [Fact]
    public void Audio_cue_resolves_volume_routing_and_audio_playback_request()
    {
        MediaAsset asset = new()
        {
            AssetId = "walk-in-bed",
            DisplayName = "Walk-In Bed",
            Kind = MediaAssetKind.Audio,
            ResolvedPath = @"D:\Audio\bed.mp3",
            DefaultCue = new MediaCueProfile
            {
                Role = MediaCueRole.Background,
                PlaybackMode = MediaPlaybackMode.Loop,
                Volume = 0.7,
                AudioRouting = new AudioRoutingMetadata
                {
                    InternalChannelCount = 4,
                    TargetInternalChannels = [3, 4],
                    PreferredOutputDeviceId = "house-left-right",
                },
            },
        };

        AudioCue cue = new()
        {
            CueId = "audio-cue-1",
            AssetId = "walk-in-bed",
            Overrides = new MediaCueOverride
            {
                Role = MediaCueRole.Foreground,
                Volume = 0.5,
            },
        };

        ResolvedAudioCue resolved = cue.Resolve(asset);
        MediaPlaybackRequest request = MediaPlaybackRequest.FromResolvedAudioCue(resolved);

        resolved.IsPlayable.Should().BeTrue();
        resolved.EffectiveCue.Volume.Should().Be(0.5);
        resolved.EffectiveCue.AudioRouting.TargetInternalChannels.Should().Equal(3, 4);
        request.LayerTarget.Should().Be(MediaPlaybackLayerTarget.Audio);
        request.LayerName.Should().Be(MediaPlaybackLayerTargetNames.Audio);
        request.IsPlayable.Should().BeTrue();
    }

    [Fact]
    public void Missing_audio_cue_flows_into_unplayable_playback_request()
    {
        MediaAsset asset = new()
        {
            AssetId = "missing-bed",
            DisplayName = "Missing Bed",
            Kind = MediaAssetKind.Audio,
            OriginalSourcePath = @"D:\Audio\missing.mp3",
            Availability = MediaAvailability.Missing(@"D:\Audio\missing.mp3", "Audio file was not found."),
        };

        ResolvedAudioCue resolved = new AudioCue
        {
            CueId = "audio-cue-2",
            AssetId = "missing-bed",
        }.Resolve(asset);

        MediaPlaybackRequest request = MediaPlaybackRequest.FromResolvedAudioCue(resolved);

        resolved.IsPlayable.Should().BeFalse();
        request.IsPlayable.Should().BeFalse();
        request.Availability.Status.Should().Be(MediaAvailabilityStatus.Missing);
    }

    [Fact]
    public void Media_playlist_loops_when_configured()
    {
        MediaPlaylist playlist = new()
        {
            PlaylistId = "walk-in",
            Name = "Walk-In",
            AdvanceMode = PlaylistAdvanceMode.Loop,
            Entries =
            [
                new MediaPlaylistEntry { EntryId = "1", CueId = "cue-1", AssetId = "asset-1", DisplayName = "Loop 1" },
                new MediaPlaylistEntry { EntryId = "2", CueId = "cue-2", AssetId = "asset-2", DisplayName = "Loop 2" },
            ],
        };

        PlaylistPlaybackSelection<MediaPlaylistEntry> start = playlist.Start();
        PlaylistPlaybackSelection<MediaPlaylistEntry> next = playlist.Advance(start.Index!.Value);
        PlaylistPlaybackSelection<MediaPlaylistEntry> wrapped = playlist.Advance(next.Index!.Value);

        start.Entry?.CueId.Should().Be("cue-1");
        next.Entry?.CueId.Should().Be("cue-2");
        wrapped.Entry?.CueId.Should().Be("cue-1");
        wrapped.Wrapped.Should().BeTrue();
    }

    [Fact]
    public void Audio_playlist_stops_at_end_when_not_looping()
    {
        AudioPlaylist playlist = new()
        {
            PlaylistId = "walk-in-audio",
            Name = "Walk-In Audio",
            Entries =
            [
                new AudioPlaylistEntry { EntryId = "1", CueId = "audio-1", AssetId = "asset-a", DisplayName = "Intro" },
                new AudioPlaylistEntry { EntryId = "2", CueId = "audio-2", AssetId = "asset-b", DisplayName = "Bed" },
            ],
        };

        PlaylistPlaybackSelection<AudioPlaylistEntry> last = playlist.Advance(1);

        last.HasSelection.Should().BeFalse();
        last.ReachedEnd.Should().BeTrue();
    }

    [Fact]
    public void Cleanup_graph_only_marks_unowned_or_unreferenced_assets_as_safe_candidates()
    {
        MediaAsset managedReferenced = new()
        {
            AssetId = "asset-live",
            DisplayName = "Referenced Managed Asset",
            StoragePolicy = MediaStoragePolicy.Managed,
        };

        MediaAsset managedOrphan = new()
        {
            AssetId = "asset-orphan",
            DisplayName = "Managed Orphan",
            StoragePolicy = MediaStoragePolicy.Managed,
        };

        MediaAsset externalReference = new()
        {
            AssetId = "asset-external",
            DisplayName = "External Reference",
            StoragePolicy = MediaStoragePolicy.Referenced,
        };

        MediaCleanupReferenceGraph graph = new()
        {
            Nodes =
            [
                new MediaReferenceNode
                {
                    NodeId = "slide-42",
                    DisplayName = "Welcome Slide",
                    Surface = MediaReferenceSurface.Slide,
                    AssetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { managedReferenced.AssetId },
                },
            ],
        };

        IReadOnlyList<MediaCleanupCandidate> analysis = graph.Analyze(
            [managedReferenced, managedOrphan, externalReference],
            pinnedAssetIds: ["asset-pinned"]);

        analysis.Should().ContainSingle(candidate =>
            candidate.AssetId == "asset-live"
            && !candidate.EligibleForCleanup
            && candidate.IsReferenced);

        analysis.Should().ContainSingle(candidate =>
            candidate.AssetId == "asset-orphan"
            && candidate.EligibleForCleanup
            && !candidate.IsReferenced);

        analysis.Should().ContainSingle(candidate =>
            candidate.AssetId == "asset-external"
            && !candidate.EligibleForCleanup
            && candidate.Reason.Contains("Externally referenced assets", StringComparison.Ordinal));
    }
}