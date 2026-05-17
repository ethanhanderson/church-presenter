# Layers and Looks

Audience output in ProPresenter is a composited stack of fixed layers. Looks decide which layers appear on which audience screens.

## Fixed Output Stack

Renewed Vision's output-layer article says ProPresenter output is made of **8 fixed layers**. The help center and UI article identify these layer concepts:

- Audio
- Messages
- Props
- Announcements
- Slide
- Media
- Live Video
- Mask

The UI article lists the visible clear-button order from top to bottom as: Audio, Messages, Props, Announcements, Slide, Media, Live Video. The output-layer article separately identifies Mask as a layer used to mask parts of the presentation and create custom shape/transparent areas. For implementation, treat mask as a special compositing constraint over audience output rather than ordinary content.

## Layer Responsibilities

**Slide layer** displays slide elements: lyrics, Bible text, presentation text, and other slide objects. Slide and presentation background colors are associated with this area of the output model, and they interact with media in non-obvious ways.

**Media layer** displays media actions: images or videos triggered from the Media Bin, or media actions inside presentations.

**Announcement layer** can run a second presentation at the same time as the normal presentation. It is commonly used for lobby loops or other screens that should not follow the main room presentation.

**Messages layer** displays short operator-triggered text overlays, such as nursery alerts, countdowns, or other notices.

**Props layer** overlays persistent slide-like objects. The official UI/show-control articles identify Props as a show-control item that can be created, edited, shown, and hidden on audience screens.

**Live Video layer** represents video input content in the audience stack.

**Audio layer** participates in clear/playback controls even though it is not visually composited in the same way as slide/media layers.

**Mask layer** clips or masks portions of presentation output.

## Background Color and Media Ordering

The most implementation-relevant edge case is slide/presentation background color versus media. Renewed Vision notes that starting in ProPresenter 7.11, background color moved above the media layer. As a result, a background media action can appear hidden by a slide or presentation background color.

The practical rules:

- Screen color is the bottom fallback and is configured per audience screen.
- Background media shows when slide/presentation background colors are disabled.
- If background media and slide/presentation background colors are both enabled, the background color can cover the media.
- Foreground media still shows when background colors are enabled.
- Troubleshooting unexpected missing media often means checking slide background, presentation background, view options, "Ignore Background Colors", or converting the media to foreground/slide object.

For ChurchPresenter, keep "slide background", "presentation/theme background", and "media layer content" explicit. Avoid a hidden default that silently covers media.

## Looks

Looks are audience-screen composition presets. The Looks window is a matrix:

- Rows are ProPresenter output layers.
- Columns are configured audience screens.
- Each cell controls whether that layer appears on that screen.

Looks can also apply alternate themes for presentation/slide content on a given screen. That is how one presentation can render normal full-screen lyrics in the room while rendering lower thirds for a stream.

Look presets can be changed:

- From the Looks window with "Make Live".
- Via slide actions.
- From the Screens menu.
- Via macros.

## Example Routing Pattern

A common ProPresenter-style setup:

- Main room screen: Slide + Media.
- Stream screen: Slide only, with an alternate lower-third theme.
- Lobby screen: Announcements only.

This is not three separate presentations in the operator's head. It is one output system where layers and theme variants are routed differently per screen.

## Clear and Preview Behavior

The Preview/Clear area shows live screen content and lets the operator choose which screen to preview. Clear groups can clear multiple configured layers, and individual clear buttons clear specific layers. The clear-button stack order reflects the layer stack.

ChurchPresenter should preserve this idea: clearing media should not necessarily clear slides, clearing announcements should not necessarily clear the main slide layer, and a grouped "clear all" should be configurable rather than hard-coded as the only behavior.

## Clear Groups

ProPresenter 7.7+ lets operators configure named clear groups instead of relying only on built-in single-layer clears and one fixed "Clear All" action. The operator opens the configuration window from the clear actions area, starts with a default **Clear All** group, can add more groups with a `+` button, and can delete groups from the group list context menu.

A clear group has operator-facing attributes:

- **Name**: the label shown in the clear actions area.
- **Icon**: a built-in or custom icon that helps distinguish similar recovery actions.
- **Tint**: optional icon color, useful for high-risk or frequently used actions.
- **Layer membership**: checkboxes for the layers and playback categories the group should clear.

The article identifies these group members:

- **Music**: audio-bin tracks marked as audio tracks.
- **Audio Effects**: audio-bin items marked as sound effects.
- **Messages**: the messages layer.
- **Props**: the props layer.
- **Announcements**: announcement-layer presentations, with an option to stop announcement timelines.
- **Presentation**: slide/presentation-layer elements, with an option to stop presentation timelines.
- **Presentation Media**: media actions attached to presentations, excluding media inserted as ordinary slide editor elements.
- **Video Input**: active video inputs.

Implementation implication for ChurchPresenter: the app should expose both single-layer clear buttons and named clear groups from the same command model. The built-in **Slide** and **Media** output-panel clears are useful shortcuts, but they should not become special page-only behavior. A future **Clear All** button should be just the default configured clear group, and editing that group should intentionally change what "Clear All" means. If the operator removes a layer from Clear All, the app should allow that but make it clear that no true all-layer group exists unless they create one.

Recommended default groups:

- **Clear All**: clears audio/music, audio effects, messages, props, announcements, slide/presentation, presentation media, and live video.
- **Clear Slide**: clears the slide/presentation layer only.
- **Clear Media**: clears presentation media and independent media-layer content without clearing slide text/objects.
- **Clear Overlays**: clears messages and props without affecting slide or media.
- **Clear Announcements**: clears the announcements layer only.

For the ChurchPresenter backend, group membership should map to explicit output-layer and playback scopes. A group member can expand to more than one backend layer, for example a media-oriented clear may need `Media` plus `Audio` where an item owns audible playback. Timeline stop options should be stored as group settings, not hardcoded into the output-panel button.

## Layer Transitions

ChurchPresenter resolves transitions by treating Show controls as defaults and cue settings as increasingly specific intent:

1. Show page transition controls provide global slide and media defaults.
2. Presentation or arrangement transition settings override the global slide default for slides in that presentation.
3. Individual layer cues override broader defaults, such as per-slide transitions or per-media cue transitions.

The resolved transition belongs to the layer whose payload changes. A slide transition should not restart persistent media, and a media transition should not imply that the slide layer changed. Looks decide whether layers appear on audience screens and may select theme/layout variants, but they should not change transition precedence.

Only layers with renderer support should expose transition controls. Slide and media are the mature transition paths today. Audio follows media state for routing and diagnostics but has no visible animation, and messages, props, announcements, live video, and mask should gain transition controls only when their payload models and output hosts can apply them.

Sources: [ProPresenter Output Layers](https://support.renewedvision.com/hc/en-us/articles/13634000690323-ProPresenter-Output-Layers), [Using Looks to Show Different Screen Content](https://support.renewedvision.com/hc/en-us/articles/360041407174-Using-Looks-to-Show-Different-Screen-Content-in-ProPresenter-7), [Understanding the ProPresenter User Interface](https://support.renewedvision.com/hc/en-us/articles/360041345954-Understanding-The-ProPresenter-User-Interface), [Using Clear Groups](https://support.renewedvision.com/hc/en-us/articles/4408681880211-Using-Clear-Groups-in-ProPresenter), and [The Announcement Layer](https://support.renewedvision.com/hc/en-us/articles/360041809953-The-Announcement-Layer).
