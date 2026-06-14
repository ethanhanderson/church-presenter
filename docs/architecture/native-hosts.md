# Native Host Architecture

This document defines the shared responsibilities of native desktop hosts. It is platform-agnostic: Windows and macOS implementations should use their native frameworks while preserving these boundaries.

## Scope

Native hosts present ChurchPresenter workflows and apply runtime output. They should:

- provide native windows, menus, navigation, dialogs, panels, and gestures,
- render operator pages and editor surfaces,
- host audience and stage output windows,
- host previews, thumbnails, and multiviews,
- bind platform media/display/hardware APIs through adapters,
- expose accessibility, localization, and OS integration,
- report host health back to the application runtime.

They should not own live production truth, output routing, persistence rules, sync semantics, package semantics, or recovery semantics.

## Host Responsibilities

### Shell and Navigation

The shell should organize workflow families:

- **Show**: playlist operation, slide/media/audio triggering, previews, clear/recovery, output health, Show Controls.
- **Editor**: slide/object editing, notes, actions, cue inspection, validation, live-edit-safe commits.
- **Reflow**: text-first editing over presentation groups and arrangements.
- **Themes**: theme libraries, generated-content templates, screen variants.
- **Output Setup**: logical screens, endpoint bindings, Looks, stage layouts, capture/streaming.
- **Settings and Support**: support files, sync/migration, packages, integrations, diagnostics, repair.

These workflows may be arranged differently on each platform, but they should call the same runtime services.

### View Models and Presentation State

Host-side state should include:

- selected item,
- editor focus,
- page filters,
- expanded/collapsed panes,
- flyout/dialog state,
- optimistic UI state,
- local layout preferences,
- validation display state.

Host-side state should not include:

- authoritative live layer state,
- authoritative player state,
- Look routing,
- clear-group expansion,
- package/sync conflict decisions,
- media relink truth,
- stage layout resolution.

## Show Workflow

The Show surface composes these workflow models:

- service playlist and selected occurrence,
- slide grid/deck and arrangement context,
- live preview and next/previous transport,
- output layer clear/recovery controls,
- media bin and media cue trigger surface,
- audio bin and audio cue trigger surface,
- Show Controls for timers, messages, props, macros, and stage,
- output/capture health,
- stage controls and stage message entry.

On Windows, Show is additionally the only **cached** top-level page and the deepest cue-pipeline integration point. Functional subsystems, render-session contracts, preview/thumbnail lifecycle, and the phased reorganization plan live in:

- `docs/architecture/platforms/winui-native-app.md` — "Show workflow and functional subsystems"
- `docs/architecture/platforms/winui-code-organization.md` — "Show feature organization"

Subsystem summary (cross-platform product semantics; Windows-specific lifecycle in the platform doc):

| Subsystem | Operator focus |
|---|---|
| Sidebar | Libraries/playlists tree, search, rename, drag/drop import and reorder |
| Slide deck | Take-live, selection model, group footers, deck toolbar |
| Browse stack | Multi-presentation browse, section headers, auto-advance |
| Transitions | Global and per-item slide/media transitions and durations |
| Media drawer | Catalog browse; click → media layer; drag → slide-linked actions |
| Output preview & clears | Program preview, slide/media layer clears, clear groups |
| Show Controls | Timers, messages, props, audio/stage controls |
| Arrangements | Arrangement picker and grouping |
| Thumbnails | Deck card previews (host cache; not live-state) |
| Workspace | Favorites, recents, toolbar defaults, pane layout persistence |

The host should keep four concepts visually distinct:

- selected operator item,
- live state of each output layer,
- composed frame for each logical screen,
- endpoint/device health.

Slide click, media click, keyboard shortcut, macro button, and remote-equivalent host actions should dispatch commands with clear source metadata — on Windows through `ICueDispatcher` and related application services, not by mutating host-local layer state.

