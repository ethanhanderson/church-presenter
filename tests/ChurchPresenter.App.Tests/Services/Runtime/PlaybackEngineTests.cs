using System.Text.Json;


using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Runtime;

/// <summary>
/// Regression tests for <see cref="PlaybackEngine"/>: operator selection, seek lifecycle,
/// snapshot consistency, and backward-compatible <see cref="ILiveSessionService"/> parity.
/// </summary>
public sealed class PlaybackEngineTests
{
    private static PlaybackEngine Create() =>
        new(NullLogger<PlaybackEngine>.Instance, new ShowTransitionDefaults());

    private static PresentationDocument MinimalDoc(params SlideDto[] slides)
    {
        var layers = JsonSerializer.SerializeToElement(Array.Empty<object>());
        var resolvedSlides = slides.Length == 0
            ? new[]
            {
                new SlideDto { Id = "s1", Type = "blank", Layers = layers },
                new SlideDto { Id = "s2", Type = "blank", Layers = layers },
            }
            : slides.Select(slide =>
            {
                slide.Layers = layers;
                return slide;
            }).ToArray();

        return new PresentationDocument
        {
            SourcePath = @"C:\t\a.cpres",
            Manifest = new PresentationManifestDto { Title = "T", PresentationId = "p1" },
            Slides = resolvedSlides.ToList(),
        };
    }

    // ── ILiveSessionService backward compatibility ────────────────────────────

    [Fact]
    public void SetAudienceEnabled_is_routing_only_and_does_not_raise_engine_events()
    {
        var engine = Create();
        var changed = 0;
        var stateChanged = 0;
        engine.Changed += (_, _) => changed++;
        engine.StateChanged += (_, _) => stateChanged++;

        engine.SetAudienceEnabled(false);
        changed.Should().Be(0);
        stateChanged.Should().Be(0);

        engine.SetAudienceEnabled(true);
        changed.Should().Be(0);
        stateChanged.Should().Be(0);
        engine.IsAudienceEnabled.Should().BeTrue();
        engine.CurrentState.IsAudienceEnabled.Should().BeTrue();

        engine.SetAudienceEnabled(true);
        changed.Should().Be(0);
        stateChanged.Should().Be(0);
    }

