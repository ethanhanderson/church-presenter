# Automation, Control, and Remotes

ProPresenter's automation features let one operator coordinate slides, media, audio, stage layouts, timers, Looks, capture, external devices, and remote clients. The key architectural lesson is that every trigger surface should converge on one command and live-state model.

## Source Basis

- [Automations & Control](https://renewedvision.com/propresenter/automations)
- [ProPresenter Remote](https://renewedvision.com/propresenter/remote)
- [Stage Display](https://renewedvision.com/propresenter/stage-display)
- [Using Macros](https://support.renewedvision.com/hc/en-us/articles/4402663090323-Using-Macros-in-ProPresenter)
- [Playback Markers](https://support.renewedvision.com/hc/en-us/articles/7171761588371-How-to-use-Playback-Markers)
- [ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6032278869011-Using-ProPresenter-Control)
- [Connecting to ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6024791423763-Connecting-to-ProPresenter-Control)
- [Remote App Interface](https://support.renewedvision.com/hc/en-us/articles/43051104879379-ProPresenter-Remote-App-Interface)
- [Network Link](https://support.renewedvision.com/hc/en-us/articles/4412193892627-Network-Link)
- [TCP/IP Connections with ProPresenter API](https://support.renewedvision.com/hc/en-us/articles/31606866768147-TCP-IP-Connections-with-ProPresenter-API)
- [MIDI ProPresenter Setup](https://support.renewedvision.com/hc/en-us/articles/1500000020301-Devices-MIDI-ProPresenter-Setup)
- [MIDI System Setup](https://support.renewedvision.com/hc/en-us/articles/4412376777491-Devices-MIDI-System-Setup)
- [DMX Devices](https://support.renewedvision.com/hc/en-us/articles/360059980973-Devices-DMX)
- [Resi Streaming](https://support.renewedvision.com/hc/en-us/articles/360058942613-Setting-up-Resi-Streaming-in-ProPresenter)
- [Operator workflows and automation](../operator-workflows/show-controls-and-recovery.md)
- [Feature inventory](../feature-inventory.md)

## Shared Command Model

Automation should not be implemented separately for slides, macros, remotes, keyboard shortcuts, devices, and calendars. ChurchPresenter should define a shared command model:

- `LiveCommand`: requested operation with source, target, authorization, correlation id, and timestamp.
- `LiveAction`: a specific effect such as take slide, trigger media, clear layer, start timer, switch Look, change stage layout, start capture, or send device command.
- `ActionBatch`: ordered/parallel set of actions, commonly used by slides and macros.
- `ActionResult`: success/failure, partial completion, diagnostics, and visible state changes.
- `CommandSource`: local UI, remote, macro, slide action, timeline, playback marker, keyboard, device, API, calendar, or linked instance.

This lets every live surface use the same behavior and makes recovery diagnostics possible.

## Slide Actions

The Automations page describes attaching actions to slides to change inputs, clear media, or trigger macros. Existing user-guide/support-derived docs identify many possible action targets: media, audio, timers, messages, props, macros, Looks, stage layouts, captures, and communication devices.

Slide activation should:

- Resolve the selected presentation/slide/arrangement occurrence.
- Compile visible slide payload for the slide layer when applicable.
- Compile attached action definitions into an action batch.
- Execute the batch through the shared command executor.
- Report results and update live-state diagnostics.

Stage-only slide actions are especially important: they should advance stage content without changing audience layers.

## Macros

Macros are named groups of actions. They can be triggered manually, from slides, through devices, or on demand. They can have collections, icons, colors, grid/table presentation, and action lists.

ChurchPresenter should serialize macros as content/support configuration:

- Macro id, name, collection, icon, color, and display metadata.
- Ordered action definitions.
- Optional delay/conditional behavior.
- Last-run diagnostics.
- Permissions or allowed trigger surfaces for future remote/API support.

Macros should not call UI event handlers. They should submit action batches.

Current macro behavior:

- Macros live in Show Controls and are created from the `[M]` icon.
- A macro can be customized with name, color, icon, and actions.
- Actions are added by right-clicking the macro and choosing Add Action, or by dragging actions from the Action Palette.
- Macros can be edited or have actions removed from the context menu.
- A macro can be triggered manually from Show Controls.
- A macro can be added to a slide through **Add Action > Add Macro** or by dragging the macro onto the slide.
- When triggered, all assigned actions run.
- Communication Devices can trigger macros.
- Macro triggering through communication devices is index-based against the currently selected macro collection.
- Macro collections can be created, renamed, toggled, and used to organize macros.
- Macros support Grid View and Table View; Table View shows assigned actions.
- Macro icons can be changed from built-in choices or custom imported icons.

Architecture implication: macro collections, macro order, display metadata, and index-based external control behavior are all part of the product contract.

## Playback Markers

Playback markers trigger actions at specific media timeline positions. They are useful for firing macros, changing stage layouts, showing messages, or coordinating with external devices.

Architecture model:

- Media playback raises marker events with cue/player identity and playback time.
- Marker events become command sources.
- Marker execution follows normal action result reporting.
- Manual seeking, retrigger, loop, and stop behavior should define whether markers can fire again.

Current playback marker behavior:

- Playback Markers are available for video and audio files.
- Images and media used as slide elements are not supported.
- Markers are added in the Media Inspector from the Markers tab at the current preview playhead position.
- Marker rows expose icon/color, timecode, optional name, and action icons.
- Context menu options include rename, recolor, add/edit/remove actions, cut, copy, paste, duplicate, delete, and move to playhead.
- Actions can be dragged from Action Palette, Show Controls, media/audio sources, or blank marker-list areas.
- Active transport controls show marker icons and can jump to marker timecode.
- Jumping to a marker performs assigned marker actions if present.
- Transport time labels can toggle from elapsed/countdown to previous/next marker timing.
- Stage layouts can display time-to-marker or marker name through Playback Marker linked text.

Architecture implication: marker state needs to be available to transport UI, stage layouts, and command execution, not only the media inspector.

## Timeline

Timeline playback can schedule slides, media, actions, and macros with recorded or authored timing. A timeline is not a view timer; it is a time-based command source.

ChurchPresenter should support a timeline abstraction that can:

- Use an internal clock, media clock, or external clock/timecode source.
- Schedule command batches.
- Pause, resume, stop, and recover manually.
- Preserve operator override ability.
- Emit diagnostics for skipped, late, failed, or cancelled actions.

## Timecode

Timecode synchronizes ProPresenter with external timecode-enabled hardware or software. For architecture, timecode is an external clock/source that drives timeline positions or command triggers.

Reserve:

- Timecode source configuration.
- Lock/loss state.
- Offset and frame-rate metadata.
- Command scheduling from timecode positions.
- Operator-visible health.

## Calendar Scheduling

The Automations page describes scheduling announcements, looping, streaming, and similar needs in advance. Scheduling should produce commands at known times instead of hidden UI automation.

ChurchPresenter should model scheduled tasks as:

- Schedule id, recurrence/time window, and enabled state.
- Target command batch.
- Preconditions such as content availability and output configuration.
- Run history and failure diagnostics.

## Device Integrations

### Stream Deck

Stream Deck can trigger content, macros, messages, capture, previous/next slide, and other commands. A Stream Deck adapter should translate button events into typed commands and receive state feedback for labels/icons where supported.

### MIDI and DMX

MIDI/DMX can synchronize production with audio and lighting consoles. Commands may include slide changes, video playback, media triggering, or custom action batches.

Device adapters should be protocol-specific at the edge and product-command-specific inside the app.

Current MIDI behavior:

- MIDI setup has system-level and ProPresenter-level steps.
- On macOS, network MIDI uses Audio MIDI Setup; same-machine routing can use the IAC Driver.
- On Windows, network MIDI requires third-party rtpMIDI, and same-machine routing can use loopMIDI.
- In ProPresenter Settings > Devices, a MIDI device can define sources for incoming notes and destinations for outgoing notes.
- Auto Reconnect can reconnect MIDI devices.
- ProPresenter listens on all channels when receiving MIDI.
- When sending MIDI, a specific channel can be chosen.
- MIDI Map auto-fill can generate note assignments from a start note range 0-99, because MIDI notes run 0-127.
- Some commands use note plus intensity to select indexed items such as playlists, playlist items, and props.
- ProPresenter can send MIDI Note On and MIDI Note Off actions from slides.
- Multiple MIDI notes can be sent from the same slide.
- Multiple MIDI devices are supported on macOS for sending.

Current DMX behavior:

- ProPresenter can be controlled by DMX or Art-Net like a lighting fixture; sACN is not currently supported.
- DMX devices are configured in Settings > Devices with hardware type, network adapter, address, Art-Net settings, and auto reconnect.
- A DMX Map lists channels and values.
- Base Address can be 1 through 499.
- DMX can select library playlists, media playlists, audio playlists, trigger cues, trigger media cues, trigger macros, select macro collections, trigger audio cues, set global transitions/duration, start/stop/reset timers, clear layers/groups, run transport commands, and start/stop presentation timelines.
- Playlist folders are not directly addressable by DMX index; selection is sequential across library/media/audio structures.
- DMX Set Transition affects the orange global transitions, not Media Bin purple transitions or document transitions.

Architecture implication: MIDI/DMX integrations are index-heavy and state/order-sensitive. ChurchPresenter should expose both stable-id commands for modern APIs and optional index maps for console-style protocols.

## API and ProPresenter Control

The public Automations page includes API support, and ProPresenter Control is a web control surface for a local instance. Control surfaces can inspect or operate Looks, macros, audio, capture/streaming, graphics output toggles, playlists, timers, messages, transport controls, stage layouts/messages, props, and connected instance identity.

ChurchPresenter should design command and query APIs early:

- Explicit read models for live session state.
- Authorization/permission decisions per command surface.
- Source metadata in command logs.
- Idempotent or conflict-aware commands where remote/network use is likely.
- Clear errors for unavailable output endpoints, missing media, denied permissions, or stale state.

Current ProPresenter Control behavior:

- Requires ProPresenter 7.9.1 or later on the same local network.
- Network must be enabled in the Network tab, exposing IP address and port.
- Control can be reached at `control.propresenter.com` or offline from the View menu.
- A hardwired network is recommended.
- Control surfaces include Looks, Macros, Audio Transport, Audio Playlists, System Time, Capture and Streaming, Screen Control, Library Playlist view, Timers, Messages, Announcement Layer transport, Presentation Layer transport, Stage Screen Controls, Stage Message, Props, and connected instance identity.
- Library Playlist is view-only: it shows playlist contents and active item but cannot select items.
- Screen Control toggles graphics outputs assigned to audience/stage screens, but not SDI, Syphon, or NDI outputs.
- Capture/Streaming can start/stop the configured capture and view elapsed time/status.
- Props show active state with orange outline.

Current TCP/IP API behavior:

- TCP/IP API provides a simple-socket alternative to the HTTP API for remote systems without HTTP capability.
- Requests and responses are single-line serialized JSON objects terminated by CRLF.
- Request members include mandatory `url`, optional `method`, optional `body`, and optional `chunked`.
- `chunked` streams updates from supported endpoints.
- Responses include `url`, `data` on success, or `error` on failure; 204 responses send no data.
- Example endpoint coverage includes stage message and system time streaming.

Architecture implication: local command/query contracts should be transport-agnostic enough to support HTTP, TCP line protocol, local UI, and future mobile clients.

## Network Link

Network Link coordinates multiple local computers for extra outputs, alternate content, or redundancy. Even if this is future scope, the architecture should not block it.

Reserve concepts:

- Instance identity and role.
- Shared command stream or replicated command batches.
- Content/version compatibility checks.
- Health and heartbeat.
- Leadership/failover decisions.
- Conflict handling when multiple operators can send commands.

Current Network Link behavior:

- Network Link controls multiple ProPresenter computers in a network group.
- There is no master computer; any computer in the group can assume control.
- Computers must have Network and Network Link enabled and must communicate on the same network.
- Connections can be discovered or manually entered with IP and port.
- Multiple groups can exist on the same network.
- On macOS, Local Network permission must allow ProPresenter.
- Triggering is index-based, not id-based.
- Library, library playlist, slide content, media playlists, media playlist content, Media Bin content, and Audio Bin content are triggered by matching indexes across computers.
- Go To Next timers do not trigger linked content when the timer advances a slide.
- Show Controls can adjust timer types/values, message token values/content, and trigger props, macros, and stage layout changes by index.
- Reordering Show Control items breaks index alignment unless order is matched again.

Architecture implication: multi-machine linking needs content/index parity checks and clear operator warnings. A stable-id replication model would be safer for ChurchPresenter, but index compatibility may matter for ProPresenter-like external control parity.

## ProPresenter Remote

The Remote page describes phone/tablet workflows:

- Advance slides and clear slide layers.
- Trigger timers.
- View and trigger macros.
- Fire props.
- Adjust and trigger audio playlists.
- Trigger/customize messages.
- Access libraries, playlists, and collections.
- Use stage mode with current/next content, notes, and timers.
- Use custom remote views.

ChurchPresenter remote readiness means:

- Stable query models for libraries, playlists, presentations, slide notes, live current/next, timers, props, messages, macros, audio playlists, and stage state.
- Commands that can run from local and remote sources without diverging behavior.
- Permission scopes for volunteer, presenter, producer, and admin-style roles.
- Latency and stale-state handling.
- Diagnostic attribution when a remote action changes live output.

Current Remote behavior:

- The current ProPresenter Remote app requires ProPresenter version 20 or later.
- It is available on iOS and Android and can be installed on Apple Silicon Macs via iPhone/iPad app filtering, but not Intel Macs.
- Launch opens to the Presentation tab by default.
- Library view browses all presentations in ProPresenter libraries and can trigger slides.
- Playlist view expands library playlists, opens items, and triggers slides or standalone media play buttons.
- The eraser icon opens clear options for audio, messages, props, announcements, slides, media, live video, and clear groups.
- Clear Groups are shown by tapping the layers icon in the clear menu.
- Three-dot menu switches Grid/List view, toggles Follow Presentation, and adjusts slide size.
- Bottom tabs are customizable by editing More-tab items and dragging orange items to tab positions.
- Timers can start, stop, reset, and be configured for type, duration, and overrun.
- Messages can be triggered, and messages with timers can configure that timer.
- Props toggle on/off by tapping.
- Audio Bin can trigger and control audio playback.
- Stage Screen Layouts can change active layout per stage screen and show/hide stage message.
- Looks can change the active audience Look.
- Macros can trigger prebuilt macros.
- Remote tab shows current active slide and next slide; tapping the next slide advances.

Architecture implication: remote clients need both browse/read models and command models. Follow Presentation, custom tabs, slide sizing, and stage-mode views are client preferences over shared live state.

## Cross-Feature Interactions

Automation touches every subsystem. A macro may change a Look, fire media, start an audio playlist, send a stage message, start a timer, and trigger capture. A remote may call the same macro. A slide action may call it too. The architecture goal is not to predict every automation combination; it is to ensure every feature exposes commandable actions and observable state.
