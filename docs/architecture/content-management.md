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

## Content Change Bus and Cue Invalidation

The unified output pipeline takes cues live from the content graph and must replace them when the underlying source changes (see `docs/architecture/target-architecture.md`). The content layer owns a single `ContentChangeBus` that publishes typed invalidation events:

- `PresentationChanged(presentationId, version, scope)` for slide text, layout, theme assignment, action, timeline, or arrangement edits.
- `ThemeChanged(themeId, version)` for theme/template edits.
- `MediaAssetChanged(assetId, stamp, reason)` for relink, metadata update, transcode, filter change, or thumbnail rebuild.
- `MediaCueChanged(cueId, version)` for cue-specific overrides such as in/out, rate, loop, role, or audio routing.
- `SupportRecordChanged(kind, id, version)` for messages, props, masks, macros, clear groups, stage layouts, timers, and labels.
- `LinkedSourceChanged(providerKind, externalId, refreshState)` for SongSelect/CCLI, Bible, MultiTracks, Planning Center-style, and ProContent-style providers.
- `LookOrScreenConfigChanged(id, version)` for Look matrices, logical screens, stage layout assignments, and clear-group configuration.

Document, support, and integration services publish these events when committing edits. Caches, prepared cues, render caches, search indexes, thumbnails, and live-state subscribers consume them.

### Invalidation Rules

- An event carries a stable `ContentRef` and a new `Version`/`Stamp`. Consumers compare stamps to drop stale entries.
- The `PreparedCueCache` (see `backend-application.md`) is indexed by `(ContentRef, LayerKind)` so invalidation maps directly to a re-prepare schedule.
- Caches are accelerators only. A missing or stale entry must be rebuildable from the canonical content + resource stamp.
- For active live payloads, the runtime issues a `ReplaceLayerPayload` action once the re-prepared cue is ready; the replacement is scoped to the affected layer slot and never restarts unaffected layers.
- Replacement transitions default to instant for version-only/metadata changes and to a crossfade when visual or audible content changed; operators can override the default through Show settings.

### Examples

- Editing a slide's theme background or text replaces the slide-layer payload only; the media layer keeps playing.
- Toggling a media library filter (color, crop, scaling) replaces the media-layer payload only; the slide layer keeps its take state.
- Updating a message template's tokens replaces the messages-layer payload while it is live.
- Relinking an audio asset replaces the audio-layer payload and updates Audio Bin previews; Slide and Media layers keep playing.
- Changing a mask shape or theme replaces the mask layer constraint per affected screen; underlying content does not re-trigger.

The content layer never reaches into the live pipeline directly. It publishes invalidation events; the runtime decides how, when, and with what transition to apply replacements.

## Playlists and Templates

Playlists should support ordered presentation occurrences, media/audio cue items, generated scripture/song items, external plan items, headers, placeholders, notes, arrangement choices, destination/output hints, and occurrence-specific cue context.

Templates preserve expected show structure before all content exists. A template item may be structural and not executable.

## Presentation Documents

Presentation documents should preserve slide ids, typed scene objects, notes, groups, arrangements, theme/template references, slide actions, builds/timelines, transition settings, dynamic token references, generated/imported provenance, and media/font/resource references.

Editor, Reflow, theme, import, export, package, and live-preparation workflows should mutate documents through document services, not ad hoc native UI serialization.

### Portable file formats (v1)

Format specs live under [`formats/`](formats/README.md):

| File | Role |
|------|------|
| [`.cpres`](formats/cpres-v1.md) | Library-owned presentation document or bundle (slides + optional embedded media/fonts/themes) |
| [`.cplibrary`](formats/cplibrary-v1.md) | Library catalog + owned presentation bundles |
| [`.cpplaylist`](formats/cpplaylist-v1.md) | Playlist structure + referenced presentation bundles (optional media) |
| [`.cpsupport`](formats/cpsupport-v1.md) | Portable support/configuration JSON |

Library-owned presentations persist under the content root `Presentations/` tree. Windows bundle I/O lives in `ChurchPresenter.Core/Cpres/` and `CpresDocumentService`; collection packages in `CollectionPackageService`; support packages in `SupportPackageService`.

## Generated and Imported Content

Generated/imported content should record provenance and refresh behavior for Bible passages, SongSelect/CCLI imports, MultiTracks data, Planning Center-style plans, ProContent-style assets, local imports, and packages.

Provenance should answer where content came from, whether it can be refreshed or regenerated, what local edits should be preserved, what license/reporting/copyright data is required, what external item it links to, and whether the link is hidden, skipped, stale, missing, or unauthenticated.

## Media and Audio Graph

Media and audio use stable asset identity plus cue-specific behavior.

Asset records include asset id, media kind, original source path or provider id, resolved local path/cache, storage policy, metadata, technical properties, thumbnail/poster metadata, missing/relinked state, package/sync eligibility, and audit diagnostics.

Cue records include cue id, asset reference, role, scaling/crop/alignment, in/out/delay/rate/duration, loop/retrigger/stop behavior, transition/effect, thumbnail override, volume and audio routing, marker actions, and output/layer target.

