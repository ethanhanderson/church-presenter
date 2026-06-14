# Application Runtime Architecture

This document defines the platform-agnostic application runtime for ChurchPresenter. The runtime is shared conceptually across native desktop apps and is responsible for production semantics, regardless of whether a host is implemented with WinUI, AppKit, SwiftUI, or another native surface.

## Scope

The application runtime owns command dispatch, action expansion, document mutation orchestration, content/media/cue/show/support/output graph queries, live session state, frame resolution, playback coordination, generated providers, diagnostics, recovery, package, sync, migration, audit, and repair workflows.

Native hosts own presentation and OS integration. They should consume runtime contracts rather than recomputing production state.

## Command Boundary

`LiveCommand` is the only boundary for live state changes. Every command should carry command kind, source metadata, target scope, correlation id, audit/provenance metadata, authorization metadata, and optional validation/preview mode.

Command sources include local UI, slide activation, media/audio bin triggers, timer/header actions, messages, props, stage controls, macros, playback markers, timeline/timecode/calendar schedulers, keyboard shortcuts, remotes, browser/API clients, MIDI, DMX, Stream Deck, Network Link, and future device adapters.

Representative commands include take slide, trigger media cue, trigger audio cue, activate video input, show/clear message, show/hide/toggle prop, start/stop/reset timer, activate Look, set stage layout, send stage message, run macro, clear layer, clear group, start/stop capture, reconnect/remap endpoint, relink media, and recover host/player state.

## Command Bus

Commands flow through a single serial bus owned by the runtime. The bus is producer/consumer-friendly: any number of command sources may enqueue, and a single executor drains them in arrival order against the latest `LiveSessionSnapshot`.

- Each command carries `{ Kind, Source, TargetScope, IntentTimestamp, CorrelationId }`. The bus stamps `EnqueueTimestamp` and assigns a monotonic sequence id.
- The bus is back-pressure aware. Producers learn when the executor is overloaded so remote/API sources can degrade gracefully instead of unbounded queue growth.
- Validation and authorization run inside the executor against the snapshot, so a remote command never observes intermediate UI state.
- Replay and audit consumers receive a stamped copy of every accepted command; rejected commands are surfaced with a reason and a correlation id.

The bus is platform-neutral. On Windows the natural mapping is `System.Threading.Channels.Channel<LiveCommand>` drained by a worker that marshals UI mutations back to the host `DispatcherQueue`. On macOS an `AsyncStream<LiveCommand>` drained by a runtime actor with `MainActor.run { ... }` for host updates fits the same shape. The semantic contract is identical and the runtime tests must not assume either binding.

## Action Expansion

`ActionBatch` is the normalized result of a command. A single slide take may update the Slide layer, trigger media or audio, start a timer, set a stage layout, run a macro, and change a Look. A macro may clear media, show a prop, switch Looks, trigger a video input, start capture, and send device commands.

The executor applies a batch against one coherent live-session snapshot so output resolution sees consistent state. Each action inside a batch carries the same `CorrelationId` so layer slot diagnostics, capture, and audit can trace cross-layer cause-and-effect.

## Cue Preparation and Prepared-Cue Cache

Every action that targets an audience or stage layer resolves through cue preparation before it touches the live snapshot. Preparation is the only stage that reads document/asset/theme/support graphs and platform resources.

- `CuePreparation` accepts a `CueRequest { CueKind, LayerKind, ContentRef, Overrides, RoutingHints }` and produces an immutable `PreparedCue` with a `(ContentVersion, ThemeVersion, ResourceStamp, OverridesHash)` stamp.
- Preparation runs on a background worker pool. It is allowed to be asynchronous (resource open, decode, scene compile) and must publish a deterministic snapshot of the cue when complete.
- The `PreparedCueCache` stores prepared cues keyed by `(LayerKind, ContentRef, RoutingHash)`. Entries declare a size cost (decoded bytes, retained surfaces, opened media items) so the cache enforces a memory budget without arbitrary leaks.
- The cache is warmed by intent hints: operator selection, hover, drawer open, look-ahead in the active arrangement, next-cue in a playlist, macro fan-out, and timeline/calendar look-ahead all schedule prepare work in the background.
- A `ContentInvalidated(ContentRef, NewVersion)` signal from the content change bus marks affected entries stale and re-prepares them.