    [Fact]
    public void SetStageEnabled_raises_Changed_only_when_value_changes()
    {
        var engine = Create();
        var raised = 0;
        engine.Changed += (_, _) => raised++;

        engine.SetStageEnabled(false);
        raised.Should().Be(0);

        engine.SetStageEnabled(true);
        raised.Should().Be(1);
        engine.IsStageEnabled.Should().BeTrue();

        engine.SetStageEnabled(true);
        raised.Should().Be(1, "no-op when value unchanged");

        engine.SetStageEnabled(false);
        raised.Should().Be(2);
        engine.IsStageEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetBlackout_and_SetClear_are_mutually_exclusive()
    {
        var engine = Create();

        engine.SetBlackout(true);
        engine.IsClear.Should().BeFalse();
        engine.IsBlackout.Should().BeTrue();

        engine.SetClear(true);
        engine.IsBlackout.Should().BeFalse();
        engine.IsClear.Should().BeTrue();
    }

    [Fact]
    public void PlayMediaCue_updates_program_media_layer_and_unsuppresses_media()
    {
        var engine = Create();
        engine.GoLive(MinimalDoc(), @"C:\t\a.cpres");
        engine.ClearMedia();
        engine.FinishClearMedia();

        engine.PlayMediaCue(new SlideMediaCue
        {
            Id = "cue-1",
            MediaId = @"C:\media\walkin.mp4",
            MediaType = "video",
            Target = "mediaUnderlay",
            Autoplay = true,
        });

        engine.CurrentState.MediaLayers.MediaUnderlay.Should().NotBeNull();
        engine.CurrentState.MediaLayers.MediaUnderlay!.MediaId.Should().Be(@"C:\media\walkin.mp4");
        engine.CurrentState.Suppress.Media.Should().BeFalse();
        engine.CurrentState.CanUndoClearMedia.Should().BeFalse();
    }

    [Fact]
    public void PlayMediaCue_records_resolved_path_on_layer_when_provided()
    {
        var engine = Create();
        engine.GoLive(MinimalDoc(), @"C:\t\a.cpres");

        engine.PlayMediaCue(new SlideMediaCue
        {
            Id = "cue-1",
            MediaId = "Media/Files/walkin.mp4",
            MediaType = "video",
            DisplayName = "Walk In",
            Target = "mediaUnderlay",
        }, resolvedMediaPath: @"D:\library\walkin.mp4");

        engine.CurrentState.MediaLayers.MediaUnderlay.Should().NotBeNull();
        engine.CurrentState.MediaLayers.MediaUnderlay!.DisplayName.Should().Be("Walk In");
        engine.CurrentState.MediaLayers.MediaUnderlay!.ResolvedSourcePath.Should().Be(@"D:\library\walkin.mp4");
    }

    [Fact]
    public void PlayMediaCue_is_idempotent_when_layer_payload_is_unchanged()
    {
        var engine = Create();
        engine.GoLive(MinimalDoc(), @"C:\t\a.cpres");

        var cue = new SlideMediaCue
        {
            Id = "cue-1",
            MediaId = @"C:\media\walkin.mp4",
            MediaType = "video",
            Target = "mediaUnderlay",
            Autoplay = true,
        };

        engine.PlayMediaCue(cue, resolvedMediaPath: @"D:\resolved\walkin.mp4");

        var snapshotAfterFirst = engine.CurrentState;
        var raised = 0;
        engine.StateChanged += (_, _) => raised++;

        engine.PlayMediaCue(cue, resolvedMediaPath: @"D:\resolved\walkin.mp4");

        raised.Should().Be(0, "duplicate cue must not re-raise playback state");
        engine.CurrentState.Should().BeSameAs(snapshotAfterFirst, "snapshot should be untouched when media is unchanged");
    }

    [Fact]
    public void GoToSlide_does_not_re_raise_state_when_same_slide_build_and_media_unchanged()
    {
        var engine = Create();
        var doc = MinimalDoc(
            new SlideDto
            {
                Id = "s1",
                Type = "blank",
                MediaCues =
                [
                    new SlideMediaCueDto
                    {
                        Id = "slide-cue",
                        MediaId = @"C:\media\announcement.png",
                        MediaType = "image",
                        Target = "mediaUnderlay",
                    },
                ],
            },
            new SlideDto { Id = "s2", Type = "blank" });

        engine.GoLive(doc, doc.SourcePath);
        engine.GoToSlide("s1");

        var snapshotAfterFirst = engine.CurrentState;
        var raised = 0;
        engine.StateChanged += (_, _) => raised++;

        engine.GoToSlide("s1");

        raised.Should().Be(0, "re-selecting the current slide should not churn playback state");
        engine.CurrentState.Should().BeSameAs(snapshotAfterFirst);
    }

    [Fact]
    public void GoToSlide_matches_slide_id_ignoring_case()
    {
        var engine = Create();
        var doc = MinimalDoc(
            new SlideDto { Id = "Slide-A", Type = "blank" },
            new SlideDto { Id = "s2", Type = "blank" });

        engine.GoLive(doc, doc.SourcePath);
        engine.GoToSlide("slide-a");

        engine.CurrentState.CurrentSlideId.Should().Be("Slide-A");
    }

    [Fact]
    public void GoToSlide_with_media_cues_replaces_only_the_cued_target()
    {
        var engine = Create();
        var doc = MinimalDoc(
            new SlideDto
            {
                Id = "s1",
                Type = "blank",
                MediaCues =
                [
                    new SlideMediaCueDto
                    {
                        Id = "slide-cue",
                        MediaId = @"C:\media\announcement.png",
                        MediaType = "image",
                        Target = "mediaUnderlay",
                    },
                ],
            },
            new SlideDto { Id = "s2", Type = "blank" });

        engine.GoLive(doc, doc.SourcePath);
        engine.PlayMediaCue(new SlideMediaCue
        {
            Id = "audio-cue",
            MediaId = @"C:\media\bed.mp3",
            MediaType = "audio",
            Target = "audio",
            Autoplay = true,
        });

        engine.GoToSlide("s1");

        engine.CurrentState.MediaLayers.MediaUnderlay?.MediaId.Should().Be(@"C:\media\announcement.png");
        engine.CurrentState.MediaLayers.Audio?.MediaId.Should().Be(@"C:\media\bed.mp3");

        engine.GoToSlide("s2");

        engine.CurrentState.MediaLayers.MediaUnderlay?.MediaId.Should().Be(@"C:\media\announcement.png");
        engine.CurrentState.MediaLayers.Audio?.MediaId.Should().Be(@"C:\media\bed.mp3");
    }

    [Fact]
    public void SwitchToPresentation_preserves_existing_media_layers_when_new_slide_has_no_media_cues()
    {
        var engine = Create();
        var firstDoc = MinimalDoc(new SlideDto { Id = "a1", Type = "blank" });
        var secondDoc = new PresentationDocument
        {
            SourcePath = @"C:\t\b.cpres",
            Manifest = new PresentationManifestDto { Title = "B", PresentationId = "p2" },
            Slides =
            [
                new SlideDto
                {
                    Id = "b1",
                    Type = "blank",
                    Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()),
                },
            ],
        };

        engine.GoLive(firstDoc, firstDoc.SourcePath);
        engine.PlayMediaCue(new SlideMediaCue
        {
            Id = "media-1",
            MediaId = @"C:\media\walkin.mp4",
            MediaType = "video",
            Target = "mediaUnderlay",
            Autoplay = true,
        });

        engine.SwitchToPresentation(secondDoc, secondDoc.SourcePath, "b1");

        engine.CurrentState.PresentationPath.Should().Be(secondDoc.SourcePath);
        engine.CurrentState.CurrentSlideId.Should().Be("b1");
        engine.CurrentState.MediaLayers.MediaUnderlay.Should().NotBeNull();
        engine.CurrentState.MediaLayers.MediaUnderlay!.MediaId.Should().Be(@"C:\media\walkin.mp4");
    }

