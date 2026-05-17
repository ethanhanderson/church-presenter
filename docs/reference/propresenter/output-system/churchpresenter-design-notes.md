# ChurchPresenter Design Notes

These notes translate the ProPresenter research into design guidance for ChurchPresenter.

## Keep Render Screens Separate From Output Devices

ChurchPresenter should model a logical output screen independently from the monitor/window/device that receives it.

Suggested conceptual entities:

- **Screen**: named render target with kind, resolution, background/fallback color, and stage/audience role.
- **Output endpoint**: monitor/window/transport destination.
- **Screen mapping**: links one screen to one or more endpoints.
- **Placeholder endpoint**: an endpoint with resolution and name but no active device.

This lets us support simple monitor outputs now while leaving room for mirroring, placeholders, and future NDI/SDI endpoints.

## Treat Looks as Routing Presets

ProPresenter Looks are not just visual styles. They are per-screen layer-routing presets with optional theme overrides.

For ChurchPresenter, "Look" or "feed preset" should answer:

- Which audience screens are active?
- Which layers appear on each screen?
- Does any screen use an alternate theme or layout variant?
- Which layers should clear together?

Avoid burying routing rules inside individual slide definitions. Slide actions may switch a look, but the preset itself should remain inspectable and reusable.

## Keep Slide and Media Layers Independent

ProPresenter's slide/media/background behavior is a useful warning. If media can be a slide action, background, foreground, or slide object, then the output engine needs explicit rules for what can cover what.

For ChurchPresenter:

- Slide text/objects and media playback should have separate live states.
- A slide background should not silently hide the media layer unless that is an explicit composition rule.
- Clearing slide content should not necessarily clear active media.
- Media playback transport should be tied to the media layer, not to all presentation surfaces.

## Model Generated Layers as First-Class

Announcements, messages, props, and masks are not ordinary slides:

- Announcements can run as a second presentation lane.
- Messages are operator-triggered overlays.
- Props are persistent overlays that can outlive slide changes.
- Masks affect the shape/transparency of output.

Even if ChurchPresenter initially implements only some of these, reserve layer slots and clear/routing semantics so later additions do not require rethinking the whole output stack.

## Stage Output Is a Dashboard

Stage displays are not just mirrored audience screens. They can show current/next slide text, notes, timers, screen previews, chord charts, stage-only messages, and operator status.

For ChurchPresenter:

- Use a stage-layout model separate from audience Looks.
- Allow a stage screen to preview any audience output.
- Consider "stage only" slide actions for rehearsal, confidence prompts, or operator-only notes.
- Do not let stage layout changes imply audience output changes.

## Design for Operator Recovery

The ProPresenter output system exposes layer clear buttons, clear groups, active look selection, screen previews, and performance stats. These are recovery tools as much as features.

ChurchPresenter should make it easy to answer:

- What is live on each screen?
- Which layer is producing an unexpected element?
- Which look/feed preset is active?
- Can I clear only the problem layer?
- Is the output endpoint connected and healthy?

## Near-Term Scope Suggestions

For the current Windows app, a practical progression is:

1. Audience screens as named local output windows.
2. Separate slide and media layer state.
3. Looks/feed presets that route slide/media layers per screen.
4. Stage screen layout model with current/next slide and screen preview.
5. Announcement layer as a second presentation lane.
6. Messages/props/masks as specialized overlay layers.
7. Placeholder endpoints and diagnostics.
8. Future NDI/SDI/alpha/audio routing.

## Dependencies Outside the Output Engine

The output engine should not be planned in isolation. The expanded reference set identifies several upstream systems that directly affect rendered output:

- [Media and audio management](../media-management/media-audio-assets.md): managed versus referenced media, Media Bin/Audio Bin playlists, cue inspector overrides, live video inputs, and audio routing.
- [Library and show management](../library-show-management/libraries-playlists-integrations.md): libraries, playlists, templates, arrangements, themes, Planning Center-style links, generated Bible/SongSelect content, sync, and packages.
- [Operator workflows and automation](../operator-workflows/show-controls-and-recovery.md): clear groups, timers, messages, props, macros, stage-only actions, remote commands, and capture/streaming health.
- [ChurchPresenter target architecture](../../../architecture/target-architecture.md): intended command, output, portability, and diagnostics architecture.

The implementation contract should include these systems early even if some are phased later. Otherwise, slide/media output may work initially but become difficult to extend to ProPresenter-like service workflows.

Sources are summarized in [Sources](sources.md).
