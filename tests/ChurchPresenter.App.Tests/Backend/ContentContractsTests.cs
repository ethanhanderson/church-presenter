using ChurchPresenter.Backend.Content;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Backend;

public sealed class ContentContractsTests
{
    [Fact]
    public void Reusable_presentation_refs_can_select_different_arrangements()
    {
        ContentPresentation presentation = CreateSongPresentation();
        PresentationReference firstServiceUse = new()
        {
            PresentationId = presentation.Id,
            LibraryId = "songs",
            ArrangementId = "short",
            DisplayName = "Mighty Song",
        };
        PresentationReference secondServiceUse = new()
        {
            PresentationId = presentation.Id,
            LibraryId = "songs",
            ArrangementId = "full",
            DisplayName = "Mighty Song",
        };

        ArrangementResolution shortSet = firstServiceUse.ResolveAgainst(presentation);
        ArrangementResolution fullSet = secondServiceUse.ResolveAgainst(presentation);

        shortSet.IsResolved.Should().BeTrue();
        shortSet.Arrangement!.GroupIds.Should().Equal("verse", "chorus");
        fullSet.IsResolved.Should().BeTrue();
        fullSet.Arrangement!.GroupIds.Should().Equal("verse", "chorus", "bridge", "chorus");
    }

    [Fact]
    public void Missing_arrangement_is_reported_without_silent_fallback()
    {
        ContentPresentation presentation = CreateSongPresentation();
        PresentationReference reference = new()
        {
            PresentationId = presentation.Id,
            ArrangementId = "evening-tag",
        };

        ArrangementResolution result = reference.ResolveAgainst(presentation);

        result.IsResolved.Should().BeFalse();
        result.Arrangement.Should().BeNull();
        result.Diagnostic.Should().Contain("evening-tag");
    }

    [Fact]
    public void Planning_center_sequence_requires_exact_group_names()
    {
        ContentPresentation presentation = CreateSongPresentation();

        ArrangementCreationResult exactMatch = presentation.TryCreateArrangementFromSequence(
            arrangementId: "pco-am",
            name: "PCO AM",
            groupNames: ["Verse", "Chorus"]);
        ArrangementCreationResult caseMismatch = presentation.TryCreateArrangementFromSequence(
            arrangementId: "pco-pm",
            name: "PCO PM",
            groupNames: ["verse", "Chorus"]);

        exactMatch.Succeeded.Should().BeTrue();
        exactMatch.Arrangement!.GroupIds.Should().Equal("verse", "chorus");
        caseMismatch.Succeeded.Should().BeFalse();
        caseMismatch.MissingGroupName.Should().Be("verse");
    }

    [Fact]
    public void Playlist_structural_items_are_non_executable_and_plan_links_need_local_targets()
    {
        HeaderPlaylistItem header = new()
        {
            Label = "Worship",
        };
        PlaceholderPlaylistItem placeholder = new()
        {
            Label = "Sermon notes pending",
            ExpectedKind = PlaylistItemKind.PresentationReference,
        };
        ExternalPlanPlaylistItem unresolvedPlanItem = new()
        {
            Label = "Announcements",
            ExternalPlan = new ExternalPlanReference
            {
                Provider = "Planning Center",
                ServiceId = "svc-1",
                PlanId = "plan-1",
                ItemId = "item-1",
            },
        };
        ExternalPlanPlaylistItem linkedPlanItem = unresolvedPlanItem with
        {
            ExternalPlan = unresolvedPlanItem.ExternalPlan with
            {
                LinkedPresentation = new PresentationReference
                {
                    PresentationId = "announcements",
                    ArrangementId = "full",
                },
            },
        };

        header.Kind.Should().Be(PlaylistItemKind.Header);
        header.IsStructural.Should().BeTrue();
        header.IsExecutable.Should().BeFalse();
        placeholder.Kind.Should().Be(PlaylistItemKind.Placeholder);
        placeholder.IsStructural.Should().BeTrue();
        placeholder.IsExecutable.Should().BeFalse();
        unresolvedPlanItem.IsExecutable.Should().BeFalse();
        linkedPlanItem.IsExecutable.Should().BeTrue();
    }

    [Fact]
    public void Playlist_template_creates_new_playlist_instances_while_preserving_structure()
    {
        PlaylistTemplate template = new()
        {
            Id = "sunday-template",
            Name = "Sunday Template",
            Items =
            [
                new HeaderPlaylistItem { Id = "header-1", Label = "Pre-Service" },
                new MediaPlaylistItem
                {
                    Id = "media-1",
                    Label = "Walk-In Loop",
                    Media = new MediaReference { MediaId = "walk-in", Name = "Walk-In Loop" },
                },
                new PlaceholderPlaylistItem
                {
                    Id = "placeholder-1",
                    Label = "Message Slides",
                    ExpectedKind = PlaylistItemKind.PresentationReference,
                },
            ],
        };

        ContentPlaylist playlist = template.CreatePlaylist("playlist-2026-05-03", "Sunday 9 AM");

        playlist.SourceTemplateId.Should().Be(template.Id);
        playlist.Items.Select(item => item.Kind).Should().Equal(
            PlaylistItemKind.Header,
            PlaylistItemKind.MediaReference,
            PlaylistItemKind.Placeholder);
        playlist.Items.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        playlist.Items.Select(item => item.Id).Should().NotIntersectWith(template.Items.Select(item => item.Id));
        playlist.Items[2].Should().BeOfType<PlaceholderPlaylistItem>()
            .Which.ExpectedKind.Should().Be(PlaylistItemKind.PresentationReference);
    }

