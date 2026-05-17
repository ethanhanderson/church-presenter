
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Output;

public sealed class AudienceOutputMonitorSelectionTests
{
    [Fact]
    public void ResolveValidMonitorIndices_filters_invalid_indices_and_sorts_distinct_results()
    {
        IReadOnlyList<MonitorInfoDto> monitors =
        [
            CreateMonitor(0),
            CreateMonitor(2),
            CreateMonitor(4),
        ];

        IReadOnlyList<int> resolved = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [4, 2, 99, 2, -1, 0],
            monitors);

        resolved.Should().Equal(0, 2, 4);
    }

    [Fact]
    public void ResolveValidMonitorIndices_returns_empty_when_no_requested_targets_exist()
    {
        IReadOnlyList<MonitorInfoDto> monitors =
        [
            CreateMonitor(1),
            CreateMonitor(3),
        ];

        IReadOnlyList<int> resolved = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [0, 2, 4],
            monitors);

        resolved.Should().BeEmpty();
    }

    [Fact]
    public void ResolveValidMonitorIndices_returns_empty_when_request_is_empty()
    {
        IReadOnlyList<int> resolved = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [],
            [CreateMonitor(0)]);

        resolved.Should().BeEmpty();
    }

    [Fact]
    public void ResolvePreferredMonitorIndex_returns_first_valid_requested_monitor()
    {
        int? resolved = AudienceOutputMonitorSelection.ResolvePreferredMonitorIndex(
            [4, 2, 99],
            [CreateMonitor(2), CreateMonitor(4), CreateMonitor(6)]);

        resolved.Should().Be(2);
    }

    [Fact]
    public void ResolvePreferredMonitorIndex_falls_back_to_first_available_monitor_when_request_is_missing()
    {
        int? resolved = AudienceOutputMonitorSelection.ResolvePreferredMonitorIndex(
            [],
            [CreateMonitor(3), CreateMonitor(5)]);

        resolved.Should().Be(3);
    }

    // ── Multi-monitor selection ───────────────────────────────────────────────

    [Fact]
    public void ResolveValidMonitorIndices_returns_all_valid_requested_monitors()
    {
        IReadOnlyList<MonitorInfoDto> monitors =
        [
            CreateMonitor(0),
            CreateMonitor(1),
            CreateMonitor(2),
        ];

        IReadOnlyList<int> resolved = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [0, 2],
            monitors);

        resolved.Should().Equal(0, 2);
    }

    [Fact]
    public void ResolveValidMonitorIndices_deduplicates_the_same_index()
    {
        IReadOnlyList<MonitorInfoDto> monitors =
        [
            CreateMonitor(0),
            CreateMonitor(1),
        ];

        IReadOnlyList<int> resolved = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [0, 0, 1],
            monitors);

        resolved.Should().BeEquivalentTo([0, 1], opts => opts.WithStrictOrdering(),
            because: "duplicates must be removed and result sorted");
    }

    // ── Exclusive role assignment (enforced by SettingsViewModel) ─────────────

    [Fact]
    public void Audience_and_stage_monitor_lists_are_independent_when_different()
    {
        // SettingsViewModel enforces exclusivity before persisting.
        // This test verifies that two distinct monitor ids can independently
        // satisfy each role's ResolveValid call — which is the contract
        // SettingsViewModel relies on.
        IReadOnlyList<MonitorInfoDto> monitors =
        [
            CreateMonitor(0),
            CreateMonitor(1),
        ];

        IReadOnlyList<int> audienceValid = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [0], monitors);
        IReadOnlyList<int> stageValid = AudienceOutputMonitorSelection.ResolveValidMonitorIndices(
            [1], monitors);

        audienceValid.Should().Equal(0);
        stageValid.Should().Equal(1);
        audienceValid.Intersect(stageValid).Should().BeEmpty(
            "audience and stage should not share a monitor when ViewModel enforces exclusivity");
    }

    private static MonitorInfoDto CreateMonitor(int index)
    {
        return new MonitorInfoDto(
            index,
            $"Display {index + 1}",
            Width: 1920,
            Height: 1080,
            X: index * 1920,
            Y: 0,
            IsPrimary: index == 0,
            RefreshRate: 60);
    }
}