    [Fact]
    public void EnterPreparedSlideCue_tracks_the_live_slide_instance_key_in_engine_state()
    {
        var engine = Create();
        var doc = MinimalDoc(new SlideDto { Id = "s1", Type = "blank" });

        engine.EnterPreparedSlideCue(new PreparedSlideCue
        {
            Presentation = doc,
            PresentationPath = doc.SourcePath,
            SlideId = "s1",
            InstanceKey = "chorus_1_s1",
            SlideIndex = 0,
            SlideDocument = doc.Slides[0],
            MediaLayers = new MediaLayersState(),
        });

        engine.CurrentSlideId.Should().Be("s1");
        engine.CurrentSlideInstanceKey.Should().Be("chorus_1_s1");
        engine.CurrentState.CurrentSlideInstanceKey.Should().Be("chorus_1_s1");
    }

    [Fact]
    public void EnterPreparedSlideCue_raises_state_when_only_the_instance_key_changes()
    {
        var engine = Create();
        var doc = MinimalDoc(new SlideDto { Id = "s1", Type = "blank" });

        engine.EnterPreparedSlideCue(new PreparedSlideCue
        {
            Presentation = doc,
            PresentationPath = doc.SourcePath,
            SlideId = "s1",
            InstanceKey = "chorus_1_s1",
            SlideIndex = 0,
            SlideDocument = doc.Slides[0],
            MediaLayers = new MediaLayersState(),
        });

        var raised = 0;
        engine.StateChanged += (_, _) => raised++;

        engine.EnterPreparedSlideCue(new PreparedSlideCue
        {
            Presentation = doc,
            PresentationPath = doc.SourcePath,
            SlideId = "s1",
            InstanceKey = "chorus_2_s1",
            SlideIndex = 0,
            SlideDocument = doc.Slides[0],
            MediaLayers = new MediaLayersState(),
        });

        raised.Should().Be(1, "live occurrence changes must propagate even when the rendered slide is identical");
        engine.CurrentState.CurrentSlideInstanceKey.Should().Be("chorus_2_s1");
    }

    // ── Snapshot consistency ──────────────────────────────────────────────────

    [Fact]
    public void CurrentState_reflects_audience_enabled_toggle()
    {
        var engine = Create();

        engine.CurrentState.IsAudienceEnabled.Should().BeFalse();

        engine.SetAudienceEnabled(true);
        engine.CurrentState.IsAudienceEnabled.Should().BeTrue();
    }

    [Fact]
    public void CurrentState_is_replaced_on_every_mutation()
    {
        var engine = Create();
        var before = engine.CurrentState;

        engine.SetStageEnabled(true);
        var after = engine.CurrentState;

        after.Should().NotBeSameAs(before, "a new snapshot must be created after each mutation");
    }

    [Fact]
    public void StateChanged_fires_with_updated_snapshot()
    {
        var engine = Create();
        PlaybackState? received = null;
        engine.StateChanged += (_, args) => received = args.State;

        engine.SetStageEnabled(true);

        received.Should().NotBeNull();
        received!.IsStageEnabled.Should().BeTrue();
    }

