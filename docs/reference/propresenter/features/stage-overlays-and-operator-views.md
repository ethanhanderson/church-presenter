# Stage, Overlays, and Operator Views

ProPresenter gives the operator more than a slide grid. Stage displays, current/next text, timers, clocks, stage messages, live previews, operator multiviews, messages, props, clear groups, and Show Controls are live surfaces that help the team run and recover a production.

## Source Basis

- [Stage Display](https://renewedvision.com/propresenter/stage-display)
- [ProPresenter Remote](https://renewedvision.com/propresenter/remote)
- [Using a Stage Screen](https://support.renewedvision.com/hc/en-us/articles/360041407794-Using-a-Stage-Screen-to-its-Full-Potential)
- [Timers](https://support.renewedvision.com/hc/en-us/articles/360050782494-Setting-up-Timers-in-ProPresenter-7)
- [Audience Countdown](https://support.renewedvision.com/hc/en-us/articles/360050786794)
- [Stage Timers](https://support.renewedvision.com/hc/en-us/articles/360053250613)
- [Show Controls](https://support.renewedvision.com/hc/en-us/articles/4412263446035-What-are-Show-Controls)
- [Remote App Interface](https://support.renewedvision.com/hc/en-us/articles/43051104879379-ProPresenter-Remote-App-Interface)
- [Clear Groups](https://support.renewedvision.com/hc/en-us/articles/4408681880211-Using-Clear-Groups-in-ProPresenter)
- [ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6032278869011-Using-ProPresenter-Control)
- [Stage, announcements, and generated content](../output-system/stage-announcements-generated-content.md)
- [Operator workflows and automation](../operator-workflows/show-controls-and-recovery.md)
- [Layers and Looks](../output-system/layers-and-looks.md)

## Stage Display

Stage Display is a confidence-monitor system. It lets presenters, musicians, speakers, and stage crew see information tailored to them instead of the exact audience output.

Stage screens can show:

- Current slide text.
- Next slide text.
- Speaker notes.
- Chord/chart data.
- Timers and clocks.
- Video countdown.
- Live screen previews.
- Stage messages.
- Capture/status information.
- Custom layout elements.

ChurchPresenter should implement stage output as a dashboard compositor. It should receive live-state and generated-data providers, not a copy of the audience screen.

Current stage-layout elements include:

- Current slide text, current slide image, and current slide notes.
- Next slide text, next slide image, and next slide notes.
- Screen Preview objects for any configured Audience or Stage screen.
- Chord Chart data added to slides.
- Stage Display Message.
- Planning Center Live timer.
- Created clocks and timers.
- System clock.
- Video countdown timer.
- Group color/name objects for current or next slide.
- Capture status color or linked text.
- Shapes and text boxes for labeling and custom layouts.
- Fill sources such as color, gradient, media, web page, video input, slide object, current/next slide image, screen preview, chord chart, Planning Center Live timer, group color, or capture status color.

Architecture implication: a stage layout is a scene graph with runtime-linked fill/text providers, not a fixed confidence-monitor template.

## Flexible Layouts

Stage layouts can be changed manually, automatically by slide actions, from remotes, from macros, or through control surfaces. A layout assignment is per stage screen, and a slide action can choose stage-only behavior or resume audience+stage output.

Architecture implications:

- Stage layout id and current assignment should be live state.
- Layout changes should be commands targeted to one or more stage screens.
- Stage layout state should persist until replaced or manually changed.
- Stage-only advancement must not mutate audience output layers.

Current stage-layout workflow:

- Open Stage Layout Editor from `Screens > Edit Layouts`, `Command+4`, or `Control+4`.
- Layouts can be copied, pasted, duplicated, deleted, or renamed.
- New layouts can start from preloaded layouts or a blank layout.
- The large plus button adds layout objects.
- Text appearance editing mirrors presentation Edit Mode, including font, style, size, attributes, color, scaling, alignment, stroke, shadow, line/list settings, and linked text.
- Layouts can be assigned directly to a stage screen from the Stage Layout Editor's **Show** button.
- Layouts can also be changed by slide actions.
- Stage layout actions remain applied until manually changed or replaced by another stage layout action.

Architecture implication: stage layout assignment is durable live state, and the editor must be separate from the runtime compositor.

## Current and Next Text

Current/next text gives stage talent a readable view of where they are and what is coming next. It may be larger, higher contrast, or differently formatted than the audience output.

ChurchPresenter should derive current/next text from:

- Current live presentation/slide/arrangement occurrence.
- Playlist and group context.
- Stage-only advancement state.
- Disabled/skipped items and repeated groups.
- Notes and chord/chart metadata.

It should not scrape rendered audience pixels or depend on slide-card UI.

## Timers and Clocks

Stage layouts can show time of day, countdown timers, countdown-to-time, elapsed time, and video countdowns. The same timer state can also feed audience messages, props, linked text, playlist headers, and slide actions.

ChurchPresenter timer state should include:

- Timer kind.
- Name and configured duration/start/end.
- Running/paused/stopped state.
- Overrun behavior.
- Display formatting.
- Color triggers and visibility conditions.
- Source command or integration.

The renderer should consume timer state through generated text providers.

Current timer behavior:

- Timer controls open from Show Controls, View menu, or keyboard shortcuts.
- Timer types are **Countdown**, **Countdown to Time**, and **Elapsed Time**.
- Countdown uses hour:minute:second duration entry and adds leading zeros automatically.
- Countdown to Time counts down to a specific time of day with AM, PM, or 24-hour format.
- Elapsed Time counts up from a start time and can optionally stop at an end time.
- Overrun lets countdowns go negative or elapsed timers continue past the end time.
- Timers can be reset, started, stopped, collapsed, and run in unlimited count.
- Audience countdown requires three pieces: Timer for data, Theme for formatting, and Message or linked text for display.
- Message countdowns can use token values, theme selection, dismiss behavior, timer overrides, and message transitions.
- Linked Text can place timers in presentations or props and control hour/minute/second/millisecond formatting.
- Timers can start from Timers, Messages, playlist headers, slide actions, and props actions.
- Playlist headers can start, stop, reset, or update an existing timer and can even change the timer type.
- Stage timers add color triggers, system clock date/time options, 24-hour clock, video countdown, and visibility conditions such as showing video countdown only while video is active.

Architecture implication: timers are stateful runtime services with many render consumers and many command sources. They should not be implemented as text boxes with local countdown loops.

## Stage Messages

Stage messages are private communication to people on stage. They are distinct from audience messages and should only appear on stage layouts that include a stage-message element.

Implementation model:

- Stage message state with text, source, timestamp, and active/dismissed status.
- Targeting to all stage screens or specific stage screens where appropriate.
- Commands from local UI, ProPresenter Control-like surfaces, mobile remote, macros, or automation.
- No automatic audience-layer output.

Current stage-message behavior:

- Stage message text can be sent from stage controls, ProPresenter Control, and Remote.
- A stage message only appears on layouts that contain a text box linked to the Stage Display Message.
- ProPresenter Control exposes a text entry area for stage message.
- Remote stage-layout controls can show/hide a stage message to stage screens.

Architecture implication: stage message is shared stage-layer data with renderer opt-in, not a per-screen modal.

## Live Screen Previews and Operator Multiviews

The Stage Display page includes live screen previews and operator views. A multiview can show multiple inputs/outputs so operators can see what is coming in and out of the system.

ChurchPresenter should reuse resolved screen frames:

- Screen preview elements subscribe to selected output frames.
- Labels, borders, strokes, and layout metadata belong to the stage/multiview layout.
- Additional previews should report performance impact.
- Operator multiview should not create a second independent render path for each screen.

Current preview/multiview behavior:

- Stage layouts can include Screen Preview objects for any Audience or Stage screen.
- Screen Preview objects can be combined with labels, strokes, and custom layout sections.
- Capture status can appear as fill color or linked text.
- Live screen previews are also exposed as a public Stage Display feature.

Architecture implication: previews should subscribe to screen-frame snapshots and metadata rather than recursively rendering the whole output graph inside stage output.

## Messages

Messages are operator-triggered audience overlays, often used for nursery alerts, announcements, countdowns, or short notices. Audience countdown workflows combine timer state, a theme, and a message or linked text.

ChurchPresenter should model messages as generated overlay layer state:

- Message template.
- Runtime fields and token bindings.
- Theme/formatting selection.
- Transition and dismiss behavior.
- Target/routing through Looks.
- Source command and diagnostics.

Messages should be separate from stage messages and props.

Current message behavior from current timer/remote/control coverage:

- Messages live in Show Controls and can be selected from ProPresenter Control and Remote.
- Messages can contain tokens, including timer tokens.
- A message can select a theme and transition.
- Dismiss behavior can clear manually, remove at timer expiration, or remove after a configured time.
- Timer options can be overridden per message.
- ProPresenter Control lets users choose a message, fill available tokens, and show it.
- Remote lets users trigger prebuilt messages and configure a timer included in a message.

Architecture implication: message templates, runtime token values, generated text, theme selection, transition, and dismiss policy should be distinct fields.

## Props

Props are persistent audience overlays. They can be shown, hidden, triggered, and automated, and active props are visible on remote/control surfaces.

ChurchPresenter should model props as reusable overlay documents with live toggled state. A prop should be able to remain visible while slides advance, and clearing props should not clear slides or media unless a clear group includes those layers.

Current prop behavior from current remote/control coverage:

- Props live in Show Controls.
- Remote lets users tap a prop to show it and tap again to hide it.
- ProPresenter Control outlines active props in orange and lets users trigger/clear individual props.
- Clear Groups can clear the Props layer separately from slides, media, messages, announcements, audio, and video input.

Architecture implication: props need persistent active state, toggle commands, active indication, and independent clear behavior.

## Show Controls

ProPresenter's Show Controls area exposes live production panels:

- Audio Bin.
- Stage.
- Timers.
- Messages.
- Props.
- Macros.

These are operational surfaces, not hidden settings. ChurchPresenter's shell should keep non-slide live controls reachable during service operation. A production app cannot depend solely on a slide grid.

Current Show Controls behavior:

- Show Controls were introduced as a consolidated bottom-right live-control area.
- Props, Messages, Timers, Stage, Audio Bin, and Macros are available there.
- Icons can be rearranged per user preference by holding Command on macOS or Control on Windows and dragging.
- Remote and ProPresenter Control expose many of the same controls externally.

Architecture implication: Show Controls layout can be user preference, but the underlying command/query features need stable ids and routes.

## Preview and Clear Recovery

ProPresenter exposes per-layer clears and clear groups. Operators can clear audio, messages, props, announcements, slides, media, live video, or configured groups.

ChurchPresenter should implement:

- Single-layer clear commands.
- Named clear groups with icon, tint, label, and layer/playback membership.
- A default Clear All group that can be configured.
- Clear commands that do not mutate operator selection.
- Active/enabled states based on backend layer diagnostics.
- Remote/macro/device access through the same command ids.

Clear state should answer "what is live and why?" rather than only hiding pixels.

Current clear behavior:

- Remote clear options include Clear Audio, Clear Messages, Clear Props, Clear Announcements, Clear Slides, Clear Media, Clear Live Video, and Clear Groups.
- Clear Groups are configured from the clear actions area.
- A default **Clear All** group exists but can be modified, after which it may no longer truly clear everything.
- Group attributes include name, icon, custom icon option, and optional tint.
- Group members include Music, Audio Effects, Messages, Props, Announcements, Presentation, Presentation Media, and Video Input.
- Announcements and Presentation group entries can optionally stop timelines.
- Presentation Media excludes media added as a normal slide element in the editor.

Architecture implication: clear commands should target layer/playback scopes and optional timeline-stop behavior, not arbitrary UI buttons.

## Cross-Feature Interactions

Stage and operator views depend on presentations, arrangements, media playback, timers, capture status, screen frames, output layers, remotes, macros, and devices. Their architecture should be read-only where they display state and command-based where they change it. Avoid UI-only shortcuts that bypass the same state model used by output and remote clients.
