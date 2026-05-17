# ProPresenter Feature Map

This is the feature-first entry point for ProPresenter behavior that should inform ChurchPresenter architecture for native Windows and macOS apps.

It synthesizes the public Renewed Vision ProPresenter pages, especially:

- [All features](https://renewedvision.com/propresenter/all-features)
- [Main ProPresenter page](https://renewedvision.com/propresenter)
- [Media & Slides](https://renewedvision.com/propresenter/media-and-slides)
- [Multi-screen management](https://renewedvision.com/propresenter/multi-screen-management)
- [Stage Display](https://renewedvision.com/propresenter/stage-display)
- [Automations & Control](https://renewedvision.com/propresenter/automations)
- [ProPresenter Remote](https://renewedvision.com/propresenter/remote)
- [Streaming](https://renewedvision.com/propresenter/streaming)

The marketing pages describe the current public feature surface. The existing reference docs and official support/user-guide material fill in how the features behave, how they combine during live production, and what architectural seams ChurchPresenter should preserve.

## Current-Version Source Policy

The detail pages under `features/` are intentionally based on current public ProPresenter feature pages and current Renewed Vision knowledge-base articles that describe the active product surface, including ProPresenter Remote for version 20 or later and recent/current support articles for Looks, Network Link, Control, TCP/IP API, media management, alpha key, NDI, Resi, MultiTracks, Planning Center, timers, and stage layouts.

Older-version references that still exist in deeper subsystem notes are historical support material and should not be used as the source of truth for new feature behavior unless a current article is unavailable and the doc explicitly calls out that limitation.

## Detail Pages

- [Authoring and slide content](features/authoring-and-slide-content.md) - slide editor, custom objects, text effects, dynamic text, themes, Reflow, Quick Edit, arrangements, timelines, and slide actions.
- [Libraries, playlists, and generated content](features/libraries-playlists-and-generated-content.md) - libraries, service playlists, playlist templates, Quick Search, SongSelect, MultiTracks, Planning Center, Bibles, ProContent, imports, packages, and provenance.
- [Media, audio, and live inputs](features/media-audio-and-live-inputs.md) - Media Bin, Audio Bin, images, videos, foreground/background roles, cue inspectors, media management, audio routing, video inputs, and ProContent assets.
- [Outputs, screens, and broadcast routing](features/outputs-screens-and-broadcast-routing.md) - multiple screens, audience/stage screen separation, Looks, masks, announcements, props, edge blend, SDI, NDI, Syphon, alpha key, recording, streaming, and Resi.
- [Stage, overlays, and operator views](features/stage-overlays-and-operator-views.md) - stage layouts, confidence monitors, current/next text, clocks, timers, stage messages, live previews, multiview/operator views, messages, props, and clear/recovery surfaces.
- [Automation, control, and remotes](features/automation-control-and-remotes.md) - slide actions, macros, playback markers, timeline, timecode, calendar scheduling, Stream Deck, MIDI/DMX, API, Network Link, ProPresenter Control, and ProPresenter Remote.
- [Administration, portability, and preferences](features/administration-portability-and-preferences.md) - seats, activation, unlicensed show building, support files, sync, migration, machine-local bindings, preferences, localization, copyright display, and team operation.

## Product Model

ProPresenter is best understood as a live-production system with six connected models. The feature pages are organized around these models so the reference can scale as ChurchPresenter adds more parity targets.

1. **Content model**: libraries, presentations, slides, groups, arrangements, themes, generated scripture, imported songs, media assets, and support files define what exists.
2. **Cue model**: media cues, audio cues, video inputs, slide actions, playback markers, macros, timers, props, messages, announcements, and capture commands define what can happen.
3. **Show model**: service playlists, playlist templates, Planning Center plans, headers, placeholders, selected presentation instances, selected arrangements, operator notes, and show view projections define what the operator can trigger.
4. **Live-state model**: active slide/media/audio/video/message/prop/announcement/timer/macro/capture state records what is currently running and why.
5. **Output model**: logical screens, fixed output layers, Looks, stage layouts, masks, endpoint mappings, NDI/SDI/Syphon/system outputs, alpha, capture, and streaming define what is rendered and where it goes.
6. **Control model**: local UI, keyboard, Stream Deck, MIDI, DMX, Network Link, HTTP/TCP APIs, ProPresenter Control, Remote, calendar scheduling, Resi Studio, and future automation surfaces all need to converge on the same command/query layer.

For ChurchPresenter, the important architectural lesson is that these models are independent but composable. A slide is content, a slide click is a command, a media action is a cue, a Look is output routing, a stage layout is a separate dashboard composition, and a Remote tap is only another command source.

## Architecture Coverage Map

| Architecture concern | Primary feature page | Deep reference |
|---|---|---|
| Slide documents, scene graph, themes, Reflow, arrangements, and timelines | [Authoring and slide content](features/authoring-and-slide-content.md) | [Presentations and slides](presentations-and-slides.md), [Render engine architecture](render-engine/render-engine-architecture.md) |
| Libraries, playlists, generated content, imports, integrations, packages, and provenance | [Libraries, playlists, and generated content](features/libraries-playlists-and-generated-content.md) | [Library and show management](library-show-management/libraries-playlists-integrations.md), [Feature inventory](feature-inventory.md) |
| Media assets, cue overrides, Media Bin, Audio Bin, live inputs, audio routing, and cleanup | [Media, audio, and live inputs](features/media-audio-and-live-inputs.md) | [Media and audio management](media-management/media-audio-assets.md), [Output types and signal routing](output-system/output-types-and-signal-routing.md) |
| Logical screens, layers, Looks, announcements, masks, endpoints, alpha, NDI, SDI, capture, and streaming | [Outputs, screens, and broadcast routing](features/outputs-screens-and-broadcast-routing.md) | [Output system](output-system/README.md), [Layers and Looks](output-system/layers-and-looks.md), [Screens and outputs](output-system/screens-and-outputs.md) |
| Stage displays, timers, messages, props, multiviews, Show Controls, and clear/recovery | [Stage, overlays, and operator views](features/stage-overlays-and-operator-views.md) | [Stage, announcements, and generated content](output-system/stage-announcements-generated-content.md), [Operator workflows and automation](operator-workflows/show-controls-and-recovery.md) |
| Macros, slide actions, markers, timeline/timecode, devices, API, Network Link, Control, and Remote | [Automation, control, and remotes](features/automation-control-and-remotes.md) | [Operator workflows and automation](operator-workflows/show-controls-and-recovery.md), [Feature inventory](feature-inventory.md) |
| Seats, activation, support files, sync/migration, preferences, credentials, copyright, localization, and diagnostics | [Administration, portability, and preferences](features/administration-portability-and-preferences.md) | [Library and show management](library-show-management/libraries-playlists-integrations.md), [Sources](output-system/sources.md) |

## Scalable Documentation Rules

When adding more ProPresenter research:

- Put **current user-facing behavior** in the relevant `features/*.md` page.
- Put **implementation consequences and contracts** in `feature-inventory.md` or the matching subsystem deep dive.
- Put **render/output pipeline design** in `render-engine/` or `output-system/`.
- Put **source links and limitations** in `output-system/sources.md`.
- Preserve the distinction between feature behavior, ChurchPresenter architecture decisions, and known source limitations.

## Coverage Notes

This reference aims to describe the complete ProPresenter product model, but source depth is uneven across official public material:

- **Well-covered current areas**: screens/outputs, Looks, output layers, stage layouts, timers, Media Inspector, media management, video/audio inputs, audio routing, alpha key, NDI, capture/RTMP/Resi, Quick Search, SongSelect, MultiTracks, Planning Center, Bibles, Remote, Control, Network Link, TCP/IP API, MIDI, DMX, sync, and migration.
- **Current but thinner areas**: dedicated Props, dedicated Messages, Masks, calendar scheduling, timecode, Stream Deck specifics, ProContent acquisition internals, copyright display UI, localization UI, and detailed library-creation UI.
- **How to handle thin areas**: keep the model conservative, cite the current feature page or adjacent current KB articles that expose the behavior, and record the limitation in `output-system/sources.md` or `feature-inventory.md` before turning it into an implementation requirement.

## Complete Feature Inventory

### Content Authoring

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Seamless live editing | Operators can add or edit videos, images, audio, text, and slide content before or during a live presentation without interrupting active output. | Editing state, prepared cues, thumbnails, and live output frames must be decoupled. Mutating a document should invalidate future cues without tearing down the current program frame unless the live payload is intentionally replaced. |
| Custom slide objects | Slides can contain text, shapes, media objects, and video inputs with custom fills and styling. | Treat slides as scene graphs with typed nodes. Do not make WinUI/AppKit views the source of truth for slide content. |
| Text/background effects and fills | Text, shapes, and backgrounds can receive visual treatments, including media/video fills. | Effects belong to renderer-capable style metadata. The model should preserve unsupported effects rather than flattening content during import. |
| Timers and dynamic text | Slides and generated overlays can show live timer or runtime data. | Dynamic text should resolve through runtime providers at render time. It is not static text in the document. |
| Reflow Editor | Operators edit all lyric/text content in a continuous text workflow, split or combine slides, and adjust breaks quickly. | Reflow is a projection over presentation text, groups, arrangements, and slide ids. It should not be a separate file format. |
| Quick Edit | Operators can fix text in-place quickly, commonly from a slide/right-click path. | Quick text changes should use the same document mutation and invalidation pipeline as full edit mode. |
| Themes | Themes apply consistent formatting to slides, presentations, libraries, Bible results, and screen-specific Looks. | Themes are reusable content/layout documents, not only global style settings. Looks may apply alternate theme variants per output screen. |
| Groups and arrangements | Songs can be divided into groups such as verse/chorus/bridge and rearranged for different service flows. | Arrangements should reference group ids and occurrences instead of duplicating slide content. |
| Presentation timelines | Slides, media, actions, and macros can be played with custom timings recorded or authored by the operator. | Timeline playback should compile to scheduled live commands and coexist with manual operator recovery. |
| Slide actions | A slide can trigger additional work such as media, audio, timers, macros, stage layouts, Looks, clears, or device commands. | Slide activation should produce an action batch consumed by the same command executor used by remotes, macros, keyboard shortcuts, and devices. |

### Organization and Generated Content

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Libraries | Presentations live in libraries and can be reused across services. | Libraries should own presentation documents. Playlist entries should reference library-owned documents. |
| Service playlists | A playlist orders presentations, generated items, media, headers, and placeholders for a service or event. | Playlist items are per-occurrence records. Duplicate references to the same presentation are valid and should not be collapsed. |
| Playlist templates | Reusable playlist structures can include headers, placeholders, and recurring presentations. | Templates should preserve intended flow before all content exists. A playlist item may be structural and non-executable. |
| Quick Search | Searches libraries and connected content sources, with preview and add-to-playlist workflows. | Search should be source-aware and produce references/import candidates with provenance. |
| SongSelect by CCLI | SongSelect can be searched/imported and can participate in CCLI reporting. | Imported songs need source identity, licensing/reporting metadata, and local-edit behavior. |
| MultiTracks | The automation page describes importing lyrics, chord charts, MIDI cues, and related song data from MultiTracks. | Integration data may create presentation content, chord/stage data, and automation cues. Keep imported identity separate from local content ids. |
| Planning Center | Services can be imported, linked to local content, updated, and converted from sequences into arrangements. | External service items should have stable external ids, local links, hidden/skipped state, and update state. |
| Bibles | Scripture can be searched by passage/range/keyword, generated into slides, themed, saved, copied, or added to playlists. | Bible output is generated presentation content with translation/license/source metadata and theme selection. |
| ProContent | ProPresenter integrates a large online media library with free and subscription assets. | Treat online media acquisition as an asset source. It should feed normal media asset/cue models rather than a special playback path. |
| Imports/exports/packages | ProPresenter imports files and exports presentations, bundles, playlists, and media-inclusive packages. | Package boundaries should distinguish document data, media assets, shared support files, and machine-local bindings. |

### Media, Audio, and Inputs

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Images and videos | Broad image/video formats can be used as foreground media, background media, or slide objects. | Asset identity, cue usage, and slide-object usage are different contracts. |
| Media Bin | A live media cue library with playlists, folders, thumbnails, video-input playlist, views, and transitions. | Media Bin is a cue system, not a file browser. It needs cue metadata, playlist state, and live layer state. |
| Audio Bin | A separate audio cue/playlist surface with transport controls. | Audio playback state must be independent from slide selection and visual output routing. |
| Foreground/background roles | Media has foreground/background semantics with distinct retrigger, duration, loop, and composition behavior. | Do not hide this as an import-only flag. It affects live layer behavior and clear/recovery logic. |
| Media/audio cue inspectors | Per-cue settings include scaling, crop, alignment, thumbnail, in/out, delay, rate, duration, transitions, effects, volume, routing, and playback markers. | The same asset can appear in many cues with different overrides. |
| Managed/referenced media | Media can be referenced at original paths or copied into managed app storage, with search paths and cleanup tools. | Store original path, resolved path, storage policy, missing state, and relink diagnostics. |
| Video inputs | Camera/SDI/NDI/Syphon or capture sources can be configured, previewed, triggered, and used in slides. | Live video inputs should be cue-like assets with device/source health and optional audio links. |
| Audio inputs and routing | Internal channels, input/output matrices, per-media routing, SDI/NDI audio, volume, delay, mute/solo, and test tones are supported. | Visual output and audio routing share cues but need separate routing and diagnostics models. |

### Screens, Outputs, and Broadcast

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Multiple screens | One computer can drive up to eight unique outputs and send different compositions to different screens. | Separate logical screens from physical, network, capture, or placeholder endpoints. |
| Audience and stage screens | Audience screens render show content; stage screens render confidence/operator information. | Audience frames and stage frames are different compositor products with different data providers. |
| Looks | Looks decide which output layers appear on each audience screen and can apply alternate themes. | Looks are routing/layout presets, not visual themes. |
| Fixed output layers | ProPresenter's output model includes audio, messages, props, announcements, slide, media, live video, and mask. | Reserve stable layer identities even if early UI only exposes slide/media. |
| Announcements | A separate presentation lane can run for lobby displays, streams, or pre-service loops while the main presentation continues. | Announcements are a routed layer, not a second app instance. |
| Props | Reusable text/media overlays can be shown, hidden, triggered, and automated. | Props are persistent overlay documents with independent layer state. |
| Masks | Custom shapes or transparent areas can hide unwanted output or create projection effects. | Masks are composition constraints and may be screen-specific. |
| Edge blend | Multiple projectors can be blended into a wider visual canvas. | Model grouped screens and per-endpoint calibration without requiring implementation in the first output pass. |
| Alpha key | Key/fill or alpha-capable output supports broadcast overlays and lower thirds. | Preserve alpha mode and transparency in render contracts. |
| SDI | Blackmagic hardware can provide SDI output and input. | SDI endpoints require device capability discovery, driver health, video mode, audio, and optional key/fill. |
| NDI | Network video output/input can send screens over Ethernet. | NDI endpoints need network discovery, bandwidth/CPU expectations, health, and diagnostics separate from local monitors. |
| Syphon | macOS can share output frames with other local apps through Syphon. | Keep endpoint abstractions platform-specific without changing the logical screen model. |
| Recording and RTMP streaming | Capture can record or stream a configured screen with codec, resolution, frame rate, elapsed time, and health. | Capture should consume resolved screen frames instead of forking the render pipeline. |
| Resi | The automation page describes resilient streaming integration through Resi. | Treat Resi as a specialized stream destination/integration, not a new content source. |

### Stage and Operator Surfaces

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Stage Display | Confidence screens show layouts tailored to stage talent. | Stage output is a dashboard compositor with layout elements and runtime data providers. |
| Flexible stage layouts | Stage layouts can be changed manually, by slide actions, by remotes, or by macros. | Stage layout changes are live commands targeted to one or more stage screens. |
| Current/next text | Stage layouts can show current and upcoming slide text with high contrast and automatic scaling. | Stage text is derived from current live state and arrangement context, not copied audience output. |
| Timers and clocks | Time of day, countdown, countdown-to-time, elapsed time, and video countdowns are stage-visible. | Timer state should be shared generated state consumed by stage, audience, props, messages, and headers. |
| Stage messages | Operators can send private messages to stage screens. | Stage messages are separate from audience messages. |
| Live screen previews | Stage or operator views can display current output preview frames. | Previews should subscribe to resolved screen frames and avoid re-rendering independently. |
| Operator multiviews | Operators can create multiview-style layouts showing inputs/outputs. | Multiview is a stage/output layout pattern using screen preview objects and labels. |
| Show Controls | Live panels expose Audio Bin, Stage, Timers, Messages, Props, and Macros. | The show UI needs persistent access to non-slide controls during live operation. |
| Clear buttons and groups | Operators can clear individual layers or named groups. | Clear groups should be first-class commands with layer/playback membership. |

### Automation, Control, and Remote Operation

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Macros | Named batches of actions can be triggered manually, from slides, from devices, or on demand. | Macros are serialized action batches with display metadata and deterministic results. |
| Playback markers | Media timelines can trigger macros or stage messages at specific times. | Media playback state should raise marker events into the command executor. |
| Timecode | Presentations can synchronize with timecode-enabled hardware/software. | Timecode is an external clock/source driving scheduled commands. |
| Calendar scheduling | Announcements, loops, streaming, and other needs can be scheduled in advance. | Scheduling should enqueue or activate command batches against known screens/layers. |
| Stream Deck | Stream Deck can trigger content, macros, messages, and capture operations. | Device events should call the same command API as UI and remotes. |
| MIDI/DMX | MIDI/DMX can control slide changes, video playback, lighting-console integration, and other live actions. | Device protocol adapters should translate events into typed commands. |
| API | ProPresenter exposes API integration for custom workflows. | Design ChurchPresenter commands and queries with authorization, source metadata, and observability from the start. |
| Network Link | Multiple local computers can be linked for extra outputs, alternate content, or redundancy. | A future link model needs command replication, instance identity, health, and conflict handling. |
| ProPresenter Control | A browser control surface can inspect/control Looks, macros, audio, capture, screens, playlists, timers, messages, transport, stage, props, and connected instance identity. | Local UI, web control, and remote clients should converge on one command/query surface. |
| ProPresenter Remote | Mobile/tablet remote can control slides, timers, macros, props, messages, audio playlists, libraries, playlists, collections, stage mode, slide notes, and custom remote views. | Remote readiness requires stable live state queries, permissions, and command-source diagnostics. |

### Administration and Portability

| Feature | What ProPresenter Does | Architecture Notes |
|---|---|---|
| Flexible seats and activation | Teams can add/remove seats, activate/deactivate devices, and build shows without activating a seat. | Licensing/activation should not be intertwined with local document authoring or read-only show preparation. |
| Team/account management | Subscription plans include team/user account management and support access. | Separate cloud/account state from local content state. |
| Support files | Screen configuration, Looks, stage layouts, props, messages, timers, macros, groups/labels, and interface settings are portable support files. | Shared configuration should have explicit schemas separate from machine-specific devices and monitor ids. |
| Sync and migration | ProPresenter syncs/migrates libraries, media, playlists, themes, and support files through a repository-style flow. | Sync/import operations need previewable replacement/delete semantics. |
| Preferences | General, screens, import, groups, input, network, sync, services, audio, advanced, devices, and update settings affect product behavior. | Preferences should be categorized by portability: shared show config, user preference, credential, cache, and machine binding. |
| Copyright display | Worship workflows include lyric/scripture copyright display/reporting expectations. | Copyright metadata should travel with generated/imported content and be renderable through themes/linked text. |
| Localization | User-guide material identifies localization as a product area. | Text resources, culture-sensitive formatting, Bible language metadata, and date/time formatting should not be hardcoded into output documents. |

## Cross-Feature Architecture Lessons

ProPresenter works because the operator can trigger one intent and have many subsystems respond coherently. A slide click can change slide output, start media, play audio, switch Looks, change stage layouts, start timers, fire macros, update remote state, and affect capture/streaming indicators. A separate click can clear only media while leaving slide text live, or show a prop without changing the current presentation.

ChurchPresenter should preserve these boundaries:

- Content documents describe what can be shown.
- Cues and actions describe what should happen when content is triggered.
- Live state records what is currently active on each layer, screen, player, timer, and capture session.
- Looks and stage layouts decide how the same content state is presented to each screen.
- Output endpoints deliver resolved frames to monitors, network transports, hardware devices, capture sessions, or placeholders.
- Remote, macro, device, calendar, keyboard, and local UI surfaces should all call the same command executor.

The native Windows and macOS apps can use different host adapters, media backends, device APIs, and windowing systems, but the core product model should stay shared: presentations, assets, cues, actions, layers, screens, Looks, stage layouts, timers, integrations, packages, and diagnostics.
