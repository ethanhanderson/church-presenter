# Render Engine Architecture

This document defines the implementation target for ChurchPresenter's output render engine. It is grounded in the ProPresenter reference model in this folder and in the current Windows app structure:

- `apps/windows/ChurchPresenter.Application` owns domain models, command/action state, routing state, render-frame resolution, persistence, and service tests.
- `apps/windows/ChurchPresenter` owns WinUI windows, output pages, media hosts, previews, and operator UI.
- `tests/ChurchPresenter.App.Tests` should cover render, routing, command, media, and recovery behavior before WinUI verification.

The current app now uses the backend render engine as the source of truth for output frames. `BackendRenderEngine`, `LiveRenderSessionState`, `RenderFrameSet`, `AudienceRenderFrame`, `StageRenderFrame`, `OutputRoutingService`, `OutputSceneResolver`, `OutputWindowService`, `AudienceOutputWindow`, and `OutputMediaSlotView` form the active path. WinUI-facing shapes such as `RenderFrame` and `OutputScene` remain compatibility adapters for the current output controls; they should not become a second render engine.

## Goals

- Resolve every live operation through a shared command/action pipeline rather than direct view mutations.
- Keep logical screens separate from local monitors, future NDI/SDI/capture transports, and placeholder outputs.
- Preserve fixed output layer identities: audio, messages, props, announcements, slide, media, live video, and mask.
- Produce immutable, per-screen render frames that WinUI hosts can apply without content graph traversal or blocking I/O.
- Keep audience and stage rendering separate: audience screens use Looks, stage screens use stage layouts and data providers.
- Make active output observable for recovery: active layers, active Look, stage layout, players, endpoint health, render errors, and capture state.
- Keep output windows thin, reusable, and UI-thread safe while expensive state resolution runs in `ChurchPresenter.Application`.

## Non-Goals

- Do not build a custom DirectX renderer for phase 1. WinUI/XAML surfaces, `MediaPlayerElement`, and composition animations are sufficient until a proven gap appears.
- Do not make all screens mirror one global "current output." Main room, stream, lobby, and stage screens need independent resolved frames.
- Do not model SDI, NDI, alpha keying, grouped screens, or edge blending as immediate WinUI features. Reserve endpoint capabilities and frame contracts so those transports can subscribe later.
- Do not let slide definitions own global routing. Slide actions may switch Looks, start media, trigger macros, or update stage layouts, but reusable routing belongs to screen/look state.

## Domain Pipeline

The render engine should be a deterministic pipeline:

```text
LiveCommand
  -> ActionBatch
  -> LayerState mutation
  -> Per-screen frame resolution
  -> WinUI host apply
```

### Live Command

`LiveCommand` is the public/internal command boundary. UI clicks, keyboard shortcuts, slide actions, macros, timers, playback markers, remote clients, and future communication devices should all produce commands with:

- command id and kind
- source metadata, such as operator UI, macro, slide action, timer, or remote
- target scope, such as global, screen, layer, stage screen, player, or endpoint
- authorization/audit fields for future remotes
- cancellation and correlation ids for diagnostics

Examples: take slide, clear media layer, set active Look, trigger Media Bin cue, start audio playlist, show message, hide prop, set stage layout, start capture, or reconnect endpoint.

### Action Batch

`ActionBatch` is the normalized result of command expansion. A single slide activation may produce:

- slide-layer change
- media cue or media playlist action
- audio cue or audio playlist action
- timer start/reset
- Look switch
- stage layout assignment
- macro expansion
- message, prop, announcement, or live-video action

Batches should be applied atomically enough that downstream frame resolution sees one coherent state snapshot. Partial failures should return `ActionResult` diagnostics without leaving hidden view-only state.

### Layer State

Layer state is the durable live-state source for rendering. It should not be a single `CurrentSlideId` plus ad hoc flags. The UI-facing routing model should expose the same fixed layer identities as the backend instead of reducing the output system to slide/media toggles. The model should track, per layer:

- active payload reference and display name
- source command and timestamp
- visibility, suppressed, clearing, and transition state
- playback ownership and transport state where applicable
- per-layer diagnostics and last error

The reserved audience layer identities are audio, messages, props, announcements, slide, media, live video, and mask. Existing slide/media behavior remains the compatibility baseline: default Looks route slide and media to audience feeds, and media routing also carries audio unless a feed explicitly disables it.