The executor never blocks on preparation for an already-warm cue. Cold-path commands wait for preparation, but the executor must not stall the host UI thread or freeze unrelated layer slots while preparation runs.

Startup media warmup is an application-service responsibility, not a drawer lifecycle side effect. The runtime should schedule warmup for the managed media catalog during bootstrap, coalesce duplicate prepare/thumbnail requests, and keep hot prepared media cues/player handles alive according to cache budget and likely-use hints. Opening the drawer should read the current media-catalog projection and subscribe to thumbnail completion; it should not be the operation that discovers or prepares the library for first live use.

## Layer Routing and Replacement

`LayerRouter` is the only writer of audience layer slots inside `LiveSessionSnapshot`. It accepts prepared cues, applies them to the declared `LayerKind` slot, and emits a new snapshot.

- A take of a prepared cue updates exactly its declared slot. Slide cues never modify the media slot; media cues never modify slide content; audio cues never modify visual slots.
- Clears target one slot (`Clear Slide`, `Clear Media`, `Clear Audio`, ...) or a configured clear group. Clears keep the prior payload reference for opt-in recovery where supported.
- Look changes reroute existing slot payloads to a different per-screen visibility/variant matrix; they do not mutate slot payloads.
- When a `ContentInvalidated` signal produces a new `PreparedCue` for a live slot, the router emits a `ReplaceLayerPayload` action that swaps the slot's payload with the configured replacement transition (default: instant when version-only metadata changed, crossfade when visual or audible content changed).
- Live overrides such as runtime clears persist across snapshot rebuilds until a new payload replaces the cleared slot.

The router records timing metadata (`prepareTimeMs`, `routeTimeMs`) per applied action so the diagnostics surface can show end-to-end take-live latency.

## Live Session Snapshot

Live state is broader than current slide. A snapshot should include selected playlist item and operator cursor, active show item, active audience layer state, active announcement lane, active media/audio/live-video players, timers, messages, props, masks, active Look, stage layout assignments, capture/streaming sessions, endpoint mappings, command provenance, and diagnostics.

Each layer slot in the snapshot carries `{ PreparedCue, EnterTimestamp, ResolvedTransition, ClearOverride, PriorPayload, HostHealth }`. Snapshots are immutable; mutations produce a new snapshot, and the host adapter applies the diff per-slot so unaffected layers keep playing.

Selection is operator context. Live state is production truth. They must remain separate.

## Domain Services

Content services manage libraries, presentations, slides, groups, arrangements, themes, playlist templates, service playlists, external plan links, and generated/imported content provenance.

Media services manage asset identity, storage policy, missing/relinked state, cue defaults and overrides, media/audio playlists, live video and audio input definitions, playback markers, thumbnails, and cleanup references.

Media drawer/card read models are produced from asset/cue identity and availability, not from thumbnail completion. A media item is visible to the operator as soon as the content graph can describe it; thumbnail status is a separate field such as pending, loaded, failed, stale, or unavailable. Thumbnail failures must produce diagnostics and fallback card chrome, not suppress the item from the drawer or from slide-action/cue assignment workflows.

Support services manage Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, masks, labels, and shared workspace defaults.

Integration services represent external systems without making them source-of-truth for local documents: Planning Center-style plans, SongSelect/CCLI, MultiTracks, Bible translations, ProContent-style acquisition, Resi-like streaming, remote/API/control clients, and future services.

Each integration should expose configured/hidden state, sign-in state, entitlement/license state, online/offline health, imported/generated local ids, refresh/reporting status, and diagnostics.

## Output Runtime

