# Screens and Outputs

ProPresenter uses "screen" and "output" as distinct concepts.

A **screen** is the render target configured inside ProPresenter. An **output** is the device or transport that receives the rendered screen. The distinction matters because a single screen can be rendered once and sent to multiple destinations, or a screen can be designed before the final destination is connected.

## Audience and Stage Screens

The Screen Configuration window separates screens into **Audience** and **Stage** groups.

- Audience screens are crowd-facing, stream-facing, lobby-facing, or otherwise program-facing destinations.
- Stage screens are confidence monitors, foldback screens, down-stage monitors, or operator displays.

Audience screens are controlled by Looks. Stage screens are controlled by stage layouts.

## Screen Types

Official ProPresenter screen types:

- **Single**: one screen render sent to one output. This is the common default.
- **Mirror**: one screen render sent to multiple outputs. The rendered composition is shared, while each mirrored output has its own output box/properties in configuration.
- **Grouped**: one screen render distributed across a grid of multiple outputs, such as a video wall.
- **Edge Blend**: multiple projector outputs blended into a larger seamless image.

The key design point is that grouped, mirrored, and edge-blended configurations are still screen-level concepts. The app renders a logical screen, then maps it onto one or more outputs.

## Output Types

Official output types named by Renewed Vision:

- **System**: physically connected display outputs such as HDMI, DisplayPort, DVI, or VGA.
- **Blackmagic / SDI**: SDI output through supported Blackmagic devices.
- **NDI**: network video output to software or hardware receivers on the network.
- **Syphon**: macOS-only local inter-app video output.
- **Placeholder**: a screen-sized stand-in used when the real output is not connected yet.

Placeholders are important for ChurchPresenter planning because they let users build service output setups off-site or before production hardware is available.

## Screen Configuration Behavior

The Screen Configuration window is where ProPresenter creates audience/stage screens and assigns output hardware, network, or placeholder destinations. The Hardware tab selects the output device/resolution and shows properties such as name and size/resolution depending on output type.

ProPresenter can show performance statistics on screens from settings. That is useful as a product clue: output surfaces often need per-screen diagnostics, not just a single global performance indicator.

## Design Implications

For ChurchPresenter:

- Model screens separately from monitors or transport endpoints.
- Let one screen map to multiple output endpoints when mirroring is needed.
- Leave room for placeholder outputs so service files can describe future screens.
- Treat stage screens as first-class screens, but do not route them through the same look/layer matrix as audience screens.
- Consider per-screen diagnostics for frame rate, render health, and output connection state.

Sources: [What's the Difference Between Screens and Outputs?](https://support.renewedvision.com/hc/en-us/articles/360041879993-What-s-the-Difference-Between-Screens-and-Outputs), [Screen Configuration in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/360041879173-Screen-Configuration-in-ProPresenter), and [Edge Blending in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/360041820053-Edge-Blending-in-ProPresenter).