### Per-Screen Frame Resolution

A frame resolver converts the global live state into one immutable frame per logical screen.

Audience screen resolution:

1. Select the active `LookPreset`.
2. Resolve layer visibility for that screen.
3. Apply optional theme/layout variants for slide content, such as full-screen room lyrics versus stream lower thirds.
4. Compose active layer payload descriptors in stack order.
5. Attach endpoint-independent metadata: size, transparent/opaque mode, background color, scale mode, transition intents, diagnostics, and frame sequence.

Stage screen resolution:

1. Select the active stage layout for the stage screen.
2. Query stage data providers: current slide, next slide, notes, timers, media countdown, audience screen previews, capture state, group info, clock, and stage messages.
3. Resolve a stage frame that does not depend on audience Looks.
4. Respect `StageOnly` commands that advance confidence content without mutating audience layer state.

### WinUI Host Apply

WinUI output hosts should consume resolved frames only. They may:

- diff frame sequence/content to skip redundant visual updates
- update XAML controls, media slots, overlays, and transitions on the UI thread
- create or dispose host elements for payloads such as image, video, web, or text
- report host-level render errors and player state back to diagnostics

They should not:

- search libraries or playlists
- resolve missing media
- decide which layer routes to which screen
- perform blocking file or network I/O while applying frames
- mutate command/layer state except through explicit host feedback events

## Screen, Endpoint, and Window Model

Use these core concepts:

- `OutputScreen`: logical render target. It has id, name, kind (`Audience` or `Stage`), nominal size, background/fallback color, alpha mode, and diagnostics.
- `OutputEndpoint`: delivery target. Initial endpoint kinds are local display and placeholder; future kinds include capture, NDI, SDI/key/fill, grouped tile, and mirror target.
- `ScreenMapping`: links one screen to zero, one, or many endpoints.
- `EndpointCapability`: declares whether the endpoint supports local fullscreen windowing, transparency, audio, capture, key/fill, mirroring, fixed refresh, or user toggling.

The current `OutputWindowService` tracks windows by monitor index. The architecture should move toward tracking output windows by logical screen id plus endpoint id, while still storing `WindowId`/`AppWindow.Id` for the actual WinUI window instance. Monitor indices are volatile; screen and endpoint ids should survive display reconnects.

Local display endpoints are implemented with secondary WinUI `Window` instances and `AppWindow` configuration. Placeholder endpoints produce frames and diagnostics without opening a window. Capture and future network/hardware outputs should subscribe to resolved frames as consumers, not bypass the render pipeline.

## Audience Render Path

Audience rendering uses the fixed layer stack and Looks:

1. The command pipeline updates global layer state.
2. The active Look answers which audience screens render which layers.
3. The frame resolver applies screen-specific theme variants and output settings.
4. The output host renders the frame in stack order.

Initial stack order should reserve:

1. Screen fallback/background
2. Audio layer state, non-visual but transport-visible
3. Media/background media
4. Slide/presentation content
5. Announcements
6. Messages
7. Props
8. Live video, where configured by Look/role
9. Mask/alpha constraint
10. Blackout, clear, and diagnostic overlays

The exact visual order should be validated when each layer ships, but the contract should keep these layer ids independent and clearable. The important implementation rule is that clear and routing operate on layers, not on output windows.

## Stage Render Path

Stage screens are dashboard surfaces. They should not be routed through audience Looks and should not rely on the audience frame as their only input.

Stage frames should support:

- current and next slide text/preview
- operator notes
- timers, system clock, video countdown, and overrun state
- stage messages
- current/next group name and color
- selected audience screen preview
- media playback state
- capture/stream health
- custom text/shapes in a stage layout

The stage path needs its own `StageLayout`, `StageLayoutElement`, `StageDataProvider`, and `StageRenderFrame` contract. Stage previews of audience screens should use a frame snapshot or rendered preview feed, not recursively embed a live WinUI output window.

## Frame Contract

The backend `RenderFrameSet` is the frame contract source of truth. Compatibility shapes such as `RenderFrame` and `OutputScene` can adapt the resolved backend frames for existing WinUI controls, but they should remain host-facing projections. The contract should separate screen-level metadata, layer descriptors, and host feedback.

