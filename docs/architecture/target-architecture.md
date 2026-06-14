# Target Architecture

This document defines the platform-agnostic architecture ChurchPresenter should use to implement the ProPresenter-derived feature set documented in `docs/reference/propresenter/`. It is the shared contract for native Windows and macOS apps.

## Product Model

ChurchPresenter should model the product as six connected systems.

1. **Content model**: libraries, presentations, slides, groups, arrangements, themes, generated scripture, imported songs, media assets, and support files.
2. **Cue model**: media cues, audio cues, video inputs, slide actions, playback markers, macros, timers, messages, props, announcements, capture commands, and device/API actions.
3. **Show model**: service playlists, playlist templates, headers, placeholders, Planning Center-style plans, presentation instances, arrangement selections, operator notes, and show-view projections.
4. **Live-state model**: active slide, media, audio, video, message, prop, announcement, timer, macro, stage, Look, capture, and endpoint state.
5. **Output model**: logical screens, fixed output layers, Looks, stage layouts, masks, endpoint mappings, system displays, NDI, SDI, Syphon-like transports, alpha, capture, and streaming.
6. **Control model**: local UI, keyboard, macros, playback markers, timeline/timecode, calendar scheduling, Stream Deck, MIDI, DMX, Network Link, HTTP/TCP APIs, browser control, mobile remote, and future automation sources.

These models are independent but composable. A slide is content. A slide click is a command. A media action is a cue. A Look is output routing. A stage layout is a dashboard composition. A remote tap is another command source.

## Core Rule

Native hosts express operator intent. The shared application runtime owns live production truth.

The native apps may have different UI frameworks, media APIs, display APIs, and packaging systems, but they must share the same semantic model:

```text
Native intent
  -> command or document mutation
  -> shared application runtime
  -> live session snapshot
  -> audience frames / stage frames / diagnostics
  -> native host adapters and output endpoints
```

No platform UI should independently decide what is live, how Looks route layers, what a clear group clears, whether media is missing, or which stage layout is active.

## Layered Architecture

ChurchPresenter should be organized into native host, application runtime, portable content/support, and machine binding layers.

The native host layer owns windows, menus, panels, dialogs, gestures, editor canvases, operator surfaces, output windows, display lifecycle, platform media/view host adapters, accessibility, localization, packaging, activation, and OS integration.

The application runtime layer owns command execution, action expansion, document mutation orchestration, live session state, graph queries, audience/stage frame resolution, media/audio/live-video coordination, generated providers, diagnostics, recovery, audit, sync, migration, and packages.

The portable content/support layer owns presentation documents, libraries, playlists, templates, themes, generated provenance, media metadata, cue metadata, Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, labels, support manifests, and package schemas.

The machine binding layer owns monitor/display bindings, endpoint bindings, device bindings, hardware capability caches, NDI/SDI/Syphon/system transport availability, credentials, tokens, local recents, caches, diagnostics snapshots, and activation state.

## Runtime Command Flow

Every live behavior should flow through one command path:

```text
Command source
  -> LiveCommand
  -> validation / authorization / preview
  -> ActionBatch
  -> LiveSessionSnapshot
  -> AudienceFrameSet + StageFrameSet + Diagnostics
  -> Host adapters / endpoints / previews / capture
```

This path must serve slide activation, media/audio/live-video cues, timers, linked dynamic text, messages, props, announcements, masks, clear layers, clear groups, Look changes, stage layout changes, macros, markers, timeline/timecode/calendar scheduling, device control, APIs, recording, streaming, and future capture consumers.

## Content Architecture

Libraries own presentations. Playlists reference library-owned presentations by occurrence. Presentation documents preserve slides, scene objects, groups, arrangements, notes, themes, actions, timelines, transitions, generated/imported provenance, and resource references.

Generated and imported content must preserve provenance for Bible passages, SongSelect/CCLI imports, MultiTracks data, Planning Center-style plans, ProContent-style assets, local imports, and package imports. Provenance determines refresh, reporting, regeneration, conflict resolution, and local-edit ownership.

## Media and Audio Architecture

The media system distinguishes asset identity from cue usage.

- `MediaAsset` and `AudioAsset` store durable identity, source, resolved location, storage policy, metadata, availability, relink state, and package/sync eligibility.
- `MediaCue` and `AudioCue` store usage-specific playback, role, scaling, crop, in/out, delay, rate, duration, transition, effects, thumbnail, volume, routing, marker actions, and retrigger behavior.
- `LiveVideoInput` and `AudioInput` store logical input identity, platform binding, health, routing, preview, and cue-like trigger behavior.

