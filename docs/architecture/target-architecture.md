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

## Output Architecture

Use stable screen and endpoint concepts:

- `OutputScreen`: logical render target such as Main, Stream, Lobby, Confidence, Operator Multiview, or Recording Feed.
- `OutputEndpoint`: delivery target such as local display, placeholder, capture consumer, NDI, SDI, Syphon-like local video, or future transport.
- `ScreenMapping`: one logical screen mapped to zero, one, or many endpoints.
- `EndpointCapability`: fullscreen, transparency, audio, capture, key/fill, mirroring, grouping, edge blend, HDR, bit depth, and platform flags.

Audience output uses fixed layer identities: Audio, Messages, Props, Announcements, Slide, Media, Live Video, and Mask.

`LookPreset` is a routing matrix: rows are layer identities, columns are audience screens, and cells decide whether a layer appears on a screen with optional screen-specific theme/layout variants. Activating a Look reroutes live payloads. It must not rewrite slide content, duplicate presentations, or change selected items.

Stage screens use `StageScreen`, `StageLayout`, `StageLayoutElement`, `StageDataProvider`, `StageMessage`, and stage command modes such as `StageOnly` and `StageAndAudience`. Stage layouts render current/next slide text, notes, timers, clocks, video countdown, media markers, audience previews, capture status, labels, chord/chart data, custom text/shapes, and stage messages through provider bindings.

Recording, RTMP streaming, Resi-like streaming, NDI, SDI, Syphon-like output, alpha key, and future transports are endpoint or capture-consumer concerns. They consume resolved screen frames and report health. Capture must not fork a separate render pipeline.

## Rendering Architecture

Rendering should be host-neutral before it is platform-specific.

```text
Presentation / Theme / Runtime tokens / Look variant
  -> Scene compiler
  -> immutable scene snapshot
  -> layer payload
  -> audience or stage frame
  -> native host adapter
```

The scene compiler emits typed nodes for text, shapes, media, live video, web content, vectors, groups, and dynamic text. Scenes include stable ids, z-order, transforms, backgrounds, alpha mode, resource dependencies, build/timeline state, diagnostics, and performance metadata.

Host adapters apply scenes using platform technologies. They may cache native visuals and media players, but scenes and frames are immutable inputs.

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