Required frame metadata:

- `FrameId` or monotonically increasing sequence
- `ScreenId`, `ScreenKind`, nominal pixel size, and effective render size
- background color and alpha mode (`Opaque`, `StraightAlpha`, `PremultipliedAlpha`)
- active Look id or stage layout id
- layer descriptors in resolved stack order
- transition descriptors per changed layer, not only one global slide transition
- scale mode and safe-area metadata
- clear/blackout/suppress flags by layer
- source command correlation id
- diagnostics snapshot

### Transition precedence

Transitions are resolved from broad defaults to more specific cue intent, and the most specific configured value wins:

1. Show page transition controls provide global slide and media defaults.
2. Presentation or arrangement transition settings override the global slide default for slides in that presentation.
3. Individual cue or layer transitions override broader defaults. Examples include per-slide transitions and per-media cue transitions.

The resulting frame should carry transition descriptors on the layer that changed, not one global transition for the whole output. A Look controls routing and optional theme/layout variants; it does not override transition precedence.

Current implemented layer support:

- Slide/presentation transitions are applied by the presentation host.
- Media transitions are applied to visual media slots such as underlay and overlay. Audio follows the media routing/transition contract for state consistency, even though it has no visible animation.
- Messages, props, announcements, live video, and mask are reserved backend/UI layer identities. They should only expose transition controls when their payload model and host renderer can honor those transitions.
- Clear and blackout overlays are immediate recovery states unless an explicit layer transition model is added for them.

Required layer descriptor metadata:

- layer kind and payload kind
- stable payload id and source reference
- visual bounds, alignment, scale/crop policy, opacity, blend/alpha behavior
- theme variant or style reference
- playback policy for media/audio/live video
- missing/unavailable state for recovery UI

Required host feedback:

- frame applied time
- dropped/stale frame counts
- media opened/failed/buffering/playing state
- endpoint visible/hidden/disconnected state
- render exception and last successful frame

## Media, Video, Audio, and Live Input

Media handling should stay split between domain cues and WinUI playback hosts.

Domain responsibilities:

- `MediaAsset` identity independent from path
- storage policy: managed, referenced, or package asset
- resolved path, original path, missing/relinked state, and search roots
- `MediaCue` and `AudioCue` overrides for fit, crop, in/out, delay, rate, duration, loop, stop, retrigger, transition, effects, volume, and routing
- foreground/background role, because role affects composition and retrigger behavior
- `LiveVideoInput` as a cue-like device source with health, thumbnail, optional audio, and routing
- media/audio playlist state separate from service playlist state

WinUI host responsibilities:

- render image payloads with an image host
- render video and audio payloads with `MediaPlayer` and `MediaPlayerElement`
- use `MediaSource`, `MediaPlaybackItem`, or `MediaPlaybackList` when playlists, tracks, looping, or metadata are needed
- expose player registration to shared transport controls and diagnostics
- keep output transport controls hidden on audience/stage windows unless an operator preview explicitly needs controls

The current `OutputMediaSlotView` already follows several useful patterns: persistent media slots, hidden transport controls, explicit `MediaPlayer`, `RealTimePlayback`, and `MediaPlaybackList` for looping. Future work should move player lifecycle and diagnostics behind an engine-level host adapter so multiple output screens can report player state without duplicating command logic.

Audio should be modeled independently from visual routing. Initial local playback can use normal Windows audio, but the domain should reserve internal channel routing, per-cue volume/routing, output-device selection, delay, and future SDI/NDI audio to avoid painting the design into a one-device corner.

## Output Window Lifecycle

For local display endpoints:

1. Create or reuse a secondary `Window` with an output page/control as content.
2. Track the instance by `WindowId`/`AppWindow.Id`, logical screen id, and endpoint id.
3. Move/resize with `AppWindow` using physical display bounds.
4. Use a fullscreen presenter for program output.
5. Keep output windows non-interactive and return focus to the operator shell.
6. Hide windows without destroying them when practical, so output toggles stay fast.
7. Destroy windows on endpoint removal, app shutdown, or unrecoverable host failure.

Display topology changes are normal live-production events. Use `DisplayAreaWatcher` where possible for added/removed/updated display areas and keep the current Win32 display-config fallback for richer or missing monitor metadata. When a mapped local endpoint disappears, preserve the logical screen and mapping as missing/placeholder state rather than deleting configuration or corrupting Looks.