Media Bin and Audio Bin are cue systems, not file browsers. Audio routing is modeled separately from visual routing so visual layers, capture audio, SDI/NDI audio, output devices, delay, mute, solo, and test diagnostics can evolve independently.

Media-library browsing is a read-model concern over the media graph. The drawer should show every available media asset immediately from manifest/query data, using stable card rows with file name, kind, duration/metadata when known, and fallback type chrome. Thumbnail images are progressive adorners, not prerequisites for card creation, drag/drop, slide-action assignment, or direct take-live actions. Drawer visibility may hint cache work, but closing the drawer must not unload prepared cues, retained media players, or thumbnail cache entries that the runtime still considers hot.

## Output Architecture

Use stable screen and endpoint concepts:

- `OutputScreen`: logical render target such as Main, Stream, Lobby, Confidence, Operator Multiview, or Recording Feed.
- `OutputEndpoint`: delivery target such as local display, placeholder, capture consumer, NDI, SDI, Syphon-like local video, or future transport.
- `ScreenMapping`: one logical screen mapped to zero, one, or many endpoints.
- `EndpointCapability`: fullscreen, transparency, audio, capture, key/fill, mirroring, grouping, edge blend, HDR, bit depth, and platform flags.

Audience output uses fixed layer identities: Audio, Messages, Props, Announcements, Slide, Media, Live Video, and Mask. Every layer is a slot fed by a prepared cue from the unified pipeline; the slot owns the live payload, transition state, and clear/recovery state for that layer independently of every other layer.

`LookPreset` is a routing matrix: rows are layer identities, columns are audience screens, and cells decide whether a layer appears on a screen with optional screen-specific theme/layout variants. Activating a Look reroutes live payloads. It must not rewrite slide content, duplicate presentations, or change selected items.

Each `OutputScreen` also owns a fallback presentation surface that is below every routed layer. For opaque local-display audience outputs the default fallback is black, matching the reference screen-color role in `docs/reference/propresenter/output-system/layers-and-looks.md`: when Slide, Media, Messages, Props, Announcements, Live Video, or Mask content clears or transitions away, the operator should see the screen fallback, not the operating system desktop or a transparent uninitialized host. Alpha/key, capture, and broadcast endpoints may negotiate transparency as an endpoint capability, but that is an explicit endpoint mode rather than a side effect of clearing content layers.

Stage screens use `StageScreen`, `StageLayout`, `StageLayoutElement`, `StageDataProvider`, `StageMessage`, and stage command modes such as `StageOnly` and `StageAndAudience`. Stage layouts render current/next slide text, notes, timers, clocks, video countdown, media markers, audience previews, capture status, labels, chord/chart data, custom text/shapes, and stage messages through provider bindings.

Recording, RTMP streaming, Resi-like streaming, NDI, SDI, Syphon-like output, alpha key, and future transports are endpoint or capture-consumer concerns. They consume resolved screen frames and report health. Capture must not fork a separate render pipeline.

## Unified Output Pipeline

Every content type — slide, media, audio, announcement, prop, mask, message, live video — flows through one pipeline. The pipeline ingests a cue from any command source, prepares an immutable cue payload, routes it to the correct layer slot on every audience or stage frame, and hands the frame to a native host adapter that applies it through platform composition and media primitives.

### Pipeline Stages

```text
Command source (UI tap, slide action, macro, marker, remote, schedule)
  -> CueRequest { LayerKind, ContentRef, Overrides, Transition, Triggers }
  -> CuePreparation (resolve content + theme + tokens + resources, stamp version)
  -> PreparedCue (immutable, layer-typed, references hot resources)
  -> LayerRouter (apply to layer slot inside LiveSessionSnapshot, respecting Looks)
  -> FrameComposer (per logical screen, compose ordered layer payloads + mask + variants)
  -> HostAdapter (apply diff to native composition tree, media players, audio session)
  -> Endpoint (local display, capture consumer, NDI/SDI/Syphon, recording, streaming)
```

Each stage produces an immutable value. Replacement at any stage triggers regeneration of all downstream values for the affected layer slot only; unaffected layers keep playing. There is no separate render path for editor previews, multi-views, recording, or streaming; they consume the same composed frames or layer payloads.

### Cue Shape

