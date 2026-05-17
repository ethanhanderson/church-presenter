# Mental Model

ProPresenter's output system is best understood as four separate decisions:

1. What content is live?
2. Which layer does that content occupy?
3. Which audience or stage screen should render it?
4. Which physical, network, or placeholder output receives that screen?

Keeping those decisions separate is the key architectural lesson for ChurchPresenter.

## Core Terms

**Screen** is ProPresenter's internal render target. It is the digital representation of a destination such as the main room screen, a lobby screen, a stream feed, or a stage confidence monitor. Renewed Vision describes a screen as "one render out of ProPresenter." A screen can drive a single output or several outputs, depending on the screen type.

**Output** is the hardware, network, or virtual destination connected to a screen. Official output types include system display outputs, Blackmagic/SDI outputs, NDI outputs, Syphon on macOS, and placeholders.

**Audience screen** is a crowd-facing or program-facing screen. Audience screens use Looks to determine which layers appear on each configured screen.

**Stage screen** is a performer/operator confidence display. Stage screens use stage layouts rather than audience Looks. They can include current and next slide content, notes, timers, screen previews, chord charts, capture status, and stage messages.

**Layer** is a fixed compositing slot in the output stack. ProPresenter exposes a fixed set of output layers rather than arbitrary user-defined layers.

**Look preset** is a saved routing/composition preset for audience screens. A look says which output layers appear on each audience screen and can apply an alternate theme to presentation content on a particular screen.

## Why This Matters

ProPresenter is not simply "one live slide sent everywhere." It is closer to a small routing and compositing engine:

- One slide action can affect a slide layer while media continues independently.
- An announcement presentation can loop on the announcement layer while the main presentation continues on the slide layer.
- A look can send lyrics plus media to the room, lyrics-only lower thirds to a stream, and announcements-only content to a lobby screen.
- A stage layout can change for performers without changing what the audience sees.
- A screen can be configured before hardware exists by using a placeholder output.

For ChurchPresenter, the important invariant is that operator actions, layer state, screen composition, and output transport should not collapse into one global "current output" concept.

## Practical Design Shape

Use these conceptual boundaries in our code and UI:

- **Content state**: active slide, active media, active announcement item, active message, active prop, active mask.
- **Layer state**: what each layer currently contains, whether it is visible, whether it is clearing or transitioning, and whether it has playback controls.
- **Screen composition**: which layers and theme variants a screen should render.
- **Output mapping**: where each screen is delivered: local monitor, future NDI/SDI transport, placeholder, or multi-output group.
- **Operator presets**: named Looks/feed presets that can change routing quickly without rebuilding output configuration.

Sources: [Screens vs Outputs](https://support.renewedvision.com/hc/en-us/articles/360041879993-What-s-the-Difference-Between-Screens-and-Outputs), [Screen Configuration](https://support.renewedvision.com/hc/en-us/articles/360041879173-Screen-Configuration-in-ProPresenter), [Output Layers](https://support.renewedvision.com/hc/en-us/articles/13634000690323-ProPresenter-Output-Layers), and [Using Looks](https://support.renewedvision.com/hc/en-us/articles/360041407174-Using-Looks-to-Show-Different-Screen-Content-in-ProPresenter-7).