## Threading and Performance

Microsoft's WinUI guidance makes the main constraint clear: XAML layout, input, event handlers, and most UI access run on the UI thread, so long event work blocks interaction. Output architecture should therefore keep the UI thread focused on applying already-resolved frames.

Rules:

- Resolve commands, content graphs, missing-media audits, playlist traversal, and package I/O off the UI thread.
- Use immutable snapshots between domain services and WinUI hosts.
- Use `DispatcherQueue` for UI access; do not use `Window.Dispatcher`, which is not supported for WinUI apps.
- Capture a window's `DispatcherQueue` on its UI thread and use it for host updates.
- Avoid per-frame XAML tree rebuilds. Diff frames and update only changed layers.
- Prefer opacity/transform composition animations for transitions that do not affect layout.
- Keep media players warm when a cue continues across slide changes.
- Avoid synchronous file existence/path probing during host apply; missing-media state should already be resolved.
- Measure frame apply time, media ready time, UI-thread queue delay, and dropped/stale frames per screen.

WinUI multiple windows created on the same UI thread share that UI processing thread. This is simpler than cross-thread window ownership, but it means output windows can still affect operator responsiveness if frame application is heavy. If future profiling proves one UI thread cannot sustain the number of output surfaces, isolate the problem behind the host adapter before changing the domain contracts.

## Technical Decisions Verified Against Microsoft Learn