A cue is `{ CueKind, LayerKind, ContentRef, ResourceRef, Overrides, Transition, Triggers, RoutingHints, Source, CorrelationId, IntentTimestamp }`. Each content type uses a concrete subtype:

- Slide cue: presentation/slide id, arrangement context, slide actions, slide-attached media references.
- Media cue: media asset reference, role (foreground/background/layer media), in/out, rate, loop, retrigger, scaling/crop.
- Audio cue: audio asset reference, routing, gain, loop, audio category (music/effect/voiceover).
- Live video cue: input id, routing, hold-on-loss, audio linkage.
- Announcement cue: announcement presentation/slide id, lane visibility, advancement mode.
- Prop cue: prop id, show/hide, theme variant.
- Mask cue: mask asset/shape id, per-screen mask routing, alpha mode.
- Message cue: template id, token values, dismiss/expiration, theme variant.

`LayerKind` is the routing key. `RoutingHints` carry optional Look-override, screen-restriction, or stage-only intent. Cues do not encode platform widgets.

### Cue Preparation

`CuePreparation` resolves a cue request into a `PreparedCue` snapshot in a single deterministic pass:

- Look up content (presentation/slide, media asset, theme, prop record, message template) through document services.
- Apply theme, tokens, dynamic text providers, Look-screen variants, and operator overrides.
- Resolve resource references through the resource-stamp registry (see file-access-and-cache-resilience.md), pinning the version that exists when preparation runs.
- For renderable layers, run the scene compiler (text/shape/image/video/web/vector/dynamic-text nodes) and produce a host-neutral scene with stable node ids, z-order, transforms, alpha mode, build/timeline data, and per-node resource references.
- For media/audio/live-video, attach an immutable playback descriptor (source kind, rate, loop, in/out, retrigger, audio routing, transport intent).
- Stamp the prepared cue with a `(ContentVersion, ThemeVersion, ResourceStamp, CueOverridesHash)` tuple so downstream stages and host caches can detect replacement.

Preparation runs on a background worker pool (off the host UI thread) and writes the prepared cue into the prepared-cue cache before routing. Cold-start paths warm the prepared cue ahead of execution where possible: selection changes, hover, drawer open, next-cue lookahead, and macro fan-out all hint preparation.

### Layer Routing and Live State

`LayerRouter` is the only writer of audience layer slots in `LiveSessionSnapshot`. Each layer slot stores `{ PreparedCue, EnterTimestamp, Transition, ClearOverride, PriorPayload }`. Routing rules:

- A cue is applied only to its declared `LayerKind` slot. Slide cues never touch the media slot; media cues never modify slide content.
- Replacing a payload on a layer does not restart other layers. Slides advancing while video plays must not affect the media slot.
- The active Look decides which screens render each layer slot and which slot variants apply, without mutating the slot payload.
- Clear actions target specific layer slots or configured clear groups (`Clear Slide`, `Clear Media`, `Clear All`, custom). Clears keep `PriorPayload` for opt-in recovery where supported.
- Mask is a per-screen composition constraint set, not a content payload. It is routed but not stacked with content; the FrameComposer applies it as an alpha/shape constraint after layer ordering.

The router emits a new `LiveSessionSnapshot` after every applied `ActionBatch`. Diffs between snapshots drive frame composition and host application.

### Operator Live-Status Controls

ProPresenter-style clear controls are command affordances for fixed output layers and configured clear groups. Their visible active state is a projection of runtime layer truth: a button is active only when at least one of its target layers has live content. It is not active because the button was recently pressed, because an output refresh occurred, or because the operator has selected an item that could be taken live.

The runtime should publish stable clear-action read models with durable ids, labels, icons, tint metadata, target layers, and command targets. Host UIs may keep platform-specific button objects or view models, but they should update only the live-status fields when the runtime reports that target layer content changed. Equivalent runtime snapshots should not cause host controls to be removed and recreated.

This preserves the reference behavior from `docs/reference/propresenter/output-system/layers-and-looks.md` and `docs/reference/propresenter/operator-workflows/show-controls-and-recovery.md`: clearing Slide, Media, Announcements, Messages, Props, Audio, Live Video, or a named clear group is independent, and visual indication reflects layer output, not page refresh churn.

### Playback Transport Read Models

Playback controls are live-state projections for explicit playback lanes, not generic media widgets. The runtime should publish separate transport snapshots for:

- **Presentation / Media Files**: media-layer playback from Media Bin cues, direct media drawer take-live actions, and normal presentation media actions routed to the media layer.
- **Audio Files**: audio-layer playback from Audio Bin/audio cues.
- **Announcements**: announcement-layer playback only.

