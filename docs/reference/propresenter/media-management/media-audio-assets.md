# Media and Audio Management

This document captures ProPresenter media/audio behavior that should shape ChurchPresenter's asset, cue, playback, and output architecture.

Primary sources: [ProPresenter Media Management](https://support.renewedvision.com/hc/en-us/articles/360041815133-ProPresenter-Media-Management), [Understanding the ProPresenter User Interface](https://support.renewedvision.com/hc/en-us/articles/360041345954-Understanding-The-ProPresenter-User-Interface), [Media Import Presets](https://support.renewedvision.com/hc/en-us/articles/34512245702035-Media-Import-Presets-in-ProPresenter), [What is the Media Inspector](https://support.renewedvision.com/hc/en-us/articles/7200487649299-What-is-the-Media-Inspector), [Media Scaling Options](https://support.renewedvision.com/hc/en-us/articles/360011694174-Media-Scaling-Options), [Triggering Media and Audio Playlists from Slide Actions](https://support.renewedvision.com/hc/en-us/articles/360056439374-Triggering-Media-and-Audio-Playlists-from-Slide-Actions), [Setting Up A Video Input](https://support.renewedvision.com/hc/en-us/articles/360053482973-Setting-Up-A-Video-Input-in-ProPresenter), [Audio Routing](https://support.renewedvision.com/hc/en-us/articles/360052696094-Audio-Routing-in-ProPresenter), [Audio Outputs](https://support.renewedvision.com/hc/en-us/articles/360052697694-Audio-Outputs-in-ProPresenter), and [SDI/NDI Audio Outputs](https://support.renewedvision.com/hc/en-us/articles/360053282613-SDI-NDI-Audio-Outputs).

## Media Bin and Audio Bin

ProPresenter exposes the Media Bin as a live operational surface, not just an import folder. The UI article describes the Media Bin as the bottom section of the interface when opened. It contains video and image content, live video inputs, playlists, playlist folders, a dedicated video-input playlist, grid/list views, filtering, cue-size controls, and a global default transition for media actions.

The Audio Bin is part of Show Controls. It is used to trigger audio files, build audio playlists, and configure how audio will play. Audio playback has its own transport controls in ProPresenter Control and the remote app, separate from slide selection.

ChurchPresenter implications:

- Treat Media Bin and Audio Bin items as cue libraries, not just file-system paths.
- Model media playlists and audio playlists separately from service playlists.
- Keep direct cue triggering and slide-attached cue triggering on the same command path.
- Track active playback ownership by layer or audio channel so transport controls know what they control.

## Managed Versus Referenced Media

ProPresenter's default import behavior can reference a file at its existing path rather than copying the file into app-managed storage. If the original path disappears, thumbnails can gray out or show question marks and the cue will not play. The Media Management article describes a `Manage Media Automatically` setting that copies imported media into ProPresenter's Media Assets folder and references that managed copy instead.

It also describes `Media Search Paths`, which let the app automatically relink missing media by searching configured alternate folders.

ChurchPresenter should model:

- `MediaAsset` identity independent from the current physical path.
- Storage policy: managed copy, referenced path, or imported package asset.
- Original source path and current resolved path.
- Missing-media state with operator-visible diagnostics.
- Configurable search roots for relinking.
- Audit/relink operations that can be tested without output windows.

## Cleanup Boundaries

Renewed Vision's Clean Up Unlinked Media feature removes media files not referenced anywhere in ProPresenter. Its reference graph includes presentations or slides, playlists, themes, props, macros, and masks.

ChurchPresenter should not implement cleanup as "delete files not in the media list." Cleanup eligibility must traverse the whole content/action graph:

- Presentations, slide objects, and slide actions.
- Service playlists and media/audio playlists.
- Themes and media actions inside themes.
- Props, messages, macros, masks, and future generated overlays.
- Imported packages and external plan links.

Deletion should be explicit, previewed, and recoverable where possible because Renewed Vision notes that media cleanup is not undoable inside ProPresenter once confirmed.

## Foreground, Background, and Import Defaults

Media import presets distinguish foreground and background behavior:

- Scaling defaults can be separate for foreground and background media.
- Images can import as foreground or background with no duration, a fixed duration, or a random duration.
- Videos can import as foreground or background and stop or loop.
- Audio can stop, loop, or play the next track automatically.

Media scaling options include:

- `Scale to Fit`: show all content without stretching, with bars if needed.
- `Scale to Fill`: crop while preserving aspect ratio.
- `Stretch to Fill`: fill bounds by stretching.
- `Scale + Blur`: blur-fill mismatched backgrounds.

The scaling article is explicit that scaling applies within object bounds, not necessarily full slide bounds. That matters when media is used as a slide object or fill rather than as a full-screen background.

ChurchPresenter should keep these concepts separate:

- Import default policy.
- Cue-level foreground/background role.
- Object-level bounds and scaling.
- Screen/layer-level composition.
- Per-cue transition and playback behavior.

## Media Inspector and Cue Overrides

The Media Inspector applies to images, videos, audio, Media Bin items, Audio Bin items, and media items inside presentations. It exposes:

- Cue name, thumbnail, and file details.
- Alignment, scaling, cropping, rotation, playback behavior, retrigger behavior, in/out points, delay, play rate, and duration.
- Cue-specific transitions.
- Effects or effect presets.
- Audio volume and per-channel routing.
- Playback markers for actions such as macros, MIDI cues, stage layout changes, and stage timers.

Retrigger rules are especially important:

- Foreground media always retriggers.
- Background media set to stop retriggers.
- Background media set to loop does not retrigger under automatic behavior.

ChurchPresenter should represent cue defaults and cue overrides explicitly. A media item in the library is not the same as a media cue in a slide, playlist, macro, or theme; each cue may need local settings.

## Slide Actions and Media/Audio Playlists

ProPresenter can trigger Media Bin playlists and Audio Bin playlists from slide actions. The operator can drag a media playlist or audio playlist to a slide, add it before or behind a slide as a cue, or add it through the Action Palette.

Implementation implication: slide activation may emit multiple commands. A slide take can update the slide layer, change the media layer, trigger an audio playlist, start timers, change a stage layout, switch a Look, or run a macro. The output engine should consume normalized actions rather than hiding this behavior in the slide view.

## Live Video Inputs

Video inputs can come from USB capture devices, SDI devices, NDI sources, or Syphon on macOS. A configured input has a name, device/source, preview, optional manually selected mode, preview thumbnail, and optional linked audio source. The input appears in a Video Inputs playlist in the Media Bin.

ProPresenter can use video inputs in three ways:

- Trigger directly from the Video Input playlist.
- Add to a slide or macro as a Video Input Action.
- Add as a slide-layer element in the Slide Editor.

ChurchPresenter should model live video as an asset/cue type with device health, thumbnail/preview, optional audio source, and routing. It should not be a special case that bypasses the media/action model.

## Audio Routing

ProPresenter's audio engine routes device inputs into up to 16 internal audio channels, then routes those channels to primary audio output devices and to SDI/NDI feeds. Routing matrices can be customized, auto-mapped, cleared, muted, soloed, and tone-tested. Individual media actions can route their embedded audio channels into specific ProPresenter channels.

Audio output articles also distinguish:

- Main audio output device.
- Media Inspector audio output.
- Adjustable channel count.
- Per-output channel routing.
- SDI/NDI audio through Blackmagic SDK or NDI.
- The need to set Main output to `None` when only SDI/NDI audio is desired to avoid duplicated audio.

ChurchPresenter's first local media implementation can be simpler, but the domain should reserve:

- Internal audio channels.
- Per-cue audio routing and volume.
- Output-device routing.
- Media-layer transport state.
- Future SDI/NDI audio and delay.

## Implementation Invariants

- Media asset identity must survive path changes.
- A cue may override the asset's default playback, transition, scaling, thumbnail, in/out, effects, and audio routing.
- Foreground/background role affects retrigger and composition behavior.
- Media cleanup must traverse references from all content and automation surfaces.
- Audio transport and visual output routing are related but not the same system.
- Live video inputs should behave like media cues plus device state, not like ad hoc output windows.

## Open Coverage Questions

- Dedicated Audio Bin behavior beyond Show Controls and transport surfaces may need more current coverage before implementing a full audio-bin editor.
- Confirm whether ChurchPresenter needs ProContent-like online media acquisition or only local media management in the first parity scope.
