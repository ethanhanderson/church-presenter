# ProPresenter Reference

This folder is an implementation-oriented reference for ProPresenter behavior that matters to ChurchPresenter architecture. It synthesizes current official Renewed Vision feature pages and knowledge-base articles; it is not a copy of the help center.

## Start Here

Use the [ProPresenter feature map](features.md) as the primary entry point. It inventories the public ProPresenter feature surface from Renewed Vision's feature pages, links to detailed functionality pages, and explains the cross-feature architecture lessons for native Windows and macOS apps.

The implementation-oriented notes remain below as deeper references. They are organized by product subsystem and should be used when a feature page points to a specific output, media, library, or operator workflow detail.

## Organization Model

This tree is intentionally layered so it can grow without mixing concerns:

1. **Feature map**: `features.md` is the product-level index. It answers "what does ProPresenter do?" and links to detail pages by feature family.
2. **Feature detail pages**: `features/*.md` explain current ProPresenter behavior, cross-feature interactions, and native Windows/macOS architecture implications.
3. **Subsystem deep dives**: folders such as `output-system/`, `media-management/`, `library-show-management/`, and `operator-workflows/` preserve implementation-oriented analysis by domain.
4. **Implementation targets**: `feature-inventory.md`, `render-engine/`, and `churchpresenter-design-notes.md` translate product behavior into ChurchPresenter contracts, phases, and dependencies.
5. **Source bibliography**: `output-system/sources.md` records the official source basis and coverage limits.

When adding future ProPresenter research, add the user-facing behavior to the relevant `features/*.md` page first, then add or update subsystem notes only when the behavior affects a shared model, renderer, command path, portability boundary, or output contract.

## Feature Detail Pages

- [Feature map](features.md) - complete feature inventory with source links and architecture implications.
- [Authoring and slide content](features/authoring-and-slide-content.md) - slide editor, custom objects, text effects, dynamic text, themes, Reflow, arrangements, timelines, and slide actions.
- [Libraries, playlists, and generated content](features/libraries-playlists-and-generated-content.md) - libraries, service playlists, templates, Quick Search, SongSelect, MultiTracks, Planning Center, Bibles, ProContent, imports, packages, and provenance.
- [Media, audio, and live inputs](features/media-audio-and-live-inputs.md) - Media Bin, Audio Bin, images, videos, cue inspectors, media management, audio routing, video inputs, and playback markers.
- [Outputs, screens, and broadcast routing](features/outputs-screens-and-broadcast-routing.md) - multiple screens, Looks, layers, announcements, props, masks, edge blend, SDI, NDI, Syphon, alpha key, recording, streaming, and Resi.
- [Stage, overlays, and operator views](features/stage-overlays-and-operator-views.md) - stage layouts, confidence monitors, current/next text, clocks, timers, stage messages, live previews, multiviews, messages, props, and clear groups.
- [Automation, control, and remotes](features/automation-control-and-remotes.md) - slide actions, macros, playback markers, timeline, timecode, calendar scheduling, Stream Deck, MIDI/DMX, API, Network Link, Control, and Remote.
- [Administration, portability, and preferences](features/administration-portability-and-preferences.md) - seats, activation, support files, sync, migration, preferences, localization, copyright display, and team operation.

## Subsystem Reference Map

Use this folder as a reference set, not a chronological research log.

### Product Reference

- [Output system](output-system/README.md) - screens, outputs, layers, Looks, stage output, alpha/keying, routing, diagnostics, and signal transports.
- [Media and audio management](media-management/media-audio-assets.md) - Media Bin, Audio Bin, foreground/background media, live video inputs, cue inspection, media defaults, storage policy, missing-media recovery, and audio routing.
- [Library and show management](library-show-management/libraries-playlists-integrations.md) - libraries, playlists, playlist templates, presentations, themes, arrangements, Planning Center, Bibles, SongSelect, Quick Search, sync, migration, and packages.
- [Presentations and slides](presentations-and-slides.md) - slide canvas objects, themes, Reflow, groups, arrangements, show views, slide destination, and slide-scene implications for rendering.
- [Operator workflows and automation](operator-workflows/show-controls-and-recovery.md) - show controls, clear groups, timers, messages, props, macros, slide actions, remotes, capture/streaming controls, and live recovery workflows.

### Implementation Reference

- [ChurchPresenter design notes](output-system/churchpresenter-design-notes.md) - implementation implications from the ProPresenter output model.
- [Feature inventory](feature-inventory.md) - implementation-oriented ChurchPresenter capability targets, source basis, implications, phases, and dependencies.
- [Render engine architecture](render-engine/render-engine-architecture.md) - implementation-oriented pipeline from live commands to layer state, per-screen frames, WinUI hosts, media playback, diagnostics, and phased build-out.

### Source Bibliography

- [Sources](output-system/sources.md) - official Renewed Vision links, coverage notes, and source limitations used by the reference set.

## Cross-Cutting Product Lessons

ProPresenter's output engine is only one part of the product model. Output behavior depends on upstream content and operational state: playlist items, presentation destinations, media actions, audio playlists, timer actions, macros, messages, props, stage layouts, Planning Center links, and support-file portability.

For ChurchPresenter, the durable architectural split is:

- Libraries, playlists, presentations, arrangements, themes, and external plan links define what can be selected.
- Media/audio assets, cues, import defaults, thumbnails, missing-media rules, and routing define how assets play.
- Slide actions, macros, timers, messages, props, and stage actions define what else changes when an operator triggers content.
- Output screens, layers, Looks, stage layouts, endpoints, and capture/stream transports define what is rendered and where it goes.

## Source Policy

Prefer current official Renewed Vision sources: public feature pages under `renewedvision.com/propresenter/...` and current `support.renewedvision.com` knowledge-base articles. If a current article requires sign-in or no current article exists, record that limitation and use only accessible current sources or clearly labeled historical material as secondary context. Do not treat older-version articles as source of truth for current ProPresenter behavior.
