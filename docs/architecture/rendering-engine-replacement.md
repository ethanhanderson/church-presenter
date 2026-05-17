# Rendering and Output Engine Architecture

This document defines the platform-agnostic rendering and output architecture for ChurchPresenter. It replaces host-specific slide rendering with a shared scene, frame, layer, and host-adapter model.

## Decision

The application runtime should compile content into immutable, host-neutral scene snapshots and resolve those snapshots into audience and stage frames. Native hosts apply frames through platform adapters.

```text
Document / cue / support state
  -> command runtime
  -> live session snapshot
  -> scene compilation and provider resolution
  -> audience frame set / stage frame set
  -> native host adapters
  -> windows, previews, endpoints, capture, transports
```

The renderer is part of the production engine, not a direct path from selected UI item to output window.

## Rendering Principles

- Selection state is not render state.
- Scenes are immutable.
- Frames are immutable.
- Output layers have stable identities.
- Looks route layers to audience screens.
- Stage layouts compose runtime data providers separately from audience Looks.
- Native hosts apply frames; they do not resolve routing or content semantics.
- Capture and transport consumers subscribe to resolved frames rather than forking rendering.

## Scene Compiler

The scene compiler converts portable content, theme data, runtime tokens, and render intent into host-neutral `SceneSnapshot` records.

Inputs:

- presentation or generated-content document,
- slide/object definitions,
- selected arrangement/build state,
- theme/template and optional Look-selected variant,
- media/font/resource dependencies,
- runtime token snapshot,
- render intent: thumbnail, editor, audience, stage preview, export, diagnostic, or capture.

Outputs:

- stable scene id/version,
- canvas size and alpha/background mode,
- ordered node graph,
- media/font/resource dependencies,
- token/provider dependencies,
- build/timeline metadata,
- source references,
- diagnostics,
- performance metadata.

The compiler must not create native views, own media players, mutate live state, decide output routing, or do blocking endpoint work during host apply.

## Scene Model

Minimum node families:

- `TextSceneNode`: text runs, font/style, layout box, effects, fills, stroke, shadow, alignment, wrapping, token spans.
- `ShapeSceneNode`: shape/path, fill, stroke, corner radius, transform, opacity, blend.
- `MediaSceneNode`: asset reference, display policy, fit/crop/stretch, poster, loop/mute/autoplay defaults.
- `LiveVideoSceneNode`: logical input reference, fit policy, optional audio, health/fallback metadata.
- `WebSceneNode`: URL or captured content reference, zoom, refresh policy, snapshot/live mode.
- `VectorSceneNode`: geometry/path data, view box, fill/stroke, winding.
- `GroupSceneNode`: child nodes, transform, clipping, opacity.
- `DynamicTextSceneNode` or token spans: runtime provider bindings.

Every node needs:

- stable id,
- z-order,
- bounds,
- transforms,
- visibility/build state,
- source reference,
- diagnostics.

Unsupported imported effects should be preserved as metadata with diagnostics rather than flattened away.

## Layer Payloads

Rendered content enters output through layer payloads.

Common payload metadata:

- payload id,
- payload kind,
- source command,
- source document/cue/support record,
- display label,
- dependencies,
- transition intent,
- diagnostics.

Payload-specific detail records:

- `PresentationRenderPayload`: presentation id, slide id, arrangement/build state, scene id, theme/template/Look variant, notes/current-next metadata.
- `MediaRenderPayload`: cue id, asset id, playback policy, scaling/crop, marker metadata, audio routing, player identity.
- `AudioRenderPayload`: cue id, asset id, routing, volume, marker metadata, transport policy.
- `LiveVideoRenderPayload`: logical input id, source health, fit policy, linked audio.
- `OverlayRenderPayload`: message/prop/mask/announcement/generated scene or provider output.
- `CaptureRenderPayload`: source screen, profile, audio routing, destination/stream metadata.

Layer descriptors carry layer-level opacity, bounds, transition, clear/suppressed state, and routing eligibility.

Media transport state should be lane-scoped, not derived from one global active-player list. The engine should publish separate transport snapshots for media files, audio files, and announcements so the operator card can switch targets without mutating output routing, clearing layers, or retargeting content.

## Audience Frame Resolution

Audience frames resolve from:

- live session layer state,
- compiled scene payloads,
- active Look,
- logical screen definitions,
- output mask/composition constraints,
- endpoint capability constraints.

The active Look decides which fixed layers appear on each audience screen:

- Audio,
- Messages,
- Props,
- Announcements,
- Slide,
- Media,
- Live Video,
- Mask.

The frame resolver should emit:

- `AudienceFrameSet`,
- per-screen `AudienceFrame`,
- ordered layer descriptors,
- payload references,
- screen-specific theme/layout variants,
- transition descriptors,
- diagnostics.

Look changes reroute live payloads. They should not reselect content or mutate documents.

## Stage Frame Resolution

Stage frames resolve from:

- stage screen assignments,
- active stage layouts,
- stage data providers,
- live show context,
- timers/clocks,
- media countdown and marker state,
- current/next slide data,
- notes and labels,
- screen preview feeds,
- capture status,
- stage messages.