    [Fact]
    public void Both_Changed_and_StateChanged_fire_on_same_mutation()
    {
        var engine = Create();
        var changedFired = false;
        var stateChangedFired = false;
        engine.Changed += (_, _) => changedFired = true;
        engine.StateChanged += (_, _) => stateChangedFired = true;

        engine.SetBlackout(true);

        changedFired.Should().BeTrue();
        stateChangedFired.Should().BeTrue();
    }

    [Fact]
    public void ClearPresentation_pair_publishes_transition_start_then_suppress_state()
    {
        var engine = Create();
        engine.GoLive(MinimalDoc(), @"C:\t\a.cpres");
        engine.GoToSlide("s1");

        var events = 0;
        engine.StateChanged += (_, _) => events++;

        engine.ClearPresentation();
        events.Should().Be(1, "clear transition should publish the animated clear-out state");
        engine.IsClearing.Presentation.Should().BeTrue();
        engine.Suppress.Presentation.Should().BeFalse();

        engine.FinishClearPresentation();
        events.Should().Be(2);
        engine.Suppress.Presentation.Should().BeTrue();
    }

    [Fact]
    public void ClearMedia_pair_raises_StateChanged_once_when_program_media_exists()
    {
        var engine = Create();
        engine.GoLive(MinimalDoc(), @"C:\t\a.cpres");
        engine.PlayMediaCue(new SlideMediaCue
        {
            Id = "cue-1",
            MediaId = @"C:\media\x.png",
            MediaType = "image",
            Target = "mediaOverlay",
        });

        var events = 0;
        engine.StateChanged += (_, _) => events++;

        engine.ClearMedia();
        events.Should().Be(0);

        engine.FinishClearMedia();
        events.Should().Be(1);
    }

    // ── Operator selection ────────────────────────────────────────────────────

    [Fact]
    public void SelectSlide_sets_operator_cursor()
    {
        var engine = Create();

        engine.SelectSlide("/path/foo.cpres", "slide-1", null);

        var cursor = engine.CurrentState.OperatorCursor;
        cursor.HasSelection.Should().BeTrue();
        cursor.SlideId.Should().Be("slide-1");
        cursor.PresentationPath.Should().Be("/path/foo.cpres");
        cursor.InstanceKey.Should().Be("slide-1", "defaults to slideId when no instanceKey supplied");
        cursor.Source.Should().Be(SelectionSource.Operator);
    }

    [Fact]
    public void SelectSlide_with_explicit_instanceKey_stores_it()
    {
        var engine = Create();

        engine.SelectSlide(null, "slide-1", "arrangement-instance-42", SelectionSource.Seek);

        var cursor = engine.CurrentState.OperatorCursor;
        cursor.InstanceKey.Should().Be("arrangement-instance-42");
        cursor.Source.Should().Be(SelectionSource.Seek);
    }

