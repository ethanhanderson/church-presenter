# Media, Audio, and Live Inputs

ProPresenter treats media and audio as live production cue systems. Images, videos, audio tracks, sound effects, video inputs, audio inputs, Media Bin playlists, Audio Bin playlists, slide-attached actions, and cue inspector settings all interact with output layers and transport controls.

## Source Basis

- [Media & Slides](https://renewedvision.com/propresenter/media-and-slides)
- [Multi-screen management](https://renewedvision.com/propresenter/multi-screen-management)
- [Automations & Control](https://renewedvision.com/propresenter/automations)
- [Media Management](https://support.renewedvision.com/hc/en-us/articles/360041815133-ProPresenter-Media-Management)
- [Media Inspector](https://support.renewedvision.com/hc/en-us/articles/7200487649299-What-is-the-Media-Inspector)
- [Media Scaling Options](https://support.renewedvision.com/hc/en-us/articles/360011694174-Media-Scaling-Options)
- [Media Import Presets](https://support.renewedvision.com/hc/en-us/articles/34512245702035-Media-Import-Presets-in-ProPresenter)
- [Video Input](https://support.renewedvision.com/hc/en-us/articles/360053482973-Setting-Up-A-Video-Input-in-ProPresenter)
- [Audio Input](https://support.renewedvision.com/hc/en-us/articles/360053484013-Setting-Up-An-Audio-Input-in-ProPresenter)
- [Audio Routing](https://support.renewedvision.com/hc/en-us/articles/360052696094-Audio-Routing-in-ProPresenter)
- [Audio Outputs](https://support.renewedvision.com/hc/en-us/articles/360052697694-Audio-Outputs-in-ProPresenter)
- [SDI/NDI Audio Outputs](https://support.renewedvision.com/hc/en-us/articles/360053282613-SDI-NDI-Audio-Outputs)
- [Playback Markers](https://support.renewedvision.com/hc/en-us/articles/7171761588371-How-to-use-Playback-Markers)
- [Understanding the ProPresenter 7 User Interface](https://support.renewedvision.com/hc/en-us/articles/360041345954-Understanding-The-ProPresenter7-User-Interface)
- [ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6032278869011-Using-ProPresenter-Control)
- [Triggering Media and Audio Playlists from Slide Actions](https://support.renewedvision.com/hc/en-us/articles/360056439374-Triggering-Media-and-Audio-Playlists-from-Slide-Actions)
- [Media and audio management](../media-management/media-audio-assets.md)
- [Layers and Looks](../output-system/layers-and-looks.md)

## Media Assets and Cues

An asset is a file/source. A cue is a use of that asset in a live or authored context. The same image, video, audio file, or input can be used by multiple cues with different playback, scaling, transition, effect, thumbnail, routing, and retrigger settings.

ChurchPresenter should maintain separate contracts for:

- `MediaAsset`: identity, original source path/provider, resolved path/cache, media type, metadata, missing state, and storage policy.
- `MediaCue`: use of an asset with role, overrides, transition, timing, and target layer.
- `AudioCue`: audio-specific use of an asset with track/effect type, volume, routing, and transport behavior.
- `LiveVideoInput`: device/network/source configuration plus cue-like triggering.
- `MediaPlaylist` and `AudioPlaylist`: ordered cue collections for live playback.

## Media Bin

The Media Bin is a live media cue library, not a directory browser. It can contain images, videos, folders, playlists, video inputs, thumbnails, grid/list views, filtering, cue-size controls, and a media default transition. Operators can trigger Media Bin cues directly or attach them to slides through actions.

ChurchPresenter implications:

- Media Bin items should be cues with metadata, not raw files.
- Direct triggering and slide-triggered media should use the same command path.
- Media playlists should be independent from service playlists.
- Active media state should be observable by layer, cue, asset, playback state, transition, and owner/source command.

## Audio Bin

The Audio Bin is a separate live cue/playlist system. ProPresenter differentiates longer audio tracks from sound effects and exposes transport controls for active audio. Audio playlists can be triggered manually or through slide actions.

ChurchPresenter should reserve:

- Audio cue type: track, sound effect, playlist, or future input.
- Active audio player state independent from visual slide selection.
- Playlist playback mode, next/loop behavior, and stop behavior.
- Transport controls reachable from local UI, remote clients, macros, and devices.

## Transport Surfaces

Current ProPresenter support material separates transport control by playback lane. The main preview transport can show presentation/media-layer playback and can be toggled to announcement and audio layers. ProPresenter Control exposes the same distinction as separate audio transport, announcement-layer transport, and presentation-layer transport surfaces.

ChurchPresenter should model these as separate transport snapshots rather than one blended active-player list:

- Media files: media-layer playback from Media Bin/direct cues and normal slide media actions.
- Audio files: audio-layer playback from Audio Bin/audio cues, independent from visual media and slide selection.
- Announcements: media playback associated with the announcement layer only.

Each snapshot should carry cue name, active player count, playing state, position, duration, seek state, and enabled/empty state. Switching the visible transport target should select which snapshot and players the controls operate on; it should not change Looks, clear layers, retarget content, or let announcement playback overwrite normal media-layer transport state.

## Foreground and Background Roles

Media can act as foreground content, background content, or a slide object. This affects composition and retrigger behavior:

- Foreground media appears above background content and generally retriggers when fired.
- Background media may continue looping and may not retrigger automatically.
- Slide media objects belong to the slide scene and follow slide visibility/build behavior.
- Background media can be hidden by slide/presentation background colors depending on layer ordering and settings.

The role should be explicit on the cue. It should not be inferred only from import source or file extension.

Current import preset behavior:

- Import presets live in ProPresenter Settings under the Import tab.
- Scaling has separate foreground and background defaults: Scale to Fit, Scale to Fill, Stretch to Fill, and Scale + Blur.
- Images can import as foreground or background with duration set to none, a specific time, or random duration.
- Videos can import as foreground or background and stop or loop.
- Audio can stop, loop, or play the next track automatically.
- Default scaling behavior is background media as Stretch to Fill and foreground media as Scale to Fit.

Architecture implication: import defaults are policy for new cues. They should not be conflated with cue-level overrides already stored on existing media actions.

## Cue Inspector Overrides

Cue inspectors allow per-cue behavior that can differ from the source asset:

- Cue name, thumbnail, and file details.
- Alignment, scaling, crop, rotation, and object bounds.
- In/out points, delay, play rate, duration, and loop/stop behavior.
- Cue-specific transitions and effects.
- Volume and per-channel routing.
- Playback markers that trigger actions or macros.
- Retrigger behavior and foreground/background semantics.

ChurchPresenter should keep asset defaults and cue overrides separate. Editing one cue should not mutate every use of the underlying media unless the operator edits the asset default intentionally.

Current Media Inspector behavior:

- Open by right-clicking media in the Media Bin, Audio Bin, or a presentation and choosing **Inspector**.
- Supports images, videos, and audio; tabs appear only when the inspected item supports them.
- Multiple media items can be inspected through a dropdown.
- Video/audio preview includes elapsed/remaining time, playback markers, and transport controls.
- Information tab includes media action name, thumbnail, custom thumbnail, and cue details.
- Properties tab includes alignment, scaling, cropping, rotation, playback behavior, retrigger behavior, end behavior, in/out points, delay, play rate, and custom duration.
- Transition tab selects a transition for a video or image cue.
- Effects tab selects an effect or effect preset for video/image cues.
- Audio tab adjusts per-channel volume and routing.
- Playback Markers tab adds marker-triggered actions.
- Automatic retrigger rules are role-sensitive: background media set to stop retriggers, background media set to loop does not retrigger, and foreground media always retriggers.

Current scaling behavior:

- **Scale to Fit** preserves all content within object bounds and may add bars.
- **Scale to Fill** preserves aspect ratio and crops.
- **Stretch to Fill** fills bounds by stretching.
- **Scale + Blur** creates a blurred background fill for mismatched resolutions and requires an active Pro+ agreement on supported versions.
- Scaling is applied to object bounds, not necessarily the full slide.
- Media Inspector previews may not show live scale updates even though Media Bin thumbnails update.

Architecture implication: cue preview, thumbnail, output rendering, and editor rendering may not be identical views, but they must all resolve from the same cue settings.

## Media Management

ProPresenter can reference media at original paths or copy media into managed app storage. Missing media may show as unavailable, and configured search paths can help relink missing assets. Cleanup removes unlinked media only after traversing a broad reference graph.

ChurchPresenter should model:

- Storage policy: referenced, managed, package-imported, remote-cached, or missing.
- Original source path and current resolved path.
- Search roots and relink results.
- Operator-visible missing-media diagnostics.
- Cleanup previews that include presentations, slide objects, slide actions, media/audio playlists, themes, props, messages, macros, masks, packages, and external links.

Media cleanup must not be implemented as "delete files not listed in the current folder." It needs the full content and action graph.

Current media-management behavior:

- By default, media import can store a reference to the original file path rather than copying the file.
- If the original device/folder/file disappears, thumbnails can gray out or show question marks and output playback fails.
- **Manage Media Automatically** copies imported files into ProPresenter's Media Assets folder under Support Files and references that copy.
- **Media Search Paths** can automatically relink missing media from alternate folders.
- **Clean Up Unlinked Media** removes media not referenced anywhere in ProPresenter, including presentations/slides, playlists, themes, props, macros, and masks.
- Cleanup moves files to Trash and is not undoable from ProPresenter, though the OS Trash may still contain the files.

Architecture implication: media resolution should be a service with audit/relink/cleanup operations. Output rendering should receive resolved assets and diagnostics, not perform file searching.

## Images and Videos

The feature pages stress broad image/video format support and use as foreground or background. For architecture:

- Decode and thumbnail generation should be service/adapter concerns, not embedded in slide cards.
- Playback lifecycle should belong to output/preview host adapters with diagnostics.
- The document model should preserve the intended scaling and role even if a platform media backend differs.
- Transparent video/image content should preserve alpha where possible for future key/fill and lower-third outputs.

## Video Inputs

Video inputs can come from USB capture, SDI hardware, NDI sources, Syphon on macOS, or other platform-specific input paths. ProPresenter lets operators configure inputs, preview thumbnails, link audio, trigger them directly from a video-input playlist, attach them to slides/macros, or place them as slide editor elements.

ChurchPresenter should treat video inputs as source-backed cues:

- Input source identity and type.
- Friendly name and preview thumbnail.
- Optional format/mode selection.
- Optional linked audio input.
- Device/network health and diagnostics.
- Routing as media/live-video layer content or as a slide scene node.

The output engine should not have one-off code paths for camera feeds. They should enter through the same action and cue system as other media.

Current video-input behavior:

- Configure video inputs in Settings > Inputs.
- Supported source categories include USB capture devices, SDI interfaces, NDI sources, and Syphon on macOS.
- A video input has a name, selected device/source, live preview, optional manual mode, and preview toggle.
- The thumbnail shown in the Media Bin can be captured from the live preview or loaded from a custom image.
- Embedded audio can be selected automatically, another audio input can be selected manually, or audio can be disabled.
- Audio settings include delay in milliseconds, master volume, channel levels, and routing.
- Configured video inputs appear in the **Video Input Playlist** in the Media Bin.
- Inputs can be triggered directly, added to a slide or macro as a Video Input Action, or added as a Video Input element in the Slide Editor.
- By default, configured audio plays whenever the video input is active on an Audience screen.

Architecture implication: video input state includes both configuration and live device health. Slide scene use and live layer triggering should share the same source identity.

## Audio Inputs and Routing

ProPresenter's audio system includes internal channels, input routing, output routing, per-media routing, main output device, SDI/NDI audio output, delay, volume, mute/solo, and test tones. Audio can be embedded in video/media cues or linked to inputs.

ChurchPresenter's first implementation can be simpler, but the domain should reserve:

- Internal channel identifiers.
- Per-cue channel mapping and volume.
- Output-device routing.
- Delay and mute/solo diagnostics.
- Separate handling for local speaker output, SDI audio, NDI audio, and capture/stream audio.
- Warnings for duplicate audio paths, such as local main output plus SDI/NDI when only one is desired.

Current audio-input and routing behavior:

- Audio inputs are configured in Settings > Inputs and can use audio-only interfaces or audio embedded in video inputs.
- Audio input modes are **Off**, **On**, **Auto Off**, and **Auto On**.
- **On** starts the input when the app launches.
- **Auto Off** keeps the input on except when foreground video with an audio track is triggered.
- **Auto On** starts the input when a linked video input is triggered to an Audience screen.
- Input mode is saved between restarts and applies immediately on launch.
- Inputs can set source, delay, master volume, per-channel volume, routing, and monitor output.
- SDI inputs expose 16 channels; NDI audio is limited to 8 channels.
- Audio transition duration can be set from 0 to 10 seconds.
- ProPresenter routes external/audio media through up to 16 internal audio channels.
- Output routing maps ProPresenter channels to output-device channels with mute, solo, tone, auto-map, and clear controls.
- Media Inspector audio routing maps a media file's channels into ProPresenter channels.
- Audio Outputs settings expose channel count, Inspector output, Main output, and SDI/NDI audio output with independent routing and volume.
- If SDI/NDI is the only desired audio output, Main output should be set to `None` to avoid duplicate audio.

Architecture implication: audio should have a routing graph and player/transport model independent from visible slide/media composition.

## Playback Markers

Playback markers connect media/audio timeline positions to automation. They can trigger macros, MIDI-like commands, stage layout changes, messages, or timer operations.

Architecture requirement:

- Media playback services should raise marker events.
- Marker events should call the same command executor as slide actions and macros.
- Marker execution should be observable in diagnostics so operators can understand why a macro or stage change happened.

Current marker behavior:

- Markers are available for video and audio media, not images or media used as slide elements.
- Marker rows include timecode, color/icon, optional name, and assigned action icons.
- Actions can be dragged from the Action Palette, Show Controls, or media/audio sources.
- Markers can trigger macros, MIDI cues, stage screen layout changes, and other actions.
- Transport controls can show markers, jump to them, and toggle elapsed/countdown display to previous/next marker.
- Stage layouts can show time to marker or marker name through Playback Marker linked text.

Architecture implication: media player state should expose marker metadata to transport UI, stage layouts, and command execution.

## Cross-Feature Interactions

Media and audio touch output layers, slide scenes, Looks, transport controls, clear groups, stage video countdowns, remote controls, macros, packages, and cleanup. The safest model is to make every media/audio trigger a typed cue action with observable playback state and explicit output ownership.