Stage rendering should emit:

- `StageFrameSet`,
- per-stage-screen `StageFrame`,
- layout element descriptors,
- provider snapshots,
- preview frame references,
- diagnostics.

Stage layouts do not use audience Looks, but they may reference audience screen previews by logical screen id.

## Dynamic Text and Providers

Dynamic text should bind to provider snapshots. Providers include:

- timers and countdown-to-time,
- system clock,
- media countdown,
- current slide text,
- next slide text,
- slide notes,
- group/arrangement labels,
- stage messages,
- scripture metadata,
- external plan metadata,
- custom operator fields,
- capture/streaming status.

Compiler diagnostics should report missing or invalid bindings. Host adapters should not resolve tokens independently.

## Builds, Timelines, and Transitions

Build state is object-level scene visibility and animation state.

Layer transition state is output-layer change state.

Rules:

- Build state selects visible nodes inside a scene.
- Slide transitions belong to the Slide or Announcements layer when those payloads change.
- Media transitions belong to the Media layer.
- Props/messages/masks may have overlay transitions where implemented.
- Timeline/timecode/calendar playback schedules commands.
- Playback markers raise marker events into the command executor.
- Host animations should reflect resolved transition/build descriptors.

Transition precedence:

1. Global Show default.
2. Presentation or arrangement default.
3. Per-slide or per-cue override.

The winning transition belongs to the changed layer.

## Native Host Adapters

Host adapters convert scenes and frames into platform visuals.

Adapters should exist for:

- audience output,
- operator preview,
- thumbnail rendering,
- editor canvas,
- stage output,
- screen preview/multiview,
- media playback,
- capture/export,
- diagnostics/snapshot testing.

Adapter responsibilities:

- create and update native visuals,
- own platform media players,
- apply frame diffs,
- map platform failures back to diagnostics,
- manage UI-thread or render-thread requirements,
- cache native resources where safe.

Adapter restrictions:

- do not decide what is live,
- do not resolve Looks,
- do not mutate document models,
- do not interpret command semantics,
- do not crawl content roots directly,
- do not invent layer clears or recovery behavior.

## Editor Rendering

Editor hosts use the same scene model plus editor-only adorners.

Editor-only state:

- selection bounds,
- resize handles,
- rotation handles,
- rulers,
- guides,
- snapping,
- drag/drop targets,
- object outlines,
- validation overlays.

These adorners must not serialize into content or appear in output frames.

Live editing should invalidate future scene versions and prepared cues. It should not tear down the currently live frame unless a command replaces it.

## Thumbnails and Previews

Thumbnails and previews should be frame/scene consumers with constrained render intent.

Rules:

- thumbnails do not autoplay video or audio,
- previews should display representative media frames when available,
- output previews subscribe to resolved frames,
- stage screen preview elements reference resolved audience frames,
- cache keys must include content/resource/theme/token stamps.

## Capture, Streaming, and Transports

Capture consumers receive resolved screen frames. They should not own content rendering.

Consumer types:

- recording,
- RTMP streaming,
- Resi-like streaming,
- NDI output,
- SDI output,
- Syphon-like local video output,
- alpha key/fill output,
- future remote transport.

Each consumer should report:

- source logical screen,
- endpoint/profile settings,
- resolution/frame rate,
- audio routing,
- alpha/key mode,
- health,
- dropped frames,
- encoder/transport errors,
- destination status.

## Diagnostics

Rendering diagnostics should distinguish:

- scene compile failure,
- missing dependency,
- dynamic token/provider failure,
- unsupported effect,
- frame resolution failure,
- layer routing issue,
- host apply failure,
- media player failure,
- endpoint/transport failure,
- capture/encoder failure.

Diagnostics should include scene ids, frame ids, layer ids, payload ids, source document/cue references, host adapter names, timing, and suggested recovery.

## Migration Direction

Implementation should move in phases:

1. Freeze shared scene, payload, frame, and diagnostic contracts.
2. Compile current presentation content into scene snapshots behind existing UI.
3. Adapt thumbnails, previews, editor, and output hosts to consume scenes/frames.
4. Move output routing and clear behavior fully into the runtime.
5. Add ProPresenter-parity features: richer text/effects, live-video nodes, grouped objects, dynamic tokens, stage layouts, lower thirds, masks, alpha-aware capture, and generated overlays.
6. Retire host-specific render logic that duplicates application runtime semantics.

## Test and Benchmark Gates

Shared tests should cover:

- scene compilation for node families,
- theme and Look variant resolution,
- dynamic token diagnostics,
- media/font dependency diagnostics,
- build/timeline state,
- layer payload resolution,
- Look routing,
- stage provider snapshots,
- transition precedence,
- clear/suppressed layer state,
- capture consumer frame source,
- cache invalidation.

Platform tests should cover:

- host adapter frame application,
- display/window lifecycle,
- media player behavior,
- accessibility of editor/operator surfaces,
- performance under high layer/node counts,
- capture/transport availability where testable.