    [Fact]
    public void ClearSelection_resets_cursor_to_empty()
    {
        var engine = Create();
        engine.SelectSlide(null, "slide-1", null);

        engine.ClearSelection();

        engine.CurrentState.OperatorCursor.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void ClearSelection_does_not_fire_events_when_already_empty()
    {
        var engine = Create();
        var raised = 0;
        engine.StateChanged += (_, _) => raised++;

        engine.ClearSelection();

        raised.Should().Be(0, "no event when cursor is already empty");
    }

    [Fact]
    public void SetUserOverrideSelection_toggles_snapshot_flag()
    {
        var engine = Create();

        engine.CurrentState.UserOverrideSelection.Should().BeFalse();

        engine.SetUserOverrideSelection(true);
        engine.CurrentState.UserOverrideSelection.Should().BeTrue();

        engine.SetUserOverrideSelection(true);
        engine.CurrentState.UserOverrideSelection.Should().BeTrue();
    }

    [Fact]
    public void SetUserOverrideSelection_fires_StateChanged_only_when_value_changes()
    {
        var engine = Create();
        var raised = 0;
        engine.StateChanged += (_, _) => raised++;

        engine.SetUserOverrideSelection(false);
        raised.Should().Be(0, "already false");

        engine.SetUserOverrideSelection(true);
        raised.Should().Be(1);
    }

    // ── SelectSlide with blank/null id routes to ClearSelection ──────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SelectSlide_with_blank_id_clears_selection(string? blankId)
    {
        var engine = Create();
        engine.SelectSlide(null, "slide-1", null);

        engine.SelectSlide(null, blankId!, null);

        engine.CurrentState.OperatorCursor.HasSelection.Should().BeFalse();
    }

    // ── Seek lifecycle ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartSeekAsync_invokes_stepProvider_once_immediately()
    {
        var engine = Create();
        var calls = 0;

        await engine.StartSeekAsync(1, _ =>
        {
            calls++;
            return Task.FromResult(SlideSeekStepResult.None);
        });

        calls.Should().Be(1, "the initial step fires immediately before the loop starts");
    }

    [Fact]
    public async Task StartSeekAsync_does_not_loop_when_initial_step_returns_no_move()
    {
        var engine = Create();
        var calls = 0;

        await engine.StartSeekAsync(1, _ =>
        {
            calls++;
            return Task.FromResult(SlideSeekStepResult.None);
        });

        await Task.Delay(80);
        calls.Should().Be(1, "loop must not fire if initial step returned Moved=false");
    }

    [Fact]
    public async Task StopSeek_cancels_running_seek()
    {
        var engine = Create();
        var calls = 0;

        await engine.StartSeekAsync(1, _ =>
        {
            calls++;
            return Task.FromResult(SlideSeekStepResult.FromDelay(TimeSpan.FromMilliseconds(10)));
        });

        await Task.Delay(15);
        engine.StopSeek();
        var callsAfterStop = calls;

        await Task.Delay(60);
        calls.Should().Be(callsAfterStop, "no more steps should fire after StopSeek");
    }

    [Fact]
    public async Task StartSeekAsync_with_same_direction_while_running_returns_true_without_restart()
    {
        var engine = Create();
        var starts = 0;

        await engine.StartSeekAsync(1, _ =>
        {
            starts++;
            return Task.FromResult(SlideSeekStepResult.FromDelay(TimeSpan.FromMilliseconds(200)));
        });
        var startsAfterFirst = starts;

        var result = await engine.StartSeekAsync(1, _ =>
        {
            starts++;
            return Task.FromResult(SlideSeekStepResult.None);
        });

        result.Should().BeTrue();
        starts.Should().Be(startsAfterFirst, "duplicate same-direction start should be ignored");

        engine.StopSeek();
    }

    [Fact]
    public async Task StartSeekAsync_with_opposite_direction_restarts_seek()
    {
        var engine = Create();
        var firstProviderCalls = 0;
        var secondProviderCalls = 0;

        await engine.StartSeekAsync(1, _ =>
        {
            firstProviderCalls++;
            return Task.FromResult(SlideSeekStepResult.FromDelay(TimeSpan.FromMilliseconds(200)));
        });

        await engine.StartSeekAsync(-1, _ =>
        {
            secondProviderCalls++;
            return Task.FromResult(SlideSeekStepResult.None);
        });

        firstProviderCalls.Should().Be(1, "first provider's loop should be cancelled");
        secondProviderCalls.Should().Be(1, "second provider fires immediately");

        engine.StopSeek();
    }

    // ── IPlaybackEngine / ILiveSessionService dual-interface ─────────────────

    [Fact]
    public void PlaybackEngine_satisfies_ILiveSessionService_contract()
    {
        var engine = Create();

        ILiveSessionService svc = engine;
        svc.IsAudienceEnabled.Should().BeFalse();
        svc.IsStageEnabled.Should().BeFalse();

        svc.SetAudienceEnabled(true);
        svc.IsAudienceEnabled.Should().BeTrue();
        engine.IsAudienceEnabled.Should().BeTrue("engine field must reflect change via interface");
    }

    [Fact]
    public void PlaybackEngine_satisfies_IPlaybackEngine_contract()
    {
        var engine = Create();

        IPlaybackEngine iEngine = engine;
        iEngine.CurrentState.Should().NotBeNull();
        iEngine.CurrentState.IsAudienceEnabled.Should().BeFalse();
    }

    // ── SwitchToPresentation ─────────────────────────────────────────────────

    [Fact]
    public void SwitchToPresentation_navigates_to_specified_slide_in_single_event()
    {
        var engine = Create();
        var docA = MinimalDoc();
        engine.GoLive(docA, @"C:\t\a.cpres");
        engine.GoToSlide("s1");

        var docB = new PresentationDocument
        {
            SourcePath = @"C:\t\b.cpres",
            Manifest = new PresentationManifestDto { Title = "B", PresentationId = "p2" },
            Slides = new List<SlideDto>
            {
                new() { Id = "b1", Type = "blank", Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()) },
                new() { Id = "b2", Type = "blank", Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()) },
            },
        };

