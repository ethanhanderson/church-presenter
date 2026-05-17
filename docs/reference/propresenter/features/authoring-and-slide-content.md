# Authoring and Slide Content

ProPresenter's authoring model is broader than a slide deck editor. It combines structured song/text preparation, visual slide composition, reusable themes, runtime dynamic text, timelines, and slide-triggered actions. ChurchPresenter should treat authoring as a durable content model that can be rendered by multiple native hosts, not as view-specific UI state.

## Source Basis

- [Media & Slides](https://renewedvision.com/propresenter/media-and-slides)
- [Main ProPresenter page](https://renewedvision.com/propresenter)
- [Automations & Control](https://renewedvision.com/propresenter/automations)
- [How to Use Reflow Editor](https://support.renewedvision.com/hc/en-us/articles/34513457866899-How-to-Use-Reflow-Editor-in-ProPresenter)
- [Slide View Options](https://support.renewedvision.com/hc/en-us/articles/360041344174-Slide-View-Options-in-ProPresenter)
- [Themes in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/11910559859603-Themes-in-ProPresenter)
- [Using Looks](https://support.renewedvision.com/hc/en-us/articles/360041407174-Using-Looks-to-Show-Different-Screen-Content-in-ProPresenter)
- [Using Macros](https://support.renewedvision.com/hc/en-us/articles/4402663090323-Using-Macros-in-ProPresenter)
- [Playback Markers](https://support.renewedvision.com/hc/en-us/articles/7171761588371-How-to-use-Playback-Markers)
- [Presentations and slides](../presentations-and-slides.md)
- [Library and show management](../library-show-management/libraries-playlists-integrations.md)

## Slide Documents

Every presentation consists of slides, and a slide can carry more than visible canvas content. It can indicate group membership, labels, notes, attached media, attached actions, transition information, timeline behavior, destination, and live-state markers in show views.

For ChurchPresenter, the minimum document shape should include:

- Presentation identity, owning library, default theme, default transition, provenance, and media/font dependencies.
- Slide identity, canvas scene, notes, group/arrangement membership, disabled state, hot key, transition/build metadata, and action references.
- Arrangement identity and ordered group occurrences for song/service variants.
- Playlist occurrence metadata separate from the library-owned presentation.

The slide document should be host-neutral. WinUI, AppKit, thumbnails, previews, export, and future web/remote views should all consume compiled scene snapshots from the same model.

Current ProPresenter show views expose the document in multiple operator projections:

- **Grid View** shows thumbnails that represent what will be sent to output, and clicking a slide sends it live immediately.
- **Table View** shows one column with thumbnail, slide text, slide notes, group labels, group color, and text formatting options for operator readability.
- **Easy View** combines thumbnails with reformatted operator-readable text; it can make media thumbnails appear differently from output and can optionally show media actions.
- **Slides by Group** can separate slides into group rows in Grid/Table/Easy views.
- **Thumbnail Background Color** is an operator-view option and does not change output; slide/editor background still controls rendered content.

Architecture implication: operator views are projections over a shared presentation/live-state model. They should not own slide content, output rendering, or live state.

## Custom Objects and Scene Graph

The public feature pages call out slide objects such as shapes, text, media, and video inputs, plus custom fills and stylings. The architecture consequence is that a slide is a scene graph:

- Text nodes with font, sizing, alignment, line spacing, fills, stroke/outline, shadow, fit policy, and dynamic-token runs.
- Shape nodes with fill, stroke, radius, opacity, transform, and effect metadata.
- Image/video nodes with asset reference, bounds, fit/crop/stretch policy, opacity, playback policy, and optional poster frame.
- Live video nodes with input/source identity and health.
- Dynamic generated nodes such as timer text, scripture metadata, current service data, or custom fields.

Scene graph z-order is internal to a slide. It must not be confused with the output layer stack. Looks route layers like slide, media, props, announcements, messages, live video, and mask after the slide scene has been compiled.

## Effects, Fills, and Backgrounds

ProPresenter emphasizes text/background effects and media/video fills. ChurchPresenter should preserve a structured style/effects model even when the first native renderer supports only a subset. Unsupported effects should remain in the document as known or opaque metadata so that future renderers, import/export, and cross-platform parity are not blocked by early Windows implementation limits.

Slide backgrounds, presentation/theme backgrounds, and the independent media layer need separate representation. A slide background is part of the slide scene; a media action can target the media output layer; a theme can supply backgrounds or media actions. This distinction is critical because Looks may route slide and media layers differently per screen.

## Themes

Themes are reusable formatting documents. They can apply to individual slides, slide ranges, presentations, libraries, Bible-generated slides, and audience screens through Looks. A theme can include theme slides, object lists, z-order, background attributes, object settings, and media actions.

ChurchPresenter should model themes as first-class content:

- Theme document identity and metadata.
- Theme slides/templates with role names and base sizes.
- Object/style defaults that can bind to presentation text.
- Optional media actions that compile into normal action definitions.
- Screen-specific variants selected by Looks.

Theme application should preserve source text, notes, groups, arrangements, slide actions, and media cues unless the operator intentionally replaces them. For architecture, a theme is not simply a CSS-like skin; it can reshape how content appears on a main room screen versus a stream lower-third screen.

Current theme details from the knowledge base:

- Theme Editor opens from the toolbar More menu.
- A theme contains one or more **Theme Slides** shown in a slide navigator.
- Theme slides can be added, renamed, duplicated, deleted, copied, or pasted.
- Each theme slide has an **Object List** whose draggable order controls z-order.
- The center canvas defines what will render; objects can sit outside the canvas but will not show on output unless they intersect the canvas.
- The Inspector is contextual. With no selection it shows theme-slide attributes such as theme size and background color. With an object selected it shows text, build, shape, or object properties.
- Starting in ProPresenter 7.11, media actions can be added to themes.
- When applying a theme from the Theme Selector, media actions can be enabled/disabled through the selector's image icon.
- Bible searches can apply themes from Bible View, with separate verse/reference object dropdowns.
- Looks can apply themes to screens so the same triggered slide text is reformatted on the fly.
- Theme storage paths differ by platform: macOS user workspaces under `~/Library/Application Support/RenewedVision/ProPresenter/User Workspaces`, and Windows local workspaces under `%AppData%\RenewedVision\ProPresenter\LocalWorkspaces`.

Architecture implication: theme documents need stable object ids, theme-slide ids, z-order, size, background, optional actions, and target bindings for generated content. Theme application and screen-specific theme substitution should be explicit operations.

## Reflow and Quick Edit

Reflow is a lyric/text preparation workflow. Operators see the text of a presentation continuously, split text into slides, combine slides, adjust line breaks, and access transition/theme choices without entering the full canvas editor.

Treat Reflow as an editing projection over the same presentation model:

- Splitting text creates or reuses slides while preserving group and arrangement intent.
- Combining slides merges content without silently deleting notes, actions, media, or transitions.
- Line breaks are text edits; slide breaks are structural edits.
- Reflow changes invalidate thumbnails, prepared cues, and future render snapshots.
- Quick Edit is the narrow in-context typo-fix path through the same mutation pipeline.

This matters because worship lyrics and scripture often require last-minute text correction while output remains live.

Current Reflow behavior:

- Split text across slides by placing the cursor after the last word of a slide and choosing **Insert Slide Break** or pressing `Option+Return`.
- Combine slides by placing the cursor at the beginning of the second slide and pressing Delete.
- Add line breaks by placing the cursor before a word and pressing Return.
- Use the bottom-right slider to resize slide previews.
- Open per-slide options such as transition and theme from the arrows on each slide.

Architecture implication: Reflow operations need semantic mutations: insert slide break, remove slide break, insert line break, update transition, and update theme. These should preserve non-text slide metadata whenever possible.

## Groups and Arrangements

Groups describe sections such as verse, chorus, bridge, reading, sermon point, or announcement segment. Arrangements describe ordered occurrences of those groups for a specific service context. The same presentation can appear multiple times in one playlist with different arrangements.

ChurchPresenter should make group and arrangement identities stable:

- Groups should have ids, labels, colors, and ordered slide membership.
- Arrangements should reference group ids or group occurrences rather than copied slide arrays.
- Playlist items should be allowed to select arrangements per occurrence.
- Stage current/next text and live navigation should understand repeated group occurrences.

## Dynamic Text and Timers

Timers and dynamic text are not just slide text with a changing string. They are runtime data bindings:

- Countdown, countdown-to-time, elapsed time, system clock, and video countdown.
- Planning Center/service timers.
- Scripture reference metadata.
- Media playback time remaining.
- Custom fields and future integration data.

The renderer should resolve dynamic text at frame/scene preparation time from a runtime provider. The document should store the binding, formatting, and fallback, not the current value.

## Slide Actions

The Automations page frames slide actions as the beginning of a broader action system: a slide can change inputs, clear media, trigger a macro, or run other production work.

ChurchPresenter should serialize slide actions declaratively:

- Stable action id, action kind, display label, icon/color/group metadata.
- Target scope such as layer, screen, stage screen, Look, timer, macro, media cue, audio cue, device, or capture session.
- Payload reference rather than imperative UI code.
- Execution policy for ordered, parallel, delayed, conditional, or stage-only behavior.
- Diagnostics metadata for recovery UI.

At take-live time, slide activation should compile the selected slide and its actions into an action batch. The same executor should run batches from slides, macros, remote clients, keyboard shortcuts, calendar events, playback markers, and device input.

Current action surfaces:

- Looks can be changed from slide actions by adding **Audience Look** to a slide.
- Stage layouts can be changed from slide actions, including per-stage-screen layout selection.
- Stage layout actions can choose **Stage Only** so audience output stays unchanged, or **Stage + Audience** to resume audience output.
- Macros can be added to slides by right-clicking a slide, choosing **Add Action > Add Macro**, or dragging a macro onto a slide.
- Timer actions can start, stop, reset, or reconfigure timers from a slide.
- Playback markers can carry actions at specific audio/video times.

Architecture implication: action definitions should be attached to slides, markers, macros, headers, and external controls with the same schema.

## Timelines and Transitions

ProPresenter supports timelines and transitions in addition to manual slide triggering. Timeline playback can schedule slides, media, actions, and macros against a recorded or authored time base. Transitions can be global defaults, presentation-level settings, slide-level overrides, or cue-specific media transitions.

ChurchPresenter should avoid view timers that mutate output directly. A timeline should enqueue scheduled commands against the live command executor, and transition resolution should belong to the layer payload being changed. A slide transition should not restart persistent media; a media transition should not imply that the slide layer changed.

Playback markers add a current, detailed timeline-adjacent behavior:

- Markers are supported on video and audio media, not images, and not media used as slide elements.
- Markers are added in the media inspector at the current playhead position.
- Marker rows include icon/color, timecode, optional name, and action icons.
- Markers can be renamed, recolored, duplicated, copied/pasted, moved to playhead, and deleted.
- Actions can be added by context menu, Action Palette drag/drop, Show Controls drag/drop, or media/audio drag/drop.
- Active media transport controls show marker icons; clicking a marker jumps playback to that marker and performs marker actions if present.
- Stage layouts can show playback marker data such as time to marker or marker name via linked text/countdown objects.

Architecture implication: media timelines, presentation timelines, and timecode should all become command sources. Marker action execution needs repeat/seek semantics and diagnostics.

## Cross-Feature Interactions

Authoring touches nearly every other subsystem:

- Themes interact with Looks for per-screen variants.
- Slide actions interact with media, audio, stage layouts, macros, timers, capture, and external devices.
- Reflow and Quick Edit affect thumbnails, prepared cues, playlists, and live-state diagnostics.
- Dynamic text uses timer, media, scripture, integration, and stage data providers.
- Arrangements affect Planning Center imports, stage current/next text, remote navigation, and live advancement.

The architecture goal is to keep all of that behavior attached to stable content/action contracts, not scattered across editor controls, slide cards, output windows, and remote handlers.
