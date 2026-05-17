# Operator Workflows and Automation

This document captures ProPresenter behavior that matters while a service is live: clear/recovery controls, timers, messages, props, macros, stage layouts, slide actions, remote control, and capture/streaming status.

Primary sources: [Understanding the ProPresenter User Interface](https://support.renewedvision.com/hc/en-us/articles/360041345954-Understanding-The-ProPresenter-User-Interface), [What are Show Controls?](https://support.renewedvision.com/hc/en-us/articles/4412263446035-What-are-Show-Controls), [Using a Stage Screen to its Full Potential](https://support.renewedvision.com/hc/en-us/articles/360041407794-Using-a-Stage-Screen-to-its-Full-Potential), [Setting up Timers](https://support.renewedvision.com/hc/en-us/articles/360050782494-Setting-up-Timers-in-ProPresenter-7), [Audience Countdown](https://support.renewedvision.com/hc/en-us/articles/360050786794), [Stage Timers](https://support.renewedvision.com/hc/en-us/articles/360053250613), [Planning Center Live Timers](https://support.renewedvision.com/hc/en-us/articles/1500006143281-Planning-Center-Live-and-Planning-Center-Live-Stage-Timers), [Using Macros](https://support.renewedvision.com/hc/en-us/articles/4402663090323-Using-Macros-in-ProPresenter), [ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6032278869011-Using-ProPresenter-Control), [Remote App Interface](https://support.renewedvision.com/hc/en-us/articles/43051104879379-ProPresenter-Remote-App-Interface), [Communication Devices](https://support.renewedvision.com/hc/en-us/articles/360058141114-Communication-Devices-in-ProPresenter-7), [Recording Screens](https://support.renewedvision.com/hc/en-us/articles/360049227893-Recording-ProPresenter-Screens), and [RTMP Streaming](https://support.renewedvision.com/hc/en-us/articles/360049229013-Streaming-a-ProPresenter-Screen-to-An-RTMP-Server).

## Show Controls

ProPresenter's Show Controls area groups live operational surfaces:

- Audio Bin.
- Stage Screens.
- Timers.
- Messages.
- Props.
- Macros.

These tools are not secondary settings pages. They are live-control panels the operator may need during a service, sometimes while slides and media continue running.

ChurchPresenter implication: the operator UI should reserve stable access to non-slide controls. A slide grid alone cannot run a ProPresenter-like service.

## Preview, Clear Buttons, and Recovery

The UI and remote articles expose clear controls per layer:

- Audio.
- Messages.
- Props.
- Announcements.
- Slides.
- Media.
- Live Video.
- Clear Groups.

The remote app presents clear options behind an eraser icon and includes Clear Groups through a layers icon. The output docs also note that clear groups can clear multiple configured layers.

Implementation implications:

- Clearing one layer must not imply clearing all live output.
- "Clear all" should be a configurable group, not the only recovery tool.
- The UI should identify which layer is producing visible output.
- Recovery workflows should be designed alongside take-live workflows.
- The same clear command should be available to UI, keyboard shortcuts, macros, remote clients, and future devices.

ChurchPresenter implementation notes:

- The Show output panel may keep primary **Slide** and **Media** clear buttons because those are the most common live-service recoveries.
- Additional clear buttons should be generated from configured clear groups or exposed through a clear-groups flyout, not added as unrelated page commands.
- Clear buttons should show icon, label, optional tint, and active/enabled state based on backend layer diagnostics.
- A button press should dispatch one engine command: explicit layer clears for built-in single-layer shortcuts, or a stable clear-group id for custom groups.
- Slide, media, announcement, message, prop, audio, audio-effect, and live-video clears should remain independently addressable so a future macro, remote client, or keyboard shortcut can call the same action.
- Clearing output content should not clear operator selection. Selection is page context; live output state is engine state.

The current implementation direction already has backend concepts for fixed layers, named clear groups, and clear commands. The remaining product work is to connect the Show output-panel UI and Settings clear-group editor to that shared model rather than keeping the slide/media toolbar as the only clear surface.

## Timers and Clocks

ProPresenter timer types include:

- Countdown.
- Countdown to Time.
- Elapsed Time.

Timers can have names, duration/start/end settings, overrun behavior, start/stop/reset controls, and no fixed count limit. Overrun can continue past zero or past an elapsed end time and can drive color changes.

Audience countdowns require three concepts:

- Timer: generates the time data.
- Theme: formats the text.
- Message or linked text: delivers the timer to an audience screen, presentation, or prop.

Timers can be started/configured by:

- Timer controls.
- Message controls.
- Playlist headers.
- Slide actions.
- Props actions, when the timer appears in a prop.

Stage timers add:

- System clock with date and 24-hour options.
- Video countdown that runs when video is playing.
- Color triggers.
- Visibility conditions such as showing a video countdown only while active.

Planning Center Live timers are separate integration-backed stage objects. They are selected from a Planning Center service and support Full Item Length, End Item on Time, and End Service on Time modes. Renewed Vision notes that Planning Center Live advancement is not the same as advancing the ProPresenter playlist.

ChurchPresenter should model timers as generated state, not as rendered text only. Timer state must be consumable by audience messages, stage layouts, props, slide actions, headers, and future integrations.

## Messages, Props, and Stage Messages

Messages are operator-triggered audience overlays. The audience countdown article shows messages can contain tokens, select a theme, select dismiss behavior, override timer options, and use a message transition.

Props are persistent audience overlays that can be shown/hidden from Show Controls and remote surfaces. The remote article says tapping a prop shows it and tapping again hides it, with active props visually indicated.

Stage messages are separate. ProPresenter Control and the stage article describe a stage message that displays on stage layouts containing a linked stage-message text box. Stage messages should not automatically become audience messages.

ChurchPresenter should keep these output channels distinct:

- Audience message layer.
- Prop layer.
- Stage message content consumed by stage layouts.
- Timer/generated text tokens used by any of the above.

## Stage Layout Operations

Stage layouts are assigned per stage screen. They can be changed in the stage editor, Show Controls, slide actions, ProPresenter Control, and the remote app.

Stage slide actions can specify layouts per configured stage screen and choose delivery behavior:

- `Stage Only`: advancing these slides updates stage screens while the last audience output stays unchanged.
- `Stage + Audience`: resumes sending content to audience and stage.

The stage article notes that stage layout actions persist until manually changed or replaced by another slide's stage layout action.

Implementation implication: stage state must be independent from audience layer state. A stage-only cue should be a first-class command that can advance confidence content without touching audience output.

## Macros and Action Surfaces

Macros are named groups of actions. They can be:

- Triggered manually from Show Controls.
- Added to slides.
- Triggered through communication devices.
- Organized into collections.
- Customized with name, color, icon, grid/table views, and action list.

When a macro triggers, all assigned actions run. The Macro article describes adding actions from the Action Palette, editing/removing actions, and using collections. Communication devices on Windows currently expose MIDI as the available device type.

ChurchPresenter should define one action model shared by:

- Slide actions.
- Macro actions.
- Playback markers.
- Playlist headers.
- Keyboard shortcuts.
- Remote clients.
- Future communication devices.

Without this shared model, the same feature will be implemented repeatedly in incompatible ways.

## Remote and Web Control Surfaces

ProPresenter Control exposes a web control surface for a connected ProPresenter instance on the same local network. It can control or inspect:

- Active Look.
- Macros.
- Audio transport and audio playlists.
- System time.
- Capture/streaming start, stop, elapsed time, and status.
- Graphics screen toggles for audience/stage system outputs, but not SDI/Syphon/NDI.
- Library playlist state, mostly view-only.
- Timers.
- Messages.
- Announcement and presentation layer media transport.
- Stage screen layouts and stage message.
- Props.
- Connected instance identity.

Transport controls should preserve the same lane distinction in the local operator UI. The media controls card should expose three selectable states: **Media Files**, **Audio Files**, and **Announcements**. Media Files targets presentation/media-layer playback from direct Media Bin cues and normal slide media actions. Audio Files targets Audio Bin/audio cue playback. Announcements targets announcement-layer media playback only.

Changing the selected transport state should swap the active-player snapshot used for cue name, player count, play/pause state, seek position, duration, and empty/disabled state. It should not change Looks, clear any layer, retarget content, or allow announcement-layer playback to replace media-layer transport state. Clear buttons remain independent recovery commands for their configured layers and clear groups.

The remote app exposes presentation and playlist browsing, slide triggering, clear options, timers, messages, props, Audio Bin, stage layouts, Looks, macros, settings, current/next slide, and a presenter-oriented remote tab.

ChurchPresenter does not need to ship remote control immediately, but the command surface should be designed so remote clients can call the same engine operations as the local UI.

## Capture, Recording, and Streaming

ProPresenter's capture feature records or streams one configured audience or stage screen at a time.

Recording settings include:

- Source screen.
- Destination: disk file or RTMP stream.
- Save location.
- Codec.
- Resolution.
- Frame rate.

RTMP streaming uses capture settings and supports presets. The RTMP article describes a live indicator:

- Green: connection is streaming without interruptions.
- Yellow: stream is dropping frames.
- Red: network connection lost or stream stopped for another reason.

Capture can start from the Capture Settings window or the Screens menu, and stop from the preview indicator, Screens menu, or Capture Settings window.

ChurchPresenter implication: capture/streaming is an output consumer with health state. It should subscribe to a configured logical screen, not duplicate the rendering pipeline.

## Multi-Screen Service Flow

A ProPresenter-like service may have:

- Main room audience screen using slide and media layers.
- Stream screen using the same slide content with an alternate lower-third theme.
- Lobby screen using announcement layer.
- Stage screen showing current/next text, notes, timer, and preview.
- Media Bin content triggered independently from slides.
- Audio Bin playlist running under the service.
- Timers started by headers or slide actions.
- Macros switching Looks, stage layouts, media, or timers.

ChurchPresenter should make the active live state observable:

- Which playlist item/presentation/slide is selected.
- Which layers are live.
- Which Look is active.
- Which stage layout is active per stage screen.
- Which media/audio players are active and controllable.
- Which screens/endpoints/capture sessions are healthy.

## Implementation Invariants

- Every live command should flow through an engine-level action/command surface.
- Stage-only advancement must not mutate audience output.
- Timer data should be shared generated state, with separate renderers for messages, props, slides, and stage layouts.
- Remote control, macro, keyboard, and local-click behavior should converge on the same commands.
- Capture and streaming should report health at the operator surface.

## Open Coverage Questions

- Current ProPresenter 7/20 dedicated articles for Props and Messages remain sparse; support/UI/remote/timer articles cover behavior, but the official user guide may be needed before implementation.
- Keyboard shortcut and communication-device command matrices need more coverage if ChurchPresenter plans a public automation API.
- Capture beyond one-screen-at-a-time and Resi-specific streaming need more coverage only if ChurchPresenter plans built-in streaming.
