# Application Runtime Architecture

This document defines the platform-agnostic application runtime for ChurchPresenter. The runtime is shared conceptually across native desktop apps and is responsible for production semantics, regardless of whether a host is implemented with WinUI, AppKit, SwiftUI, or another native surface.

## Scope

The application runtime owns command dispatch, action expansion, document mutation orchestration, content/media/cue/show/support/output graph queries, live session state, frame resolution, playback coordination, generated providers, diagnostics, recovery, package, sync, migration, audit, and repair workflows.

Native hosts own presentation and OS integration. They should consume runtime contracts rather than recomputing production state.

## Command Boundary

`LiveCommand` is the only boundary for live state changes. Every command should carry command kind, source metadata, target scope, correlation id, audit/provenance metadata, authorization metadata, and optional validation/preview mode.

Command sources include local UI, slide activation, media/audio bin triggers, timer/header actions, messages, props, stage controls, macros, playback markers, timeline/timecode/calendar schedulers, keyboard shortcuts, remotes, browser/API clients, MIDI, DMX, Stream Deck, Network Link, and future device adapters.

Representative commands include take slide, trigger media cue, trigger audio cue, activate video input, show/clear message, show/hide/toggle prop, start/stop/reset timer, activate Look, set stage layout, send stage message, run macro, clear layer, clear group, start/stop capture, reconnect/remap endpoint, relink media, and recover host/player state.

## Action Expansion

`ActionBatch` is the normalized result of a command. A single slide take may update the Slide layer, trigger media or audio, start a timer, set a stage layout, run a macro, and change a Look. A macro may clear media, show a prop, switch Looks, trigger a video input, start capture, and send device commands.

The executor applies a batch against one coherent live-session snapshot so output resolution sees consistent state.

## Live Session Snapshot

Live state is broader than current slide. A snapshot should include selected playlist item and operator cursor, active show item, active audience layer state, active announcement lane, active media/audio/live-video players, timers, messages, props, masks, active Look, stage layout assignments, capture/streaming sessions, endpoint mappings, command provenance, and diagnostics.

Selection is operator context. Live state is production truth. They must remain separate.

## Domain Services

Content services manage libraries, presentations, slides, groups, arrangements, themes, playlist templates, service playlists, external plan links, and generated/imported content provenance.

Media services manage asset identity, storage policy, missing/relinked state, cue defaults and overrides, media/audio playlists, live video and audio input definitions, playback markers, thumbnails, and cleanup references.

Support services manage Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, masks, labels, and shared workspace defaults.

Integration services represent external systems without making them source-of-truth for local documents: Planning Center-style plans, SongSelect/CCLI, MultiTracks, Bible translations, ProContent-style acquisition, Resi-like streaming, remote/API/control clients, and future services.

Each integration should expose configured/hidden state, sign-in state, entitlement/license state, online/offline health, imported/generated local ids, refresh/reporting status, and diagnostics.

## Output Runtime

The runtime tracks logical screens, endpoints, screen-to-endpoint mappings, endpoint capabilities, endpoint health, placeholder state, and host feedback. Logical screens survive device changes. Endpoints can be attached, missing, replaced, mirrored, grouped, captured, or transport-backed.

Audience output reserves these layer identities: Audio, Messages, Props, Announcements, Slide, Media, Live Video, and Mask.

Each layer includes payload id/kind, source command, source content/cue reference, visibility/routing eligibility, clear/suppressed state, transition metadata, timing/build/playback state where relevant, diagnostics, and optional recoverable prior state.

`LookPreset` defines audience screen routing, layer visibility per screen, screen-specific variants, and optional default clear policy. Activating a Look reroutes existing payloads and must not mutate documents or select content.

Stage state uses stage screens, stage layout assignments, layout elements, data providers, stage messages, stage-only advancement state, and current/next context derived from show/live state.

## Rendering Resolution

The runtime resolves immutable frames:

```text
LiveSessionSnapshot
  -> audience layer payloads
  -> Look routing
  -> AudienceFrameSet

LiveSessionSnapshot
  -> stage data providers
  -> StageFrameSet
```

Native hosts can diff and apply frames, but they do not decide routing.

## Playback Coordination

Media, audio, and live-video playback are runtime-coordinated and host-applied. The runtime tracks active players, owning layer/cue/action, playback status, transport state, loop/stop/next behavior, marker positions, audio routing, error/health state, and host feedback.

Native media APIs differ, so host adapters own concrete players. The runtime owns semantic player state and command interpretation.

## Queries and Read Models

Every host/control surface should use explicit read models for library browsing, playlist browsing, presentations, show view, media/audio bins, live session status, output topology, active Look, stage layouts, timers, messages, props, macros, capture, diagnostics, recovery, and integrations.

Remote/API/control clients should not scrape native UI state.

## Diagnostics and Recovery

The runtime should produce operator-facing diagnostics for command acceptance/rejection, source provenance, active layers, selected/live distinctions, output health, frame resolution, host apply status, player errors, missing media, timer/provider failures, capture/stream health, integration credentials/entitlements, and sync/package conflicts.

Diagnostics should identify whether a problem is content, cue, command, routing, render, endpoint, host, transport, or integration related.

Recovery is command-driven and should include targeted layer clear, configured clear group, undo/recover cleared payload where supported, stop timeline on clear where configured, endpoint reconnect, endpoint remap, player reset, host resync, relink media, repair support-file references, retry/stop capture, and resolve sync/package conflict.

## Testing Boundary

Most runtime behavior should be testable without native UI: command expansion, action ordering, live session mutation, clear groups, Look routing, stage-only behavior, timer/message/prop state, macro expansion, cue resolution, endpoint mapping, package/sync conflicts, provenance, diagnostics, and recovery suggestions.

Native app tests should focus on host application of runtime contracts, not duplicating semantic runtime tests.
