# Content, Media, and Support Architecture

This document defines platform-agnostic architecture for ChurchPresenter content, media, generated content, support files, packages, sync, migration, audit, and repair.

## Core Rule

Portable church content and machine-local bindings must remain separate in storage, sync, migration, packages, settings, diagnostics, and recovery.

Portable content describes how a church runs services. Machine-local bindings describe how one computer realizes that setup.

## Portable Content Root

The shared content system should organize durable records into stable top-level categories such as `Libraries/`, `Playlists/`, `Presentations/`, `Themes/`, `Media/`, `Support/`, `Configurations/`, `Packages/`, and `Audits/`.

The exact on-disk shape can evolve, but the model should preserve category ownership, schema versions, ids, manifests, references, and repair paths.

## Content Graph

The content graph models libraries, presentations, slides, groups, arrangements, themes, playlists, templates, structural playlist items, generated/imported content, external plan links, and package membership.

A presentation is owned by one library. Playlist entries are occurrence references to that library-owned presentation.

Rules:

- Removing a playlist occurrence is non-destructive.
- Duplicate occurrences of the same presentation are valid.
- Deleting a library-owned presentation is destructive.
- Copying a presentation creates a distinct owned document.
- Moving a presentation transfers ownership.
- Deleting a library deletes its owned presentations through the same graph-safe process.

Graph-safe deletes must update registries, playlist occurrences, prepared cues, thumbnails, render caches, recents, selection/live-state references, package manifests, and diagnostics.

## Playlists and Templates

Playlists should support ordered presentation occurrences, media/audio cue items, generated scripture/song items, external plan items, headers, placeholders, notes, arrangement choices, destination/output hints, and occurrence-specific cue context.

Templates preserve expected show structure before all content exists. A template item may be structural and not executable.

## Presentation Documents

Presentation documents should preserve slide ids, typed scene objects, notes, groups, arrangements, theme/template references, slide actions, builds/timelines, transition settings, dynamic token references, generated/imported provenance, and media/font/resource references.

Editor, Reflow, theme, import, export, package, and live-preparation workflows should mutate documents through document services, not ad hoc native UI serialization.

## Generated and Imported Content

Generated/imported content should record provenance and refresh behavior for Bible passages, SongSelect/CCLI imports, MultiTracks data, Planning Center-style plans, ProContent-style assets, local imports, and packages.

Provenance should answer where content came from, whether it can be refreshed or regenerated, what local edits should be preserved, what license/reporting/copyright data is required, what external item it links to, and whether the link is hidden, skipped, stale, missing, or unauthenticated.

## Media and Audio Graph

Media and audio use stable asset identity plus cue-specific behavior.

Asset records include asset id, media kind, original source path or provider id, resolved local path/cache, storage policy, metadata, technical properties, thumbnail/poster metadata, missing/relinked state, package/sync eligibility, and audit diagnostics.

Cue records include cue id, asset reference, role, scaling/crop/alignment, in/out/delay/rate/duration, loop/retrigger/stop behavior, transition/effect, thumbnail override, volume and audio routing, marker actions, and output/layer target.

The same asset can appear in multiple cues with different overrides.

Media Bin and Audio Bin are cue libraries with folders, playlists, cue order, cue metadata, trigger behavior, availability/relink state, preview state, and thumbnails. They should not be implemented as raw file-system folders in the native UI.

Video and audio inputs should be asset/cue-like records with source type, platform device/source id, portable logical name, capability summary, audio linkage, routing, preview/thumbnail, health, and fallback behavior. Platform device ids belong in machine-local bindings.

## Support Graph

Production support items are first-class portable records: Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, masks, labels/groups, generated-content templates, and shared workspace defaults.

Support records should include ids, display labels, schema versions, references, package eligibility, and audit diagnostics.

Portable output records include logical screen definitions, audience/stage classification, Look routing matrices, layer membership, theme/layout variants, default clear policy, stage layouts, clear groups, and capture defaults.

Machine-local output records include concrete monitor/device mapping, display coordinates, transport device ids, platform endpoint bindings, and output capability caches.

Stage layouts store layout size, element definitions, data-provider bindings, style/theme data, screen preview bindings by logical screen, message/timer/current-next element settings, and package/sync metadata.

Clear groups store group id, name, icon/tint, target layer/playback scopes, timeline-stop behavior, default group status, and package/sync metadata. `Clear All` is a configured clear group, not a hardcoded command.

Timers store configuration and runtime defaults, not current clock state unless intentionally saved as show/session state. Messages and props store template/content/style/behavior records that can become layer payloads. Macros store deterministic action batches with ids and display metadata.

## Portable vs Machine-Local

Portable records include libraries, playlists, templates, presentations, themes, groups, arrangements, media metadata, managed media manifests, Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, masks, generated/imported provenance, and shared preferences that define production behavior.

Machine-local records include monitor/display ids, output endpoint bindings, audio/video hardware selections, local NDI/SDI/Syphon/system capability state, credentials/tokens, caches, thumbnails, relink search roots, local recents, activation/license state, and local diagnostics snapshots.

Sync and packages should include machine-local data only when explicitly requested.

## Package Shapes

| Package boundary | Includes | Excludes by default |
|---|---|---|
| Presentation document | Presentation, slide actions, document-local theme references | Shared media binaries unless bundled |
| Presentation bundle | Presentation plus selected media/font dependencies | Unrelated playlists/support files |
| Playlist package | Playlist structure, item occurrences, selected presentations, selected cues | Credentials and endpoint bindings |
| Shared support package | Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, themes/templates, shared defaults | Monitor ids, device ids, credentials, caches |
| Sync snapshot | Chosen portable categories and manifests | Machine-local bindings unless selected |
| Machine binding export | Local endpoint/device bindings and related setup | Church-wide content unless selected |

## Import, Sync, Migration, and Cleanup

Import/sync/migration flows should preview category scope, replace/merge behavior, duplicate/newer/older/deleted/unchanged/conflict states, missing dependencies, media copy/reference decisions, credential gaps, machine-local binding reconciliation, destructive consequences, and rollback/recovery plans.

Migration should normalize older or external shapes into the canonical graph instead of preserving multiple write formats.

Cleanup must traverse the full reference graph: presentations, slide objects, themes, media/audio bins, playlists, props, messages, macros, masks, announcements, stage layouts, packages, generated imports, and sync manifests. Before deletion, cleanup should explain why each candidate is considered unused and what would be affected.

## Audit and Repair

Audit services should validate folders, manifests, registries, document references, media availability, support-file references, theme/template references, output/stage configuration, package/sync manifests, generated/imported provenance, and machine-binding compatibility.

Repair should distinguish portable content repair, local binding repair, cache invalidation, relink assistance, package/sync conflict resolution, and destructive cleanup.

Audit output that describes content health can be portable. Local repair state should stay machine-local.