    [Fact]
    public void Package_boundaries_capture_playlist_and_media_semantics()
    {
        ContentPackageBoundaryDescriptor playlistPackage = ContentPackageBoundaries.Describe(ContentPackageBoundaryKind.PlaylistPackage);
        ContentPackageBoundaryDescriptor playlistPackageWithMedia = ContentPackageBoundaries.Describe(ContentPackageBoundaryKind.PlaylistPackageWithMedia);
        ContentPackageBoundaryDescriptor presentationBundle = ContentPackageBoundaries.Describe(ContentPackageBoundaryKind.PresentationBundle);
        ContentPackageBoundaryDescriptor supportFilePackage = ContentPackageBoundaries.Describe(ContentPackageBoundaryKind.SupportFilePackage);
        ContentPackageBoundaryDescriptor syncBackup = ContentPackageBoundaries.Describe(ContentPackageBoundaryKind.SyncBackup);

        playlistPackage.SupportsPlaylistItem(PlaylistItemKind.Header).Should().BeTrue();
        playlistPackage.SupportsPlaylistItem(PlaylistItemKind.ExternalPlanReference).Should().BeTrue();
        playlistPackageWithMedia.IncludesMediaPayloads.Should().BeTrue();
        presentationBundle.IncludesPresentationDocuments.Should().BeTrue();
        presentationBundle.SupportsPlaylistItem(PlaylistItemKind.ExternalPlanReference).Should().BeFalse();
        supportFilePackage.IncludesSharedConfiguration.Should().BeTrue();
        supportFilePackage.SupportsDestructiveReplace.Should().BeFalse();
        syncBackup.IncludesSharedConfiguration.Should().BeTrue();
        syncBackup.SupportsDestructiveReplace.Should().BeTrue();
    }

    [Fact]
    public void Provenance_semantics_preserve_refresh_behavior_across_supported_boundaries()
    {
        ContentProvenance planningCenter = ContentProvenance.PlanningCenterAttachment("item-42", "plan-99");
        ContentProvenance packageImport = ContentProvenance.PackageImport("pkg-2026");

        planningCenter.CanRefreshFromSource.Should().BeTrue();
        planningCenter.PreserveLocalEdits.Should().BeTrue();
        packageImport.CanRefreshFromSource.Should().BeFalse();
        ContentPackageBoundaries.CanRetainProvenance(ContentPackageBoundaryKind.PresentationDocument, planningCenter).Should().BeTrue();
        ContentPackageBoundaries.CanRetainProvenance(ContentPackageBoundaryKind.PlaylistPackage, packageImport).Should().BeTrue();
        ContentPackageBoundaries.CanRetainProvenance(ContentPackageBoundaryKind.SharedConfiguration, planningCenter).Should().BeFalse();
    }

    private static ContentPresentation CreateSongPresentation()
    {
        return new ContentPresentation
        {
            Id = "mighty-song",
            Title = "Mighty Song",
            Slides =
            [
                new ContentSlide { Id = "s1", Title = "Verse 1", GroupId = "verse" },
                new ContentSlide { Id = "s2", Title = "Chorus", GroupId = "chorus" },
                new ContentSlide { Id = "s3", Title = "Bridge", GroupId = "bridge" },
            ],
            Groups =
            [
                new ContentGroup { Id = "verse", Name = "Verse", SlideIds = ["s1"] },
                new ContentGroup { Id = "chorus", Name = "Chorus", SlideIds = ["s2"] },
                new ContentGroup { Id = "bridge", Name = "Bridge", SlideIds = ["s3"] },
            ],
            Arrangements =
            [
                new ContentArrangement
                {
                    Id = "natural",
                    Name = "Natural",
                    IsNatural = true,
                    GroupIds = ["verse", "chorus", "bridge"],
                },
                new ContentArrangement
                {
                    Id = "short",
                    Name = "Short",
                    GroupIds = ["verse", "chorus"],
                },
                new ContentArrangement
                {
                    Id = "full",
                    Name = "Full",
                    GroupIds = ["verse", "chorus", "bridge", "chorus"],
                },
            ],
            DefaultArrangementId = "natural",
            Provenance = ContentProvenance.PlanningCenterAttachment("song-17", "plan-17"),
        };
    }
}