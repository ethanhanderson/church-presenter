# Presentations and Slides

This reference captures ProPresenter presentation and slide behavior that matters to ChurchPresenter's document model, editor surfaces, and slide renderer. It is grounded in official Renewed Vision product/help material and should be read alongside the output, media, library, and operator-workflow references in this folder.

## Source Basis

- [ProPresenter Media & Slides](https://renewedvision.com/propresenter/media-and-slides) describes custom slide objects, text/background effects and fills, timers/dynamic text, Reflow, Quick Edit, themes, playlists, quick search, arrangements, video inputs, and broad image/video support.
- [Understanding the ProPresenter User Interface](https://support.renewedvision.com/hc/en-us/articles/360041345954-Understanding-The-ProPresenter7-User-Interface) describes Show/Edit/Reflow modes, the Text and Theme toolbar controls, Slide View, arrangements, operator notes, timelines, presentation destination, slide transitions, view modes, media actions, preview, clears, layers, and Show Controls.
- [How to Use Reflow Editor in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/34513457866899-How-to-Use-Reflow-Editor-in-ProPresenter) describes text splitting, slide combining, line breaks, preview sizing, and per-slide transition/theme access from Reflow.
- [Themes in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/11910559859603-Themes-in-ProPresenter) describes theme slides, theme canvas/object lists, z-order, theme inspector settings, applying themes to slides/presentations/libraries/Bibles, optional media actions in themes, and applying themes to screens through Looks.
- The official ProPresenter 7 User Guide is useful secondary material where support articles are thin. It confirms presentation headers, operator notes, groups/arrangements, timelines, destination target, presentation transitions, global transition precedence, slide views, clear layers, audience Looks, and stage-layout editing.

## Product Model

A ProPresenter-style presentation is more than a list of visual slides. In ChurchPresenter it should be a library-owned content document that can appear in playlists by reference, carry grouping/arrangement metadata, define slide-level actions, target different output layers, and be reformatted by themes or Looks.

ChurchPresenter should model these concepts explicitly:

- `Presentation`: durable document identity owned by one library, title, metadata, default theme, default transition, optional external source/provenance, and media/font references.
- `Slide`: canvas content plus notes, group membership, disabled state, hot key, transition/build metadata, media cues, and slide actions.
- `Group`: named/colored section such as verse, chorus, bridge, reading, or announcement segment.
- `Arrangement`: ordered group occurrences, including repeated groups, without duplicating slide content.
- `PresentationInstance`: playlist usage/reference of a library-owned presentation with selected arrangement, destination, and show-specific overrides.
- `Theme`: reusable layout/style document that can supply slide templates, text boxes, background settings, object defaults, and media actions.
- `Look variant`: per-screen theme/layout selection used by audience Looks to reformat the same slide content for different outputs.

The important boundary is that libraries own presentation documents, while playlists and live state decide when and where that content runs. Playlist usage must not imply file ownership: removing a presentation from a playlist only removes that occurrence, while deleting the presentation from its owning library deletes the bundle and every app reference to it.

Presentation deletes should be routed through application content-management services rather than page-local file deletion. A successful delete must remove the owned `.cpres` file, remove catalog/library/playlist references, clear stale workspace selection and recent-file state, invalidate `ShowSessionCache`, prepared cues, thumbnails, scene/render snapshots, and bundle asset caches, and publish a presentation-deleted content change so previews, output, diagnostics, and audit state can respond coherently.

## Slide Canvas

The slide canvas should be treated as a scene graph, not a pile of WinUI controls. ProPresenter's landing page calls out layered objects such as shapes, text, media, and video inputs, plus fills, styling, text/background effects, timers, and dynamic text. The Theme Editor article also makes object z-order explicit through an object list.

ChurchPresenter scene nodes should support at least:

- Text objects with font family/weight/style, size, alignment, vertical alignment, line height, letter spacing, padding, text fit, fills, strokes/outlines, shadows, and future text effects.
- Shape objects with fills, strokes, corner radius, opacity, transforms, and common shape families.
- Media objects for images and video with fit/crop/stretch policy, opacity, playback policy, and asset identity separate from path.
- Live video input objects as cue-like source nodes with device identity and health.
- Web or external content objects with safe snapshot/live-host boundaries.
- Vector/path objects for imported graphics and custom shapes.
- Dynamic text/timer tokens resolved from runtime providers rather than serialized as static output text.

The scene graph is internal to the slide/presentation layer. It must not replace the output layer stack. Object z-order decides order inside one slide scene; Looks and the backend render engine decide how slide, media, announcements, messages, props, live video, masks, and audio route to each screen.

## Backgrounds and Media

Slides can have their own backgrounds and media objects, while ProPresenter also has separate media/background layers and Media Bin cues. ChurchPresenter should preserve those distinctions:

- A slide background is part of the slide scene and can be solid, transparent, gradient, image, or video.
- A media object placed on the slide canvas is part of the slide payload and follows slide visibility/build rules.
- A media action or Media Bin cue targets the media layer and can continue independently across slide changes.
- A theme can include background or media actions, but those actions must still enter the shared command/action pipeline.

This matters because output Looks may route the slide layer and media layer differently per screen. A stream lower-third Look might show slide text without the room background; a lobby screen might show announcements while the main room continues a different presentation.

## Themes

Themes are reusable formatting documents. They can be applied to individual slides, selected slide ranges, entire presentations, libraries, generated Bible slides, and output screens through Looks. Theme slides have their own canvas, object list, z-order, background attributes, and inspector-driven object settings. Current ProPresenter material also notes media actions in themes.

ChurchPresenter implications:

- Theme slides should be first-class documents with stable ids, names, layout roles, base size, background, object graph, media actions, and metadata.
- Applying a theme should be a content transformation or style binding, not an output-window mutation.
- Theme application must preserve slide text, notes, group/arrangement identity, slide actions, and media cues unless the operator intentionally replaces them.
- Looks should be able to select alternate theme/layout variants per audience screen so one slide can render full-screen in the room and lower-third on stream.
- Theme media actions should compile into normal actions; they should not bypass the command executor.

## Reflow and Quick Editing

Reflow is a text-centric workflow for preparing lyrics and generated text quickly. Official help covers splitting text across slides, combining slides, adjusting line breaks, preview sizing, and accessing transition/theme options on each slide.

ChurchPresenter should treat Reflow as a projection over the presentation model:

- Splitting text creates or reuses slide records while preserving group/arrangement intent where possible.
- Combining slides merges text without losing notes, actions, transitions, or media cues silently.
- Line breaks are text content changes, not new slides.
- Reflow edits must invalidate prepared cues and thumbnails because slide content changed.
- Reflow should not be a separate file format or a separate renderer.

Quick Edit is the narrow typo-fix path. It should mutate the same slide text model and go through the same invalidation and cue-refresh behavior as Reflow/Edit.

## Groups, Arrangements, and Timelines

ProPresenter presentations commonly represent songs where slides belong to verse/chorus/bridge groups and arrangements repeat or reorder those groups. The UI and user guide also describe presentation timelines that can drive custom timings between slides or record slide advancement against an audio track.

ChurchPresenter implications:

- Groups own stable ids and display labels; arrangements reference group ids, not copied slide arrays.
- A playlist item can choose an arrangement without changing the library-owned source presentation.
- Repeated group occurrences need instance identity so live state, selection, and stage "next" content can distinguish repeated slides.
- Auto-advance, go-to-next timers, and future timelines should compile to scheduled live commands rather than direct view timers.
- Timeline playback must coexist with manual operator advance and recovery controls.

## Slide Actions

Slide activation can do more than show slide content. Existing ProPresenter references and this repo's output docs identify actions such as media/audio playlist triggers, Look changes, stage layout changes, timers, messages, props, macros, clears, capture commands, and stage-only behavior.

ChurchPresenter should serialize slide actions as declarative action definitions. At take-live time, those definitions become an `ActionBatch` through the same command executor used by UI clicks, keyboard shortcuts, macros, remotes, and timers.

Required properties for action definitions:

- stable id and action kind,
- display label and optional icon/color/group metadata,
- target scope such as layer, screen, stage screen, Look, timer, macro, media cue, or capture session,
- payload reference rather than embedded imperative UI code,
- execution policy for ordered, parallel, delayed, conditional, or stage-only behavior,
- diagnostics metadata for recovery UI.

## Show Views and Operator Data

Show mode can present slides in grid/table/easy/outline-style views, with slide notes, group rows, thumbnail size controls, arrangement grouping, transition/action indicators, and live state indicators. Those are operator projections over the same presentation and live-state model.

ChurchPresenter should keep these concerns separate:

- Thumbnail generation uses the same slide scene compiler as previews and output, with thumbnail-specific host settings.
- Slide notes, operator notes, disabled state, hot keys, action counts, transition badges, and media badges are metadata overlays in the operator UI.
- Selection is not live state. The selected slide can differ from the slide currently routed to the audience slide layer.
- In Show mode, a normal primary click on a slide card is an activation gesture: it takes that slide live through the shared command/action path and updates program/live indicators, but it does not move the operator selection cursor.
- Selecting a slide card for slide-item commands is explicit. Ctrl+click selects the card without taking it live. Shift+click extends selection across the visible deck so multiple slide cards can be selected for selection-scoped workflows. Right-click context menus resolve their target from the clicked card and must not need to mutate selection before opening.
- Clicking outside slide cards clears slide-card selection only, like clearing a text selection. It must not clear or change the slide that is currently live on the audience slide layer.
- Selection chrome and live chrome must remain independently visible. A slide can be selected but not live, live but not selected, both selected and live, or neither; keyboard shortcuts use the selected slide, while live advancement uses the live/program slide when there is no explicit selection.
- The slide-output-layer live indicator is a green ring around the card whose presentation path, slide id, and arrangement instance key match the current program payload on `OutputLayerKind.Slide`. Do not show that green ring for announcement-layer presentation playback, cleared/suppressed slide layers, or operator selection alone.
- Destination target determines whether activation affects the slide/presentation layer, announcement layer, stage screens, or another explicit target.

## Rendering Implications

The current code already has typed presentation/project models and a `SlideStageView` that renders text, shapes, media, web, vector, backgrounds, layered media, thumbnails, previews, and output. For ProPresenter-level parity, that view should become a host adapter over a shared scene compiler rather than the renderer itself.

The target shape is:

```text
PresentationProject + Theme + Look variant + runtime tokens
  -> SlideSceneCompiler
  -> immutable SlideScene
  -> backend RenderPayloadDescriptor / AudienceRenderFrame
  -> WinUI, thumbnail, preview, export, and future transport host adapters
```

Key requirements:

- One compiler path for editor preview, thumbnails, audience output, stage previews, and export.
- Immutable scene snapshots with stable node ids for diffing and diagnostics.
- No library search, media relink, token lookup, theme lookup, or arrangement traversal inside output-window apply code.
- Editor adorners such as selection rectangles, resize handles, snapping guides, rulers, and inspector overlays must stay outside serialized slide scenes and audience frames.
- Media player lifecycle should be owned by host adapters and coordinated through playback diagnostics, not hidden in slide content.
- Transparent backgrounds and alpha modes must survive through render frames for lower thirds and future NDI/SDI keying.
- Benchmarks should cover scene compile time, host apply time, thumbnail generation, media startup, text layout cost, memory, dropped/stale frames, and high object counts.

## Current ChurchPresenter Fit

Existing implementation pieces already point in the right direction:

- `PresentationProject`, `PresentationSlide`, `SlideLayer`, `TextLayer`, `ShapeLayer`, `MediaLayer`, `WebLayer`, `VectorLayer`, `SlideBackground`, `SlideTransition`, `BuildStep`, `SlideActionDefinition`, groups, and arrangements live in `ChurchPresenter.Application`.
- `ThemeTemplate` and `ThemeTemplateSlide` model theme slides and object lists.
- `PresentationTextWorkflowService` implements text editing and Reflow-like slide splitting on the typed project model.
- `CuePreparationService` prepares slide/media cues before take-live time.
- `BackendRenderEngine` resolves command/action state into audience and stage render frames.
- `SlideStageView` is the current WinUI rendering surface, but it mixes scene interpretation, XAML creation, media player lifecycle, thumbnail behavior, and output-layer composition in one control.

The replacement work should preserve the useful document contracts while moving slide-scene compilation and render-frame integration behind testable application-layer services and thin WinUI host adapters.

## Open Product Questions

- Which ProPresenter object/effect families are required for first parity: text, shapes, image/video, live video, web, vectors, dynamic text, builds, masks, props, or all of them?
- Should ChurchPresenter intentionally match ProPresenter's UI terminology for "Presentation Layer" versus this repo's newer "Slide layer" terminology?
- How much of ProPresenter timeline recording/playback is needed for the first engine replacement?
- Should theme application be destructive, style-linked, or selectable per slide instance?
- Which dynamic text providers are in scope first: timers, clocks, Planning Center/service data, scripture metadata, media countdown, or custom fields?