The runtime tracks logical screens, endpoints, screen-to-endpoint mappings, endpoint capabilities, endpoint health, placeholder state, and host feedback. Logical screens survive device changes. Endpoints can be attached, missing, replaced, mirrored, grouped, captured, or transport-backed.

Audience output reserves these layer identities: Audio, Messages, Props, Announcements, Slide, Media, Live Video, and Mask.

Each layer includes payload id/kind, source command, source content/cue reference, visibility/routing eligibility, clear/suppressed state, transition metadata, timing/build/playback state where relevant, diagnostics, and optional recoverable prior state.

Each logical `OutputScreen` carries a nominal `RenderSize` in physical pixels. Topology services resolve that size from mapped local displays (native monitor resolution) when a screen is bound to connected hardware; placeholder or unmapped screens keep a configured fallback (typically 1920×1080). The runtime never downscales frames for delivery — native hosts raster and composite at `RenderSize`, then endpoint windows mirror the result.

`LookPreset` defines audience screen routing, layer visibility per screen, screen-specific variants, and optional default clear policy. Activating a Look reroutes existing payloads and must not mutate documents or select content.

Stage state uses stage screens, stage layout assignments, layout elements, data providers, stage messages, stage-only advancement state, and current/next context derived from show/live state.

## Rendering Resolution and Frame Composition

The runtime resolves immutable frames per snapshot:

```text
LiveSessionSnapshot
  -> per-screen layer iteration (Audio, Messages, Props, Announcements, Slide, Media, Live Video)
  -> Look routing + per-screen theme/layout variants
  -> per-screen Mask composition constraint
  -> AudienceFrameSet (one frame per logical audience screen)

LiveSessionSnapshot
  -> stage data provider bindings
  -> StageFrameSet (one frame per stage screen)
```

Frames carry stable layer slot identities, payload ids, transition descriptors, resource references, and diagnostics. Native hosts diff frames per slot and apply only what changed; they never decide routing or visibility.

**Screen-native layer scaling.** Presentation slides compile to scenes at the document's slide size (for example 1920×1080). Hosts must not bitmap-stretch those scenes into a larger output slot. Instead, the host scales scene coordinates and typography into each screen's `RenderSize` before rasterizing (uniform fit with letterboxing when aspect ratios differ) so text and vector geometry stay sharp on high-DPI outputs. Still-image and video layers use per-payload fit policy at the slot size; media defaults to fill/cover so undersized sources (for example 1080p on a 4K monitor) cover the full screen. Masks use the same fit path as slide scenes.

The composer surfaces explicit diagnostics for known interaction edges, such as a slide/presentation background color sitting above a foreground media payload (see `docs/reference/propresenter/output-system/layers-and-looks.md`), so the operator is never surprised by a hidden layer.

## Playback Coordination

Media, audio, and live-video playback are runtime-coordinated and host-applied. The runtime tracks active players, owning layer/cue/action, playback status, transport state, loop/stop/next behavior, marker positions, audio routing, error/health state, and host feedback.

Transport state is projected as lane-specific read models:

- `Presentation` / `MediaFiles` for media-layer playback from Media Bin/direct media drawer take-live actions and presentation media actions.
- `AudioFiles` for audio-layer playback from Audio Bin/audio cues.
- `Announcements` for announcement-layer playback.

The selected transport lane determines which runtime player identities receive play, pause, seek, restart, and marker-jump commands. A transport command may affect more than one host binding when the same logical screen/layer is mirrored to multiple endpoints, but it still represents one semantic lane operation. The runtime must keep file display names, position, duration, playing state, enabled/empty state, and diagnostics separated per lane so announcement or audio playback cannot overwrite presentation-media state.

Native media APIs differ, so host adapters own concrete players. The runtime owns semantic player state and command interpretation. Host adapters must use the platform's gapless playback construct rather than seek-to-zero:

- WinUI: `MediaPlayer` consuming a `MediaPlaybackList` with `AutoRepeatEnabled = true` and a non-zero `MaxPlayedItemsToKeepOpen` so the loop boundary does not reopen the decoder. `MediaSource.OpenAsync` and `MediaPlaybackItem` `OpenOperationCompleted` gate first-frame visibility.
- macOS: `AVQueuePlayer` paired with `AVPlayerLooper` over a template `AVPlayerItem`. `AVPlayerItem.status` and `isPlaybackLikelyToKeepUp` gate first-frame visibility.

Transport state (play, pause, seek, mark) is carried in the prepared cue and routed through the same command bus. Operator transport UI publishes commands rather than mutating host players directly. Output previews and capture consumers should subscribe to the same frame/player identity as the audience output; they must not register separate playback truth or create a second player that only visually resembles the live output. Operator previews may raster at a smaller pixel size for the preview card, but must apply the same frame payloads, routing, transitions, clears, and media player bindings as the canonical screen render.

## Resource Tiering

The runtime references resources through stable identities and tiered caches (see `docs/architecture/file-access-and-cache-resilience.md`). The application layer owns:

- A hot in-memory tier for decoded surfaces, pre-rolled media items, scene snapshots, and prepared cues the pipeline expects to need next.
- A warm on-disk tier for decoded thumbnails, posters, pre-roll segments, and scene caches.
- A cold tier that streams from the original asset path or provider.

Caches are accelerators, never sources of truth. Every entry is keyed by a resource stamp that includes content/version/theme metadata so source edits invalidate the entry deterministically.

Thumbnail caches should store both successful images and recent negative/provider-failure outcomes with short lifetimes so the UI does not repeatedly pay for the same slow miss while an asset remains unchanged. A thumbnail request key includes resource stamp, requested intent/size, platform scale, and provider version. Cache invalidation follows `MediaAssetChanged` and resource-stamp changes; it does not follow drawer open/close state.

## Queries and Read Models

Every host/control surface should use explicit read models for library browsing, playlist browsing, presentations, show view, media/audio bins, live session status, output topology, active Look, stage layouts, timers, messages, props, macros, capture, diagnostics, recovery, and integrations.

Remote/API/control clients should not scrape native UI state.

Read models should publish semantic changes, not every internal refresh. A runtime service may rebuild a `LiveSessionQuerySnapshot` after routing, playback, host feedback, or output-topology events, but it should compare the projected value to the previous query value and notify subscribers only when observable product state changed. Snapshot version changes that do not alter active layers, clear groups, Look routing, stage state, player state, endpoint state, or diagnostics should not force host UI churn.

Collection-shaped read models must preserve item identity whenever membership and ordering are unchanged. For example, output clear actions should keep stable ids for built-in layer clears and configured clear groups; when only `IsActive` changes, the query/update path should update that property on the existing projected item instead of replacing the collection. Collection replacement is reserved for real membership, ordering, label, icon, tint, target-layer, or command-target changes.

This rule is especially important for live-service controls because ProPresenter-style operator surfaces use active visual states as recovery information. Refresh noise must not make the operator wonder whether a layer changed.

## Diagnostics and Recovery

The runtime should produce operator-facing diagnostics for command acceptance/rejection, source provenance, active layers, selected/live distinctions, output health, frame resolution, host apply status, player errors, missing media, timer/provider failures, capture/stream health, integration credentials/entitlements, and sync/package conflicts.

Diagnostics should identify whether a problem is content, cue, command, routing, render, endpoint, host, transport, or integration related.

Recovery is command-driven and should include targeted layer clear, configured clear group, undo/recover cleared payload where supported, stop timeline on clear where configured, endpoint reconnect, endpoint remap, player reset, host resync, relink media, repair support-file references, retry/stop capture, and resolve sync/package conflict.

## Testing Boundary

Most runtime behavior should be testable without native UI: command expansion, action ordering, live session mutation, clear groups, Look routing, stage-only behavior, timer/message/prop state, macro expansion, cue resolution, endpoint mapping, package/sync conflicts, provenance, diagnostics, and recovery suggestions.

Native app tests should focus on host application of runtime contracts, not duplicating semantic runtime tests.