The same asset can appear in multiple cues with different overrides.

Media Bin and Audio Bin are cue libraries with folders, playlists, cue order, cue metadata, trigger behavior, availability/relink state, preview state, and thumbnails. They should not be implemented as raw file-system folders in the native UI.

Media drawer cards are a projection of this graph, not a thumbnail loader. The card list should materialize immediately from asset and cue metadata, then update thumbnail/poster fields as cache work completes. Missing, stale, unsupported, or slow thumbnails should leave the card visible with fallback type artwork and diagnostics. They must not hide the media asset, block slide-action assignment, or delay direct media cue creation.

Thumbnail and poster records are cache metadata tied to resource stamps. The content graph records the asset identity, desired thumbnail intent, last known thumbnail status, and rebuild reason; machine-local caches hold platform image bytes or decoded surfaces. Rebuilding a thumbnail follows the same invalidation rule as other media changes: `MediaAssetChanged(assetId, stamp, reason)` marks the thumbnail stale and schedules regeneration without making the asset disappear from library queries.

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

ProPresenter export/sync categories map to ChurchPresenter archives as follows (full schemas in [`formats/README.md`](formats/README.md)):

| Package boundary | File / store | Includes | Excludes by default |
|---|---|---|---|
| Presentation document | `.cpres` (`bundleMode: document`) | Slides, groups, arrangements, actions, theme refs, media/font metadata | `media/`, `fonts/` binaries |
| Presentation bundle | `.cpres` (`bundleMode: bundle`) | Document contents plus embedded `media/*`, `fonts/*`, `themes/*` | Unrelated playlists, support files |
| Library package | `.cplibrary` | `library.json`, all owned `.cpres` (mode per export flags) | Playlists, media bin, support, machine bindings |
| Playlist package | `.cpplaylist` | `playlist.json`, referenced `.cpres` per item, occurrence metadata, external plan links | Library ownership transfer, credentials |
| Playlist package (no media) | `.cpplaylist` (`includeMedia: false`) | Structure + document-mode presentations | Embedded media binaries |
| Shared support package | `.cpsupport` | `Configurations/*.json`: Looks, screens, stage layouts, clear groups, timers, messages, props, macros, shared defaults | Monitor ids, device ids, credentials, caches |
| Sync snapshot (future) | Multi-category archive | Operator-selected libraries, media, playlists, themes, support | Machine-local bindings unless selected |
| Machine binding export | Machine-local store | Endpoint/device bindings and reconciliation state | Church-wide portable content unless selected |

### Contained presentation requirements

A **contained presentation** inside any package must carry enough data to restore operator and output behavior on a cold machine:

- Stable `presentationId`, title, timestamps, provenance (`externalSong`, `sync`, future `ContentProvenance`)
- Full `slides.json` scene graph and `arrangement.json` group/arrangement graph
- Per-slide and per-presentation transitions, actions, notes, theme bindings
- `manifest.media[]` and `manifest.fonts[]` inventories with hashes and archive paths
- In **bundle** mode: every referenced binary present under `media/` and `fonts/`
- In **document** mode: metadata sufficient for relink (filename, optional source hints in extension data)
- Embedded `themes/*.json` when the presentation must render identically without the global theme library

### Library and playlist packages with presentations

- **Library packages** export every presentation listed in `library.json`, deduplicated by source bundle path. Import creates a new library id and remaps paths into `Presentations/`.
- **Playlist packages** export every presentation path in `items[]`, preserving duplicate occurrences and per-item `arrangementId` / `destinationLayerId`. Import creates a new playlist; presentations copy to `Presentations/` but are not automatically added to a library.
- Both package types should record `includeMedia`, `includeFonts`, and `presentationBundleMode` in `manifest.json` so importers know whether to expect binaries and whether relink is required.
- Preview/import must list `PackageCopyRequirement` entries for each presentation bundle and embedded media payload before writing files.

## Import, Sync, Migration, and Cleanup

Import/sync/migration flows should preview category scope, replace/merge behavior, duplicate/newer/older/deleted/unchanged/conflict states, missing dependencies, media copy/reference decisions, credential gaps, machine-local binding reconciliation, destructive consequences, and rollback/recovery plans.

Migration should normalize older or external shapes into the canonical graph instead of preserving multiple write formats.

Cleanup must traverse the full reference graph: presentations, slide objects, themes, media/audio bins, playlists, props, messages, macros, masks, announcements, stage layouts, packages, generated imports, and sync manifests. Before deletion, cleanup should explain why each candidate is considered unused and what would be affected.

## Audit and Repair

Audit services should validate folders, manifests, registries, document references, media availability, support-file references, theme/template references, output/stage configuration, package/sync manifests, generated/imported provenance, and machine-binding compatibility.

Repair should distinguish portable content repair, local binding repair, cache invalidation, relink assistance, package/sync conflict resolution, and destructive cleanup.

Audit output that describes content health can be portable. Local repair state should stay machine-local.
