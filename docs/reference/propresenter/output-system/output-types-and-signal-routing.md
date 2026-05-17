# Output Types and Signal Routing

ProPresenter's output model spans local displays, network video, SDI hardware, local inter-app video, placeholders, and multi-output screen arrangements. ChurchPresenter does not need to implement every transport immediately, but the data model should leave room for them.

## System Displays

System outputs are physically connected display outputs such as HDMI, DisplayPort, DVI, or VGA. In ProPresenter, these are selected as output devices for configured screens.

The toolbar can toggle audience and stage system screens on or off. Renewed Vision notes that SDI and NDI screens are always on and cannot be toggled off in the same way.

## NDI

NDI is a network video transport. ProPresenter uses it to send output to another device, software program, or hardware receiver on the same network. Common receiving targets include OBS, vMix, or other production devices.

Operational notes from Renewed Vision:

- ProPresenter uses NDI version 6.
- Sender and receiver generally need to be on the same network subnet with network discovery allowed.
- Hardwired gigabit Ethernet is recommended for reliable production use.
- NDI is CPU-intensive and bandwidth-intensive.
- NDI troubleshooting often starts by viewing the ProPresenter NDI output locally with NDI Studio Monitor/Video Monitor to distinguish ProPresenter issues from network issues.

For ChurchPresenter, NDI should be treated as an output transport with connection health and diagnostics, not merely a monitor.

## Blackmagic / SDI

Blackmagic outputs use supported DeckLink/UltraStudio devices for SDI output. Renewed Vision maintains a supported-device list for SDI output, live video input, and alpha keying.

Operational notes:

- Blackmagic Desktop Video drivers must be installed for the hardware.
- If Blackmagic Media Express cannot see or use the device, ProPresenter cannot use it either.
- SDI output capability depends on the exact device and video mode.

For ChurchPresenter, SDI support should be represented as a hardware-backed transport with device capability discovery, driver dependency checks, and explicit format/mode selection.

## Syphon

Syphon is a macOS-only local inter-app output type. It lets ProPresenter send video to another app on the same computer. Since ChurchPresenter is currently a Windows app, this is mainly a conceptual placeholder: local inter-process video transports exist, but Syphon itself is not a Windows feature.

## Placeholder Outputs

Placeholder outputs let users configure screens and Looks without being physically connected to the final output device. They are useful for off-site planning and service preparation.

ChurchPresenter should support a similar concept even before advanced transports exist: users should be able to define "Main Room 1920x1080", "Stream 1920x1080", or "Lobby 1080p" without requiring the final monitor to be attached.

## Mirrored, Grouped, and Edge-Blended Screens

Mirroring sends one rendered screen to multiple outputs. Grouped screens distribute one logical screen across a grid of outputs. Edge blending overlaps projector outputs and adjusts the overlap to create a larger image.

Edge blend settings include blend width, blend mode, radius, intensity, and color/black-level adjustment. Renewed Vision notes that perfect edge blends are difficult and hardware-dependent.

For ChurchPresenter:

- Mirror is a realistic near-term feature if we support multiple windows/endpoints for the same screen.
- Grouped and edge-blended screens are advanced screen mapping features and should not complicate the first simple output path.
- The model should not assume every screen has exactly one output.

## Alpha Keying

Alpha keying lets ProPresenter composite graphics over live video through a switcher. ProPresenter can output key/fill signals for transparent graphics.

Important distinctions:

- **NDI alpha** uses one NDI signal; the receiver interprets transparency.
- **SDI alpha** typically uses two physical connections: fill and key.
- **External key** is the common setup for sending key/fill to a broadcast switcher.
- **Internal key** is used when the Blackmagic device acts as a downstream keyer with video input and output.
- Since ProPresenter 7.14, alpha outputs can be straight or premultiplied.

Premultiplied keying applies alpha to the color before output; the fill looks like the editor view. Straight keying leaves full color in the fill signal and sends transparency separately in the key signal. Straight can be better when the switcher is configured for it, but premultiplied may be preferable when the fill signal is also used by itself.

For ChurchPresenter, transparency should be part of the render contract. Lower-third and overlay outputs should not assume an opaque black background forever.

## Audio Outputs

ProPresenter has separate audio output settings from visual screen configuration. Official audio-output articles describe:

- Main audio output device.
- Media Inspector audio output.
- Adjustable channel count.
- Per-output channel routing.
- SDI and NDI audio output through Blackmagic SDK/NDI rather than ordinary system audio.
- The need to choose "None" for Main output when only SDI/NDI audio is desired, to avoid duplicate audio.

ChurchPresenter's visual output model should not forget audio routing. Media playback may need per-output audio routing, delay, and future SDI/NDI audio behavior.

Sources: [Screen Configuration](https://support.renewedvision.com/hc/en-us/articles/360041879173-Screen-Configuration-in-ProPresenter), [Screens vs Outputs](https://support.renewedvision.com/hc/en-us/articles/360041879993-What-s-the-Difference-Between-Screens-and-Outputs), [NDI Troubleshooting](https://support.renewedvision.com/hc/en-us/articles/4403014667283-NDI-Troubleshooting), [Supported Blackmagic Devices](https://support.renewedvision.com/hc/en-us/articles/360011598133-ProPresenter-Supported-Blackmagic-devices), [Alpha Keying](https://support.renewedvision.com/hc/en-us/articles/18435523006739-Alpha-Keying-in-ProPresenter), [Premultiplied and Straight Keying](https://support.renewedvision.com/hc/en-us/articles/18438961234835-Premultiplied-and-Straight-Keying), [Audio Outputs](https://support.renewedvision.com/hc/en-us/articles/360052697694-Audio-Outputs-in-ProPresenter), [SDI/NDI Audio Outputs](https://support.renewedvision.com/hc/en-us/articles/360053282613-SDI-NDI-Audio-Outputs), and [Edge Blending](https://support.renewedvision.com/hc/en-us/articles/360041820053-Edge-Blending-in-ProPresenter).