Each snapshot carries the cue display name, enabled/empty state, player count, playing state, position, duration, seek/scrub state, marker metadata, and diagnostics for that lane. Selecting a transport tab changes which snapshot and player set the controls operate on; it must not change Looks, retarget content, clear a layer, or blend announcement/audio state into presentation media.

Transport commands (`Play`, `Pause`, `Seek`, `Restart`, marker jumps) dispatch through the same command/runtime boundary as take-live commands. A command targets the selected transport lane and is fanned out by runtime-owned player identity for that lane. Host controls must not mutate platform players directly or infer transport state from selected content, file paths, or output refresh events.

### Frame Composition

`FrameComposer` builds an immutable `AudienceFrameSet` and `StageFrameSet` from the snapshot:

- For each audience screen the composer iterates the fixed layer stack in canonical order (Audio, Messages, Props, Announcements, Slide, Media, Live Video) honoring Look visibility and per-screen theme/layout variants, then applies the per-screen Mask.
- The slide/presentation background color rule is explicit (see `docs/reference/propresenter/output-system/layers-and-looks.md`): slide and presentation background colors live in the slide layer and sit above the media layer; if both are enabled the background color covers media, so the composer surfaces a diagnostic when that would unexpectedly hide a live media payload.
- Stage frames are composed from stage data providers (current/next, timers, clocks, capture status, messages, audience previews) bound to stage layouts.
- Frames carry stable layer slot identities, payload ids, transition descriptors, resource references, and diagnostics so host adapters can diff and apply per-layer changes without rebuilding the entire frame.
- Operator previews, multiviews, diagnostics snapshots, recording, streaming, and transport outputs consume these same resolved frames or renderer-owned frame/player handles. They must not run a second semantic output pipeline, bind a second independent media player for the same layer, or decide that their preview state is authoritative.

### Host Application

Host adapters apply frames per screen and per layer slot:

- Each layer slot maps to a persistent host surface (a XAML/AppKit element backed by a composition visual or layer). Surfaces are created once per screen and reused across cues.
- Each local output endpoint maps to a stable host window/surface identity while the endpoint remains enabled. Display polling, hot-plug checks, focus restoration, and health feedback may reconcile endpoint state, but an unchanged `(ScreenId, EndpointId, Bounds/Mode)` tuple must not reattach content, recreate layer slots, reapply equivalent frames, or rebind media players.
- The screen fallback/background is owned by the output host root, outside the fixed content-layer stack. Layer clears only hide or transition that layer's slot; they do not remove the root fallback surface.
- Runtime player identity is owned at the live layer slot boundary. Host surfaces may attach one or more platform presentation surfaces to that identity, but a preview surface must observe the same player/frame state as the audience output for the same logical screen and layer. If a platform cannot attach the same player object to multiple surfaces safely, the host adapter should fan out decoded frames or renderer-owned textures while preserving one semantic player and one runtime transport state.
- Surfaces own pre-warmed back buffers and platform resource handles. Replacement of a prepared cue on the slot swaps payloads against the existing surface rather than recreating it.
- Transitions are applied per slot: cuts, fades, dissolves, and motion transitions follow the transition hierarchy (per-cue → per-presentation/per-asset → per-layer global → Show default).
- Loops use the platform gapless-playback primitive (queue/looper with the prepared item retained) so the loop boundary does not reopen the decoder.
- Capture consumers and transports subscribe to the same composed frames; they do not fork their own pipelines.

### Cold-Start, Pre-Warming, and Performance Budgets

The pipeline must take a cue live with no perceptible warm-up across every layer type. To achieve this the pipeline preserves these invariants:

- Prepared cues for the operator's likely next intent are warmed ahead of time: selection, hover, drawer open, look-ahead in the active arrangement, next-cue in a playlist, and macro expansion all hint preparation.
- Resources resolve through the tiered resource cache: a hot in-memory tier holds the bytes/decoded surfaces and pre-rolled media items the pipeline expects to need next; a warm on-disk tier holds decoded frames/thumbnails/pre-roll segments; the cold tier streams from the original file or provider. The cache adapts to available memory and meets a configurable budget (see `file-access-and-cache-resilience.md`).
- Layer host surfaces are kept alive while their owner screen is live and pre-allocate one hidden back buffer per slot for crossfades and cuts so the visible buffer is never recreated mid-transition.
- Media slots use the platform's pre-roll/buffer-ready signal (Windows `MediaPlaybackItem` open + `PlaybackSession.PlaybackState`, macOS `AVPlayerItem.status` + `isPlaybackLikelyToKeepUp`) to gate visibility. The pipeline never reveals a slot until the first frame is decoded.
- Looping uses the platform's gapless construct (Windows `MediaPlaybackList` with `AutoRepeatEnabled = true` and a non-zero `MaxPlayedItemsToKeepOpen`, macOS `AVQueuePlayer` + `AVPlayerLooper`) rather than seek-to-zero on end-of-track.
- The runtime publishes per-stage timing diagnostics (`prepareTimeMs`, `routeTimeMs`, `composeTimeMs`, `applyTimeMs`, `mediaReadyTimeMs`, `loopGapMs`) so regressions are visible.
- Media library startup warms the full managed media catalog independently of drawer visibility. It should resolve media paths, metadata, thumbnails/posters, prepared media cues, and retained playback slots in the background so slide actions, direct media takes, and media-layer cues can use assets as soon as each asset becomes ready.
- Thumbnail generation is progressive and measured. The first paint of media cards must come from the media manifest/read model; thumbnails may arrive later. Platform thumbnail providers should be used as accelerators, but repeated slow fallback matrices are prohibited on the hot drawer path. If the system thumbnail provider misses quickly, adapters should move to deterministic image decode or video first-frame generation instead of trying many equivalent platform thumbnail modes.

### Content-Source Invalidation and Cue Replacement

Cues are immutable, but the content behind a cue is editable. When the underlying source changes, the pipeline replaces the affected cue without tearing down unrelated layers:

- Document/asset/theme/support edits publish a `ContentInvalidated(ContentRef, NewVersion)` event through the content change bus.
- The prepared-cue cache holds an index of prepared cues by `(ContentRef, LayerKind)`. Invalidation marks affected entries stale and re-prepares them in the background.
- For live payloads, the runtime issues a re-prepare and, when a new `PreparedCue` is ready, dispatches a `ReplaceLayerPayload` action that the router applies to the matching layer slot with a configurable replacement transition (default: instant when version is identical, crossfade when visual content changed).
- Examples: a slide's theme background color change replaces the slide-layer payload only; a media library filter change replaces a media-layer payload only; a message template token change replaces the messages-layer payload; an audio asset relink replaces the audio-layer payload. None of these restart other layers.
- The replacement path is the same path used for explicit operator changes; there is no separate "edit live" mode.

### Action and Macro Interaction

Slide actions and macros expand into `ActionBatch` values that may include multiple cues across different layers (for example: take slide, trigger background media, fire a macro, change Look). The router applies a batch against one snapshot so the resulting frames reflect a coherent intent. Cues inside a batch carry the batch correlation id so diagnostics, recovery, and audit can trace cross-layer cause-and-effect.

## Platform Implementation Mapping

The unified pipeline is platform-neutral, but each native host adapter binds to a concrete set of platform primitives. Detailed mapping lives in `docs/architecture/platforms/winui-native-app.md` and `docs/architecture/platforms/macos-native-app.md`. Summary:

| Pipeline element | WinUI / Windows App SDK | macOS / AppKit / SwiftUI |
|---|---|---|
| Command bus | `System.Threading.Channels` + `DispatcherQueue` | `AsyncStream` / `AsyncChannel` + `MainActor` |
| Cue preparation worker | `Task.Run` / thread pool + `IMemoryCache` | Background `Task` / actors + `NSCache` |
| Layer host surface | `Microsoft.UI.Xaml` element backed by `Microsoft.UI.Composition.SpriteVisual` (per slot) | `NSView` with `wantsLayer = true`, dedicated `CALayer`/`CAMetalLayer` per slot |
| Media playback | `Windows.Media.Playback.MediaPlayer` + `MediaPlaybackList` (gapless via `AutoRepeatEnabled` + `MaxPlayedItemsToKeepOpen`), painted into a `CompositionSurfaceBrush` | `AVPlayer` with `AVQueuePlayer` + `AVPlayerLooper`, rendered by `AVPlayerLayer` or `AVSampleBufferDisplayLayer` |
| Audio routing | `MediaPlayer.AudioCategory` + audio render device APIs | `AVAudioSession` + `AVAudioEngine` routing |
| Mask layer | `CompositionMaskBrush` / `CompositionVisualSurface` | `CALayer.mask` / `CAShapeLayer` alpha mask |
| Vector / custom 2D | Win2D `CanvasControl` / `CanvasAnimatedControl` into a composition surface | Core Graphics / Metal into a `CAMetalLayer` |
| Output windows | `AppWindow` + `OverlappedPresenter` / `FullScreenPresenter` + `DisplayAreaWatcher` | `NSWindow` with `collectionBehavior = .fullScreenPrimary` + `NSScreen` monitoring |
| Resource cache | `IMemoryCache` (size-limited, `MemoryCacheEntryOptions` with priorities and post-eviction callbacks) | `NSCache` (`totalCostLimit`, automatic eviction) |
| Observation / live snapshots | `INotifyPropertyChanged` / `IObservable<T>` to host view-models | Swift `Observable` macro / Combine publishers |