**Selection vs live:** slide-card selection (operator editing context) is host-side state. Live chrome (which slide is on the slide output layer) comes from the runtime live-session snapshot. Clearing the slide layer or deleting the final slide clears selection and keeps audience output blank.

Media drawers should be responsive catalog views. The host should create stable card view models from runtime media read models immediately, before thumbnails finish. Thumbnail, duration, readiness, and health fields update in place as background services report progress. A drawer open event may raise preparation priority for visible cards, but card creation, media discovery, and live-take readiness must not depend on the drawer remaining open.

### Live-Status Control Stability

Operator controls that indicate live output state should be visually stable across runtime refreshes. The host should treat clear buttons, transport lane selectors, output health chips, and similar controls as stable command affordances whose visible status is bound to runtime read-model properties.

For clear buttons, the active visual state means "one of this action's target layers currently has live content." It should not be derived from selection, focus, recent command dispatch, page refresh, or output-host apply events. If the runtime publishes the same clear-action definitions with different live flags, the host should update those flags in place; it should not remove and recreate buttons, swap duplicate active/inactive controls, or reset focus/hover state.

Collection rebuilds are appropriate only when the set of available actions changes: a clear group is added, removed, reordered, renamed, retinted, or retargeted. Per-layer live/cleared status belongs in item state. This keeps ProPresenter-style clear/recovery controls trustworthy while slides, media, announcements, props, messages, audio, and live video continue independently.

## Editor, Reflow, and Themes

Editor hosts apply scene snapshots plus editor adorners.

Editor commits should use document services for:

- object creation/editing,
- text edits,
- theme assignment,
- action edits,
- group/arrangement edits,
- media references,
- timeline/build edits,
- validation and save.

Reflow hosts should edit the presentation text projection and commit through document services that preserve slide ids, groups, arrangements, actions, and provenance where possible.

Theme hosts should manage reusable templates and variants. They should not bake output-specific Looks into presentation documents.

## Output Hosts

Output hosts apply resolved frames produced by the unified output pipeline (see `docs/architecture/target-architecture.md` and `docs/architecture/backend-application.md`).

Responsibilities:

- create/position native windows or surfaces,
- bind logical screens to concrete endpoints,
- keep local output window identity stable while the same endpoint remains enabled,
- reconcile display polling, hot-plug, focus, and bounds changes idempotently so unchanged targets do not reattach content or reapply frames,
- paint a screen fallback/background behind the layer stack so cleared layers reveal a known screen color instead of the desktop or an uninitialized surface,
- maintain persistent per-layer host surfaces (one per layer kind per screen) and apply per-slot diffs only,
- host media player surfaces and bind them to the runtime's playback coordinator,
- render operator previews, multiviews, and diagnostics snapshots from the same resolved frames/player identities as audience and stage output,
- treat media thumbnails/posters as progressive cache outputs and never as authoritative visibility state for media items,
- apply layer transitions/build descriptors using the platform's composition stack (no manual frame loops for crossfades),
- run gapless looping via the platform's native construct, never seek-to-zero,
- gate first-frame visibility on the platform's "ready to render" signal,
- report host feedback (health, frame timing, dropped frames, decoder errors),
- handle platform display lifecycle, hot-plug, and DPI/refresh changes,
- support fullscreen/presentation modes.

Restrictions:

- do not compute Look routing,
- do not select layers,
- do not inspect presentation files,
- do not clear layers locally,
- do not derive stage layouts from audience screens,
- do not fork capture rendering,
- do not create independent preview-only media players that can drift from audience transport state,
- do not reorder, hide, or substitute layer slots based on host opinion.
- do not treat display-poll ticks, focus restoration, or unchanged bounds as content reload events.

### Output Window Stability

Output windows are endpoint bindings, not render payloads. Once a local display endpoint is enabled, the host should keep its native window, root renderer, and per-layer surfaces warm until the endpoint is disabled, remapped, closed, or destroyed. A periodic monitor poll or platform display notification should compare the requested logical target with the active window binding before doing any work. If `ScreenId`, `EndpointId`, display identity, bounds, scale, and presentation mode are unchanged, the host should leave the content tree attached and report no semantic change to the renderer.

