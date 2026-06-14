using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;

using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Show.Queries;

public sealed class OperatorQueryServicesTests
{
    [Fact]
    public void LiveDiagnosticsRecoveryQueryService_projects_diagnostics_and_clear_group_actions()
    {
        LiveProductionQuerySnapshot query = new()
        {
            Version = 42,
            ActiveLookId = "default",
            Screens =
            [
                new LiveOutputScreenQuery
                {
                    ScreenId = "main",
                    ScreenName = "Main",
                    Health = EndpointHealth.Connected,
                    DiagnosticsMessage = "Main connected.",
                    HasResolvedFrame = true,
                    ActiveLookName = "Default",
                },
                new LiveOutputScreenQuery
                {
                    ScreenId = "stage",
                    ScreenName = "Stage",
                    Health = EndpointHealth.Missing,
                    DiagnosticsMessage = "Stage display is missing.",
                    HasResolvedFrame = false,
                    ActiveLookName = "Default",
                },
            ],
            Selection = new LiveSelectionStateQuery
            {
                SelectedSlideId = "s2",
                LiveSlideId = "s1",
                UserOverrideSelection = true,
            },
            ActiveLayers =
            [
                new LiveLayerStateQuery
                {
                    Kind = ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages,
                    Id = "messages",
                    DisplayName = "Messages",
                    IsLive = true,
                    PayloadId = "welcome",
                    PayloadName = "Welcome",
                },
            ],
            FrameHealth =
            [
                new LiveFrameHealthQuery
                {
                    ScreenId = "stage",
                    ResolvedSequence = 12,
                    AppliedSequence = 10,
                    IsStale = true,
                    DroppedFrameCount = 1,
                },
            ],
            MediaIssues =
            [
                new LiveMediaIssueQuery
                {
                    Id = "missing-media:walk-in",
                    Kind = "missing-media",
                    SubjectId = "walk-in",
                    Message = "Walk-in media is missing.",
                    RecoveryActionType = "relink-media",
                },
            ],
            Generated = new LiveGeneratedSystemsQuery
            {
                ClearGroupIds = ["overlays"],
                ClearGroups =
                [
                    new LiveClearGroupQuery
                    {
                        Id = "overlays",
                        Name = "Overlays",
                        Layers =
                        [
                            ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages,
                            ChurchPresenter.Backend.Rendering.OutputLayerKind.Props,
                        ],
                    },
                ],
                Timers =
                [
                    new TimerSnapshot
                    {
                        Id = "service",
                        Name = "Service",
                        Status = GeneratedTimerStatus.Running,
                    },
                ],
                StageMessageText = "Stand by",
                StageLayoutsByScreenId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["stage"] = "confidence",
                },
            },
        };

        LiveDiagnosticsRecoverySnapshot snapshot = LiveDiagnosticsRecoveryQueryService.BuildSnapshot(query);

        snapshot.Version.Should().Be(42);
        snapshot.OutputSummary.Should().Contain("1/2");
        snapshot.GeneratedSystemsSummary.Should().Contain("Timers: 1 active");
        snapshot.StageSummary.Should().Contain("Stand by");
        snapshot.Diagnostics.Should().ContainSingle(item => item.Title == "Main" && item.Severity == "healthy");
        snapshot.Diagnostics.Should().Contain(item => item.Title == "Selection" && item.Message.Contains("differs"));
        snapshot.Diagnostics.Should().Contain(item => item.Title == "missing-media" && item.Severity == "error");
        snapshot.RecoveryActions.Should().ContainSingle(action =>
            action.Id == "clear-group:overlays" &&
            action.ActionType == "clear-group");
        snapshot.RecoveryActions.Should().Contain(action =>
            action.Id == "clear-layer:messages" &&
            action.ActionType == "clear-layer");
        snapshot.RecoveryActions.Should().Contain(action =>
            action.Id == "reconnect-endpoint:stage" &&
            action.ActionType == "reconnect-endpoint");
        snapshot.RecoveryActions.Should().Contain(action =>
            action.Id == "resync-host:stage" &&
            action.ActionType == "resync-host");
        snapshot.RecoveryActions.Should().Contain(action =>
            action.Id == "relink-media:walk-in" &&
            action.ActionType == "relink-media");
    }

    [Fact]
    public void LiveMediaQueryService_projects_active_capture_sessions()
    {
        LiveProductionQuerySnapshot query = new()
        {
            Generated = new LiveGeneratedSystemsQuery
            {
                ActiveCaptureSessions =
                [
                    new CaptureSessionState
                    {
                        Metadata = new CaptureSessionMetadata
                        {
                            Id = "stream",
                            Name = "Main Stream",
                        },
                        IsActive = true,
                    },
                ],
            },
        };

        LiveMediaQuerySnapshot snapshot = LiveMediaQueryService.BuildSnapshot(query);

        snapshot.ActiveCaptureSessions.Should().ContainSingle(session => session.Metadata.Id == "stream");
        snapshot.Summary.Should().Contain("Main Stream");
    }

    [Fact]
    public async Task ContentSupportQueryService_projects_audit_and_support_preview()
    {
        Mock<IContentAuditService> audit = new();
        audit.Setup(service => service.RunAuditAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentAuditResult
            {
                ContentRootPath = "C:\\content",
                AuditedAt = "2026-05-02T00:00:00Z",
                Issues = [new AuditIssue { Severity = AuditIssueSeverity.Warning, Code = "missing", Message = "Missing." }],
                BrokenReferences = [new BrokenContentReference { ReferenceKind = "media" }],
                RecoveryActions = [new ContentRecoveryAction { ActionId = "repair" }],
                CleanupCandidates = [new MediaCleanupCandidate { AssetId = "asset", EligibleForCleanup = true }],
            });

        Mock<ISupportPackageService> support = new();
        support.Setup(service => service.PreviewImportAsync("C:\\support.cpsupport", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportPackagePreview
            {
                Changes =
                [
                    new SupportPackagePreviewChange
                    {
                        Kind = SupportPackageChangeKind.Replace,
                        Path = "Configurations/Show.json",
                        IsDestructive = true,
                    },
                ],
            });

        ContentSupportQueryService service = new(audit.Object, support.Object);

        ContentAuditProjection auditProjection = await service.RunAuditAsync();
        SupportPackageImportProjection supportProjection = await service.PreviewSupportPackageImportAsync("C:\\support.cpsupport");

        auditProjection.Summary.Should().Contain("1 issue");
        auditProjection.BrokenReferenceCount.Should().Be(1);
        auditProjection.RecoveryActionCount.Should().Be(1);
        auditProjection.CleanupCandidateCount.Should().Be(1);
        supportProjection.HasDestructiveChanges.Should().BeTrue();
        supportProjection.Summary.Should().Contain("1 replace");
    }
}