        var events = 0;
        engine.StateChanged += (_, _) => events++;

        engine.SwitchToPresentation(docB, @"C:\t\b.cpres", "b2");

        // Exactly one state event – no blank-frame interstitial.
        events.Should().Be(1, "SwitchToPresentation should fire exactly one state event");
        engine.CurrentSlideId.Should().Be("b2");
        engine.PresentationPath.Should().Be(@"C:\t\b.cpres");
        engine.IsLive.Should().BeTrue();
    }

    [Fact]
    public void SwitchToPresentation_resets_suppress_and_clear_state()
    {
        var engine = Create();
        var docA = MinimalDoc();
        engine.GoLive(docA, @"C:\t\a.cpres");
        engine.GoToSlide("s1");
        engine.ClearPresentation();
        engine.FinishClearPresentation();
        engine.Suppress.Presentation.Should().BeTrue();

        var docB = new PresentationDocument
        {
            SourcePath = @"C:\t\b.cpres",
            Manifest = new PresentationManifestDto { Title = "B", PresentationId = "p2" },
            Slides = new List<SlideDto>
            {
                new() { Id = "b1", Type = "blank", Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()) },
            },
        };

        engine.SwitchToPresentation(docB, @"C:\t\b.cpres", "b1");

        engine.Suppress.Presentation.Should().BeFalse("suppress resets on presentation switch");
        engine.IsClearing.Presentation.Should().BeFalse();
    }

    [Fact]
    public void SwitchToPresentation_keeps_audience_and_stage_flags()
    {
        var engine = Create();
        engine.SetAudienceEnabled(true);
        engine.SetStageEnabled(true);
        var docA = MinimalDoc();
        engine.GoLive(docA, @"C:\t\a.cpres");

        var docB = new PresentationDocument
        {
            SourcePath = @"C:\t\b.cpres",
            Manifest = new PresentationManifestDto { Title = "B", PresentationId = "p2" },
            Slides = new List<SlideDto>
            {
                new() { Id = "b1", Type = "blank", Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()) },
            },
        };

        engine.SwitchToPresentation(docB, @"C:\t\b.cpres", "b1");

        engine.IsAudienceEnabled.Should().BeTrue("audience flag preserved across presentation switch");
        engine.IsStageEnabled.Should().BeTrue("stage flag preserved across presentation switch");
    }

    // ── ClearPresentation guard ──────────────────────────────────────────────

    [Fact]
    public void ClearPresentation_applies_suppress_even_when_no_slide_is_selected()
    {
        var engine = Create();
        var doc = MinimalDoc();
        engine.GoLive(doc, @"C:\t\a.cpres");
        // Deliberately do NOT call GoToSlide — CurrentSlideId is null.

        engine.ClearPresentation();
        engine.FinishClearPresentation();

        engine.Suppress.Presentation.Should().BeTrue("clear should work without a prior GoToSlide");
        engine.CanUndoClearPresentation.Should().BeFalse("no slide to undo to when none was selected");
    }

    [Fact]
    public void ClearPresentation_stores_undo_state_when_slide_is_selected()
    {
        var engine = Create();
        var doc = MinimalDoc();
        engine.GoLive(doc, @"C:\t\a.cpres");
        engine.GoToSlide("s1");

        engine.ClearPresentation();
        engine.FinishClearPresentation();

        engine.Suppress.Presentation.Should().BeTrue();
        engine.CurrentSlideId.Should().Be("s1", "slide clear keeps the program slide cued");
        engine.CanUndoClearPresentation.Should().BeTrue("undo is available when a slide was shown");

        engine.UndoClearPresentation();
        engine.CurrentSlideId.Should().Be("s1", "undo only lifts slide suppress");
        engine.Suppress.Presentation.Should().BeFalse();
    }

    [Fact]
    public void ClearPresentation_raises_state_changed_when_clear_transition_starts()
    {
        var engine = Create();
        var doc = MinimalDoc();
        engine.GoLive(doc, @"C:\t\a.cpres");
        engine.GoToSlide("s1");

        var events = 0;
        engine.StateChanged += (_, _) => events++;

        engine.ClearPresentation();

        events.Should().Be(1);
        engine.IsClearing.Presentation.Should().BeTrue();
        engine.Suppress.Presentation.Should().BeFalse();
    }
}