This preserves the ProPresenter-style separation documented in `docs/reference/propresenter/output-system/mental-model.md`: the operator changes content, layer, screen, or output mapping deliberately. A platform host refresh is not a new content decision and must not restart media, rebuild slots, clear layers, or expose windows behind the output.

### Preview and Transport Surfaces

The operator preview is a view of a resolved logical screen, not a separate output engine. If the audience window receives screen `Main` with Slide and Media layers, the preview for `Main` should render the same layer payloads, routing, transition state, clear state, and playback position. Differences are allowed only for scale, chrome, diagnostic overlays, audio muting policy, and interaction affordances.

On Windows, the operator preview uses a **preview-sized** `LayerStackHost` rasterized at the preview card's layout pixels (not a downscaled mirror of the full-resolution canonical surface). Audience and stage output windows mirror the canonical native-resolution render via `GetScreenSurface`. Both paths share the same pipeline frame apply and media slot bindings; only the output pixel dimensions differ.

Transport controls are command affordances over runtime transport read models. The host may present tabs or segmented controls for presentation/media, audio, and announcement playback, but the displayed cue name, enabled state, playing state, position, duration, and markers must come from the runtime lane snapshot. Host-local media elements, preview controls, or selected-item state are never the source of truth for whether a layer is playing.

### Host Adapter Contract

Host adapters expose a single, narrow interface to the runtime per output target:

- `ApplyFrame(AudienceFrameSet)` and `ApplyFrame(StageFrameSet)` — apply diffs against persistent per-layer surfaces.
- `EnsureSurfaces(ScreenConfiguration)` — create or reconcile per-screen container/layer surfaces.
- `BindPlayer(PlayerHandle, LayerSlotId)` — attach a runtime-owned player to a host surface.
- `RaiseHealth(HostHealthEvent)` — surface ready state, dropped frames, decoder errors, and recovery hints.
- `Shutdown(Reason)` — tear down surfaces and players deterministically.

Adapters must:

- pre-allocate hidden back surfaces per layer slot so transitions never wait on surface creation,
- keep the active prepared cue's resources retained while the slot is live,
- apply payload swaps atomically per slot (the host never shows a half-applied diff), and
- never block the runtime command bus thread on host work; long operations report progress via `RaiseHealth`.

Concrete platform mappings (Win2D/Composition/MediaPlayer on Windows; CALayer/CAMetalLayer/AVPlayer on macOS) live in the platform docs and must not leak through the adapter contract.

## Settings and Diagnostics Surfaces

Settings should make portability explicit:

- portable content and support files,
- machine-local bindings,
- output mappings,
- credentials/integrations,
- sync/migration/package scope,
- diagnostics and recovery.

Diagnostics surfaces should present runtime-provided data:

- active layers,
- selected/live distinctions,
- output screens and endpoint health,
- frame/host apply status,
- media/audio/video player state,
- capture/stream status,
- missing media and relink options,
- support-file repair options,
- command provenance.

## Platform Extension Points

Each native app should provide adapters for:

- display/window management,
- media playback,
- audio routing,
- video input,
- NDI/SDI/Syphon-like transports where supported,
- capture/record/stream,
- local file access and security prompts,
- credentials/keychain/credential vault,
- accessibility APIs,
- notifications and app lifecycle.

Adapter contracts should translate platform capabilities into shared runtime capability/health models.

## Testing Boundary

Native host tests should verify:

- command dispatch from key workflows,
- frame application,
- output window lifecycle,
- host adapter diagnostics,
- accessibility and keyboard navigation,
- platform settings integration,
- packaging/startup/update behaviors.

Shared runtime tests should cover the semantic behavior. Native tests should not duplicate the command/runtime matrix unless a platform adapter can change behavior.