The native hosts must not invent layer routing, clear policy, prepared cue lifetime, transition precedence, resource caching policy, or live-state semantics; those live in the shared runtime.

## Rendering Architecture

Rendering is the inner contract of the unified pipeline. It is host-neutral before it is platform-specific.

```text
Presentation / Theme / Runtime tokens / Look variant
  -> Scene compiler
  -> immutable scene snapshot
  -> layer payload (carried inside a PreparedCue)
  -> audience or stage frame
  -> native host adapter (composition surface, media player, audio session)
```

The scene compiler emits typed nodes for text, shapes, raster images, vector graphics, media, live video, web content, groups, and dynamic text. Scenes include stable ids, z-order, transforms, backgrounds, alpha mode, resource dependencies, build/timeline state, diagnostics, and performance metadata.

Host adapters apply scenes using platform technologies. They may cache native visuals and media players keyed by the prepared cue's resource stamp, but scenes and frames are immutable inputs and a cache hit must produce byte-equivalent output to a cold render.

Scenes compile at presentation slide size; hosts scale them into each logical screen's `RenderSize` before rasterizing so typography and vectors stay sharp on high-DPI outputs. Media layers fill the slot per payload fit policy. Operator previews may use a smaller raster size while consuming the same frame and player identities as the canonical screen render.

## Support Systems

Timers are generated runtime state consumed by audience messages, linked text, props, stage layouts, playlist headers, slide actions, macros, and remotes.

Messages are operator-triggered audience overlays. Props are persistent reusable overlays. Announcements are a separate presentation lane routed through Looks. Masks are screen/output composition constraints. Each uses first-class records, layer state, command targets, clear behavior, diagnostics, package/sync references, and render payloads where applicable.

Macros are named action batches. Playback markers are media-timeline action points. Timeline, timecode, and calendar scheduling are command sources that schedule runtime commands instead of manipulating host visuals.

All control surfaces should use shared commands and queries: local shell, keyboard, remotes, browser control, HTTP/TCP API, Network Link, MIDI, DMX, Stream Deck, and future integrations. External index-based protocols can map to stable internal ids and explicit scopes.

## Portability and Packages

Portable support files include presentations, playlists, templates, themes, media metadata, logical screens, Looks, stage layouts, clear groups, timers, messages, props, macros, masks, labels, and shared workspace defaults.

Machine-local bindings include monitor ids, window positions, endpoint bindings, device selections, hardware capability state, credentials, caches, relink hints, recents, activation state, and local diagnostics.

Package shapes are explicit: presentation document, presentation bundle with media, playlist package with optional media, shared support-file package, sync repository snapshot, and machine-local binding export when explicitly requested.

Import, sync, and migration must preview media copy requirements, replace/merge behavior, unresolved dependencies, destructive effects, credential gaps, and machine-binding reconciliation.

## Diagnostics and Recovery

Operators should be able to inspect active layers, selected versus live state, active Look and stage layout assignments, last resolved/applied frames, endpoint health, player state, missing media, timers/messages/props/macros affecting output, capture health, integration/account state, sync/package conflicts, and host adapter errors.

Recovery should be command-driven: clear layer, clear group, recover cleared payload where supported, reconnect/remap endpoint, reset player, relink media, resync host, repair support-file mismatch, retry/stop capture, and resolve package conflicts.

## Platform Implementation Rule

Windows and macOS implementations should differ only below the shared architecture boundary: UI controls, display/window APIs, media playback APIs, hardware/device APIs, packaging, accessibility implementation, and OS lifecycle.

They should not diverge in document semantics, command semantics, live-state semantics, output layer semantics, package semantics, or recovery semantics.
