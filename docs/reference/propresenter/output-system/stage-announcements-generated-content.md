# Stage, Announcements, and Generated Content

ProPresenter separates several "not just the current slide" output channels. These are important because they let one operator run the main show while other screens continue to show supporting content.

## Stage Screens

Stage screens are confidence or operator displays. They are configured as screens, but their content comes from stage layouts instead of audience Looks.

Stage layouts can include:

- Current slide text, image, notes, or preview.
- Next slide text, image, notes, or preview.
- Screen preview of any audience or stage screen.
- Chord charts.
- Stage display messages.
- Planning Center Live timer.
- Clocks and timers.
- System clock.
- Video countdown timer.
- Current/next group color and group name.
- Capture status.
- Custom shapes and text boxes.

This makes stage display more like a dashboard compositor than a duplicate audience output.

## Stage Layout Assignment

Stage layouts can be assigned directly from the layout editor, or they can be changed by slide actions. Slide-triggered stage actions can choose a layout per stage screen.

The stage action also has a delivery mode:

- **Stage Only**: advancing those slides updates stage screens while leaving the last audience output in place.
- **Stage + Audience**: resumes sending content to both stage and audience destinations.

Stage layout actions persist until changed manually or replaced by another slide's stage layout action.

## Announcement Layer

The Announcement Layer is a second presentation lane. A presentation can target Announcements instead of the normal Presentation destination. Once targeted, it runs on the announcement layer and is made visible by enabling Announcements in the appropriate Look/screen.

Common use:

- Lobby screen runs an announcement loop.
- Main room continues to run service slides.
- Announcement layer can be cleared independently from the preview/clear controls.

Important behavior: if an announcement screen also has lower layers enabled, those lower layers may show too. The official announcement article emphasizes that the output "truly does layer." For a lobby-only announcement screen, enable Announcements and disable the lower content that should not appear there.

## Messages

Messages are operator-controlled text overlays shown on audience screens. Current Renewed Vision timer, Remote, Control, and Show Controls articles describe Messages as a live tool for tokenized text, timer countdowns, theme formatting, transitions, dismiss behavior, remote triggering, and control-surface entry.

For ChurchPresenter, messages should be modeled as short-lived overlays with:

- A named message template or ad hoc text.
- Token/value support if we need repeatable alerts.
- Show, hide, and clear states.
- Layer routing through Looks/feed presets.
- Operator control over whether externally submitted content goes live.

## Props

Props are persistent audience-screen overlays managed from Show Controls. Current Remote and Control articles describe Props as triggerable overlays with active state; Clear Groups can clear the Props layer independently from slides, media, messages, announcements, audio, and video input.

For ChurchPresenter, Props map well to reusable overlay graphics: lower bugs, event branding, QR codes, or persistent labels. They should not be treated as normal slides because they can stay live while slides change underneath.

## Masks

The output-layer article identifies Mask as the layer used to mask parts of the presentation, creating a custom shape or transparent areas. The current support search did not surface a dedicated ProPresenter 7 mask article, so this reference keeps mask behavior conservative: model it as a screen/output-level clipping or alpha constraint that can affect visible output independently from slide/media content.

Implementation implication: a mask should not be stored as just another slide item. It affects composition of the rendered output and may need per-screen routing or enable/clear state.

## Show Controls

ProPresenter's Show Controls area groups operational tools such as:

- Audio Bin
- Stage Screens
- Timers
- Messages
- Props
- Macros

This is a useful product-design hint: not every live action belongs in the slide grid. Some live output controls are persistent operational surfaces that should stay accessible while the operator is running a show.

Sources: [Using a Stage Screen to its Full Potential](https://support.renewedvision.com/hc/en-us/articles/360041407794-Using-a-Stage-Screen-to-its-Full-Potential), [The Announcement Layer](https://support.renewedvision.com/hc/en-us/articles/360041809953-The-Announcement-Layer), [What are Show Controls?](https://support.renewedvision.com/hc/en-us/articles/4412263446035-What-are-Show-Controls), [ProPresenter Output Layers](https://support.renewedvision.com/hc/en-us/articles/13634000690323-ProPresenter-Output-Layers), [Audience Countdown](https://support.renewedvision.com/hc/en-us/articles/360050786794), [Remote App Interface](https://support.renewedvision.com/hc/en-us/articles/43051104879379-ProPresenter-Remote-App-Interface), and [Using ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6032278869011-Using-ProPresenter-Control).
