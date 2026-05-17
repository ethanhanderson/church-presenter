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

The Show surface should compose these workflow models:

- service playlist and selected occurrence,
- slide grid/deck and arrangement context,
- live preview and next/previous transport,
- output layer clear/recovery controls,
- media bin and media cue trigger surface,
- audio bin and audio cue trigger surface,
- Show Controls for timers, messages, props, macros, and stage,
- output/capture health,
- stage controls and stage message entry.

The host should keep four concepts visually distinct:

- selected operator item,
- live state of each output layer,
- composed frame for each logical screen,
- endpoint/device health.

Slide click, media click, keyboard shortcut, macro button, and remote-equivalent host actions should dispatch commands with clear source metadata.

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

Output hosts apply resolved frames.

Responsibilities:

- create/position native windows or surfaces,
- bind logical screens to concrete endpoints,
- apply frame diffs,
- host media player surfaces,
- apply layer transitions/build descriptors,
- report host feedback,
- handle platform display lifecycle,
- support fullscreen/presentation modes.

Restrictions:

- do not compute Look routing,
- do not select layers,
- do not inspect presentation files,
- do not clear layers locally,
- do not derive stage layouts from audience screens,
- do not fork capture rendering.

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