- Use WinUI 3 `Window` content plus `AppWindow` for secondary output windows. Microsoft documents secondary WinUI windows, `AppWindow`, `WindowId`, and `XamlRoot` as the supported multi-window model: [Show multiple windows for your app](https://learn.microsoft.com/windows/apps/develop/ui/multiple-windows).
- Track multiple windows by `WindowId`/`AppWindow.Id`, not by a single static `App.Window`, because Microsoft explicitly recommends `WindowId` for multiple windows: [Windowing overview for WinUI 3 and Windows App SDK](https://learn.microsoft.com/windows/apps/develop/ui/windowing-overview).
- Treat `AppWindow` sizing/moving as physical device-pixel work and XAML layout as effective-pixel work. The windowing overview documents this distinction, so endpoint bounds and render-frame sizes should not be conflated.
- Use `DisplayArea`/`DisplayAreaWatcher` for display topology where practical. Microsoft documents `DisplayArea` as an HMONITOR abstraction and `DisplayAreaWatcher` as the API for added, removed, and updated display areas: [DisplayAreaWatcher class](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.displayareawatcher?view=windows-app-sdk-1.8).
- Use `MediaPlayerElement`/`MediaPlayer` for WinUI video/audio hosts. Microsoft documents `MediaPlayerElement`, `MediaSource`, transport controls, programmatic `MediaPlayer` control, `MediaPlaybackList`, stretch behavior, and `RealTimePlayback`: [Media players](https://learn.microsoft.com/windows/apps/develop/ui/controls/media-playback).
- Keep expensive work off the UI thread. Microsoft documents that UI layout, input, and app UI code share the UI thread, and recommends asynchronous APIs/background work for responsiveness: [Keep the UI thread responsive](https://learn.microsoft.com/windows/uwp/debug-test-perf/keep-the-ui-thread-responsive).
- Use composition-friendly transitions for transform/opacity work. Microsoft notes that some animations/transitions can run on a separate render thread when they do not affect input or layout, and WinUI composition APIs can animate visual properties efficiently: [Animating XAML elements with composition animations](https://learn.microsoft.com/windows/apps/develop/motion/xaml-property-animations).

## Diagnostics

Diagnostics should be first-class, not log-only. Operators need to know why an output is wrong during a service.

Per layer:

- active payload and origin command
- visible/suppressed/clearing state
- transition state
- media playback state and errors
- missing source or relink status

Per screen:

- active Look or stage layout
- last resolved frame id and last applied frame id
- frame resolution duration and host apply duration
- render size, endpoint mappings, alpha mode
- last render error

Per endpoint:

- connected, placeholder, missing, hidden, visible, or failed
- monitor/display identity and last observed bounds
- `WindowId`/`AppWindow.Id` for local display endpoints
- refresh rate if known
- capture/stream health for future consumers

Operator recovery UI should allow targeted clear by layer, active Look inspection, stage layout inspection, output endpoint reconnect, and player stop/pause where appropriate.

## Testing Strategy

Most behavior should be tested in `tests/ChurchPresenter.App.Tests` without opening WinUI windows.

Domain/service tests:

- command to action-batch expansion for slide activation, macros, timers, clears, media cues, and Look switches
- layer-state mutation and undoable clear behavior
- audience frame resolution for Main, Stream, Lobby, and placeholder screens
- stage-only advancement that leaves audience frames unchanged
- stage frame resolution from current/next slide, timers, messages, media countdown, and previews
- screen-to-endpoint mapping: zero, one, many, missing, placeholder, and reconnect
- Look routing with alternate theme variants and clear groups
- media cue overrides, missing-media diagnostics, relink state, cleanup reference graph
- capture as a screen consumer with health state

WinUI host tests/manual verification:

- secondary output windows open on selected monitors and do not steal operator focus
- disconnected monitors preserve logical screen mappings
- output windows apply resolved frames without library/playlist resolution
- media slots continue playback across slide changes when the cue is unchanged
- clear/blackout/layer suppression visual states match domain frames
- layer transition precedence follows global Show default, then presentation default where applicable, then cue/layer override
- operator UI remains responsive while multiple output windows and media players are active

Existing tests such as `OutputRoutingServiceTests`, `OutputSceneResolverTests`, `OutputFrameSnapshotAdapterTests`, and `AudienceOutputMonitorSelectionTests` are the right pattern: lock behavior in application services before replacing or expanding the WinUI host stack.

## Phased Implementation

### Phase 0: Contract Hardening

- Keep the UI-facing output layer model aligned with backend `OutputLayerKind` identities.
- Introduce `OutputScreen`, `OutputEndpoint`, `ScreenMapping`, endpoint capabilities, and placeholder endpoints.
- Split audience screen routing from the current built-in audience/stage feed list.
- Add frame ids, screen ids, layer descriptors, and diagnostics to the frame contract.
- Keep current slide/media behavior representable through migration defaults.

### Phase 1: Command and Layer State

- Add `LiveCommand`, `LiveAction`, `ActionBatch`, `ActionResult`, and a shared executor.
- Route slide clicks, toolbar clears, Look changes, and media actions through the executor.
- Replace presentation/media clear flags with layer-specific state and clear groups.
- Preserve the existing operator UI behavior while keeping backend render frames as the implementation path.

### Phase 2: Audience Screens and Local Endpoints

- Resolve per-audience-screen frames from one live state plus active Look.
- Map one logical screen to one or more local display endpoints.
- Track local output windows by screen/endpoint and `WindowId`.
- Add placeholder endpoint diagnostics and monitor reconnect behavior.

### Phase 3: Media and Audio Engine Expansion

- Add `MediaAsset`, `MediaCue`, `AudioCue`, media/audio playlists, live video input contracts, and cue overrides.
- Feed media/audio state into layer state instead of direct slot-only state.
- Surface shared media transport and player diagnostics.
- Keep future SDI/NDI audio routing as domain metadata, not WinUI-only behavior.

### Phase 4: Stage Renderer

- Add stage screens, layouts, elements, and data providers.
- Support stage-only and stage-plus-audience command modes.
- Render current/next, notes, timers, media countdown, stage messages, and audience previews.

### Phase 5: Diagnostics and Future Consumers

- Build an output diagnostics panel around screen/layer/endpoint/player state.
- Add capture/stream session as a screen consumer.
- Add transport extension points for NDI, SDI/key/fill, grouped screens, edge blending, and capture without changing the command pipeline.

## Implementation Invariants

- A live command is the only way to intentionally change live output state.
- Render frames are immutable snapshots, not mutable UI state.
- Backend render frames are the render source of truth; WinUI compatibility models adapt them for controls.
- Audience Looks do not control stage layouts.
- Stage-only commands do not mutate audience layer state.
- Clearing one layer does not imply clearing all layers.
- Transition precedence is global Show default, then presentation/arrangement default where applicable, then individual cue/layer override.
- Endpoint loss does not delete logical screen configuration.
- WinUI output windows apply frames; application services resolve frames.
- Media/audio playback state belongs to media/audio layers and player registration, not to each presentation surface.
