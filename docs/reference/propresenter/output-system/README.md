# ProPresenter Output System Reference

This reference summarizes official Renewed Vision / ProPresenter support material that is useful when designing ChurchPresenter's output engine. It is not a mirror of the help center; it is a local implementation-oriented model of how ProPresenter thinks about screens, outputs, layers, looks, stage displays, generated overlays, and transport/routing concerns.

## Output Docs Map

- [Mental model](mental-model.md) - the vocabulary and system shape to keep in mind.
- [Screens and outputs](screens-and-outputs.md) - ProPresenter's distinction between rendered screens and hardware/network destinations.
- [Layers and looks](layers-and-looks.md) - fixed output stack, layer visibility, alternate themes, clear behavior, and background/media interaction.
- [Stage, announcements, and generated content](stage-announcements-generated-content.md) - stage displays, announcement layer, messages, props, masks, and show controls.
- [Output types and signal routing](output-types-and-signal-routing.md) - system displays, NDI, SDI/Blackmagic, Syphon, placeholders, alpha/key output, audio, grouped/mirrored/edge-blended screens.
- [ChurchPresenter design notes](churchpresenter-design-notes.md) - implications for our app architecture.
- [Sources](sources.md) - official Renewed Vision links, coverage notes, and source limitations for the wider ProPresenter reference set.

## Related Reference Areas

- [ProPresenter feature map](../features.md) - the feature-first index that connects public Renewed Vision feature pages to detailed ChurchPresenter architecture implications.
- [Media and audio management](../media-management/media-audio-assets.md) - the asset, cue, live video, and audio systems that feed output layers.
- [Library and show management](../library-show-management/libraries-playlists-integrations.md) - playlists, presentations, arrangements, themes, integrations, sync, and packages.
- [Operator workflows and automation](../operator-workflows/show-controls-and-recovery.md) - clear groups, timers, messages, props, macros, slide actions, remotes, capture, and live recovery.
- [ChurchPresenter architecture](../../../architecture/target-architecture.md) - intended app architecture for commands, outputs, portability, and recovery.

## High-Level Summary

ProPresenter treats each audience or stage screen as a configured render target. A screen may map to one output device, multiple mirrored outputs, a grouped wall, or an edge-blended projector set. The physical or network destination is the output; the screen is the thing ProPresenter renders.

Audience screens receive a composited stack of fixed layers. Looks decide which audience screens receive which layers, and can apply alternate themes to presentation content for a specific screen. This is how one operator can drive main room lyrics, lower-third stream graphics, and lobby announcements at the same time.

Stage screens are related but distinct. They use stage layouts rather than audience looks, and they can show current/next slide text, preview images, notes, timers, screen previews, chord charts, capture status, and stage-only messages.

The model to carry into ChurchPresenter is: separate render surfaces from transport outputs, keep layers independently clearable and routable, make routing presets first-class, and avoid binding all outputs to the same visual composition.
