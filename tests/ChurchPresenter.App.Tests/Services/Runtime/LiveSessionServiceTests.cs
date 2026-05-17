
using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Runtime;

/// <summary>Parity: live store audience/output toggle (legacy <c>liveStore</c> / output disable audience).</summary>
public sealed class LiveSessionServiceTests
{
    [Fact]
    public void SetAudienceEnabled_is_routing_only_and_does_not_raise_Changed()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        svc.SetAudienceEnabled(false);
        raised.Should().Be(0);

        svc.SetAudienceEnabled(true);
        raised.Should().Be(0);
        svc.IsAudienceEnabled.Should().BeTrue();

        svc.SetAudienceEnabled(true);
        raised.Should().Be(0);
    }

    [Fact]
    public void SetStageEnabled_raises_Changed_only_when_value_changes()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        svc.SetStageEnabled(false);
        raised.Should().Be(0, "no event when value is already false");

        svc.SetStageEnabled(true);
        raised.Should().Be(1);
        svc.IsStageEnabled.Should().BeTrue();

        svc.SetStageEnabled(true);
        raised.Should().Be(1, "no event when value is unchanged");

        svc.SetStageEnabled(false);
        raised.Should().Be(2);
        svc.IsStageEnabled.Should().BeFalse();
    }

    [Fact]
    public void Audience_and_stage_states_are_independent()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);

        svc.SetAudienceEnabled(true);
        svc.SetStageEnabled(false);

        svc.IsAudienceEnabled.Should().BeTrue();
        svc.IsStageEnabled.Should().BeFalse();

        svc.SetStageEnabled(true);
        svc.IsAudienceEnabled.Should().BeTrue("audience state should be unaffected by stage toggle");
        svc.IsStageEnabled.Should().BeTrue();

        svc.SetAudienceEnabled(false);
        svc.IsAudienceEnabled.Should().BeFalse();
        svc.IsStageEnabled.Should().BeTrue("stage state should be unaffected by audience toggle");
    }

    [Fact]
    public void NextSlideAction_skips_disabled_slides()
    {
        var svc = new LiveSessionService(NullLogger<LiveSessionService>.Instance);
        var doc = new PresentationDocument
        {
            Manifest = new PresentationManifestDto { Title = "Slides" },
            Slides =
            [
                new SlideDto { Id = "slide-1", Type = "song" },
                new SlideDto { Id = "slide-2", Type = "song" },
                new SlideDto { Id = "slide-3", Type = "song" },
            ],
            Project = new PresentationProject
            {
                Manifest = new PresentationManifest { Title = "Slides" },
                Slides =
                [
                    new PresentationSlide { Id = "slide-1", Type = "song" },
                    new PresentationSlide { Id = "slide-2", Type = "song", Disabled = true },
                    new PresentationSlide { Id = "slide-3", Type = "song" },
                ],
            },
        };

        svc.GoLive(doc, "presentations/slides.cpres");
        svc.GoToSlide("slide-1");

        svc.NextSlideAction();

        svc.CurrentSlideId.Should().Be("slide-3");
    }

    // ── Arranged playback sequence tests ─────────────────────────────────────

    [Fact]
    public void BuildPlaybackSequence_natural_sequence_order_matches_project_slide_order()
    {
        var project = BuildSongProject();
        PresentationModelUtilities.ReconcileArrangement(project);

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        seq.Instances.Select(i => i.SlideId)
            .Should().ContainInOrder(project.Slides.Select(s => s.Id),
                "natural playback follows the raw slide list");
    }

    [Fact]
    public void BuildPlaybackSequence_with_custom_arrangement_reorders_groups()
    {
        var project = BuildSongProject();
        PresentationModelUtilities.ReconcileArrangement(project);

        var chorus = project.Arrangement.Sections.First(g => g.Section == "chorus");
        var verse = project.Arrangement.Sections.First(g => g.Section == "verse");

        // Arrangement: chorus first, then verse.
        var customArr = new NamedArrangement
        {
            Id = "reorder",
            Name = "Chorus First",
            IsNatural = false,
            Groups =
            [
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
                new ArrangementGroupRef { SectionGroupId = verse.Id },
            ],
        };
        project.Arrangement.Arrangements.Add(customArr);
        project.Arrangement.ActiveArrangementId = "reorder";

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        // All chorus slides should come before verse slides.
        var slideIds = seq.Instances.Select(i => i.SlideId).ToList();
        slideIds.Should().StartWith(chorus.SlideIds,
            "the custom arrangement places chorus before verse");
    }

    [Fact]
    public void BuildPlaybackSequence_repeated_group_has_two_distinct_occurrence_indices()
    {
        var project = BuildSongProject();
        PresentationModelUtilities.ReconcileArrangement(project);

        var chorus = project.Arrangement.Sections.First(g => g.Section == "chorus");

        var customArr = new NamedArrangement
        {
            Id = "double-chorus",
            Name = "Double Chorus",
            IsNatural = false,
            Groups =
            [
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
            ],
        };
        project.Arrangement.Arrangements.Add(customArr);
        project.Arrangement.ActiveArrangementId = "double-chorus";

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        seq.Count.Should().Be(chorus.SlideIds.Count * 2);

        var indices = seq.Instances.Select(i => i.OccurrenceIndex).ToList();
        indices.Should().Contain(0).And.Contain(1,
            "each repetition of the group gets an incrementing occurrence index");

        // The same base slide ID must appear twice with different instance keys.
        var firstSlideInstances = seq.Instances
            .Where(i => i.SlideId == chorus.SlideIds[0])
            .ToList();
        firstSlideInstances.Should().HaveCount(2);
        firstSlideInstances[0].InstanceKey.Should().NotBe(firstSlideInstances[1].InstanceKey);
    }

    [Fact]
    public void BuildPlaybackSequence_disabled_slides_are_included_for_caller_to_skip()
    {
        var project = BuildSongProject();
        // Mark a slide as disabled.
        project.Slides[0].Disabled = true;
        PresentationModelUtilities.ReconcileArrangement(project);

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        // BuildPlaybackSequence includes disabled slides; it's ShowViewModel/LiveSessionService
        // that skips them during navigation.
        seq.Instances.Should().Contain(i => i.SlideId == project.Slides[0].Id,
            "the sequence always contains all slides; navigation callers decide which to skip");
    }

    [Fact]
    public void BuildPlaybackSequence_FindByInstanceKey_locates_repeated_instance()
    {
        var project = BuildSongProject();
        PresentationModelUtilities.ReconcileArrangement(project);

        var chorus = project.Arrangement.Sections.First(g => g.Section == "chorus");
        var customArr = new NamedArrangement
        {
            Id = "find-key-arr",
            Name = "Find Key",
            IsNatural = false,
            Groups =
            [
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
            ],
        };
        project.Arrangement.Arrangements.Add(customArr);
        project.Arrangement.ActiveArrangementId = "find-key-arr";

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);

        // Second occurrence instance key should be findable.
        var secondOccurrence = seq.Instances.First(i => i.OccurrenceIndex == 1);
        var found = seq.FindByInstanceKey(secondOccurrence.InstanceKey);

        found.Should().NotBeNull();
        found!.InstanceKey.Should().Be(secondOccurrence.InstanceKey);
        found.OccurrenceIndex.Should().Be(1);
    }

    [Fact]
    public void BuildPlaybackSequence_FindCurrentProgramInstance_prefers_instance_key_over_first_matching_slide_id()
    {
        var project = BuildSongProject();
        PresentationModelUtilities.ReconcileArrangement(project);

        var chorus = project.Arrangement.Sections.First(g => g.Section == "chorus");
        var customArr = new NamedArrangement
        {
            Id = "current-instance-arr",
            Name = "Current Instance",
            IsNatural = false,
            Groups =
            [
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
                new ArrangementGroupRef { SectionGroupId = chorus.Id },
            ],
        };
        project.Arrangement.Arrangements.Add(customArr);
        project.Arrangement.ActiveArrangementId = "current-instance-arr";

        var seq = PresentationModelUtilities.BuildPlaybackSequence(project);
        var repeatedSlide = chorus.SlideIds[0];
        var secondOccurrence = seq.Instances.First(i => i.SlideId == repeatedSlide && i.OccurrenceIndex == 1);

        var found = seq.FindCurrentProgramInstance(secondOccurrence.InstanceKey, repeatedSlide);

        found.Should().NotBeNull();
        found!.InstanceKey.Should().Be(secondOccurrence.InstanceKey);
        found.OccurrenceIndex.Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PresentationProject BuildSongProject() =>
        new()
        {
            Manifest = new PresentationManifest { Title = "And Can It Be" },
            Slides =
            [
                new PresentationSlide { Id = "v1", Type = "song", Section = "verse",  SectionLabel = "Verse 1" },
                new PresentationSlide { Id = "v2", Type = "song", Section = "verse",  SectionLabel = "Verse 1" },
                new PresentationSlide { Id = "c1", Type = "song", Section = "chorus", SectionLabel = "Chorus"  },
                new PresentationSlide { Id = "c2", Type = "song", Section = "chorus", SectionLabel = "Chorus"  },
            ],
            Arrangement = new PresentationArrangement { Order = ["v1", "v2", "c1", "c2"] },
        };
}