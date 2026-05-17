# Outputs, Screens, and Broadcast Routing

ProPresenter's output model is built around logical screens, fixed layers, Looks, and multiple endpoint types. The public pages highlight multi-screen management, up to eight unique outputs, broadcast-quality output, alpha key, SDI, NDI, Syphon, edge blend, announcements, props, masks, recording, streaming, and Resi.

## Source Basis

- [Main ProPresenter page](https://renewedvision.com/propresenter)
- [Multi-screen management](https://renewedvision.com/propresenter/multi-screen-management)
- [Streaming](https://renewedvision.com/propresenter/streaming)
- [Screens vs Outputs](https://support.renewedvision.com/hc/en-us/articles/360041879993-What-s-the-Difference-Between-Screens-and-Outputs)
- [Screen Configuration](https://support.renewedvision.com/hc/en-us/articles/360041879173-Screen-Configuration-in-ProPresenter)
- [Output Layers](https://support.renewedvision.com/hc/en-us/articles/13634000690323-ProPresenter-Output-Layers)
- [Using Looks](https://support.renewedvision.com/hc/en-us/articles/360041407174-Using-Looks-to-Show-Different-Screen-Content-in-ProPresenter)
- [Announcement Layer](https://support.renewedvision.com/hc/en-us/articles/360041809953-The-Announcement-Layer)
- [Clear Groups](https://support.renewedvision.com/hc/en-us/articles/4408681880211-Using-Clear-Groups-in-ProPresenter)
- [Edge Blending](https://support.renewedvision.com/hc/en-us/articles/360041820053-Edge-Blending-in-ProPresenter)
- [Alpha Keying](https://support.renewedvision.com/hc/en-us/articles/18435523006739-Alpha-Keying-in-ProPresenter)
- [Premultiplied and Straight Keying](https://support.renewedvision.com/hc/en-us/articles/18438961234835-Premultiplied-and-Straight-Keying)
- [NDI Troubleshooting](https://support.renewedvision.com/hc/en-us/articles/4403014667283-NDI-Troubleshooting)
- [Recording Screens](https://support.renewedvision.com/hc/en-us/articles/360049227893-Recording-ProPresenter-Screens)
- [RTMP Streaming](https://support.renewedvision.com/hc/en-us/articles/360049229013-Streaming-a-ProPresenter-Screen-to-An-RTMP-Server)
- [Resi Streaming](https://support.renewedvision.com/hc/en-us/articles/360058942613-Setting-up-Resi-Streaming-in-ProPresenter)
- [Output system reference](../output-system/README.md)
- [Output types and signal routing](../output-system/output-types-and-signal-routing.md)
- [Layers and Looks](../output-system/layers-and-looks.md)

## Screens Versus Outputs

The key product distinction is that a screen is what ProPresenter renders, while an output is where that rendered signal goes. An audience screen may map to a system display, an SDI output, an NDI stream, a Syphon feed, a placeholder, a capture session, or a grouped/edge-blended set of endpoints.

ChurchPresenter should model:

- `OutputScreen`: logical render target, category, size, background/fallback settings, and diagnostics.
- `OutputEndpoint`: monitor/window, placeholder, NDI, SDI, Syphon-like local video, capture, or future endpoint.
- `ScreenMapping`: relationship between logical screen and one or more endpoints.
- Endpoint capability and health: connected, missing, driver/network state, selected mode, alpha/audio support, and last frame status.

Do not make a WinUI window, macOS window, monitor id, or capture device the identity of a screen. Those are endpoint bindings.

Current screen/output behavior:

- Screen Configuration opens from `Screens > Configure Screens`.
- **Audience** means crowd-facing screens; **Stage** means confidence/production screens.
- Screen types are **Single**, **Mirror**, **Grouped**, and **Edge Blend**.
- A **Single** screen renders one screen to one output.
- A **Mirror** screen renders once and sends to multiple outputs; mirrored outputs appear as separate boxes, with the primary output leftmost.
- A **Grouped** screen renders one image across a grid of outputs, such as a TV wall.
- An **Edge Blend** screen uses multiple projectors with overlap/blend settings.
- Output types include System, BlackMagic/SDI, NDI, Syphon on macOS, and Placeholder.
- Placeholders support off-site setup when final hardware is not attached.
- Hardware tab settings select device/resolution and show output-specific information such as name and size.
- Screens settings can show performance statistics on screen, including internal frame rate and actual output frame rate.
- Current Screen Configuration also includes Bit Depth and HDR output settings.

Architecture implication: screen setup is a portable logical model plus a local hardware binding model, with performance/health diagnostics visible to operators.

## Audience and Stage Screens

Audience screens show production output. Stage screens show confidence/operator information. They share some lower-level rendering infrastructure but have different content models:

- Audience screens use Looks and audience layer composition.
- Stage screens use stage layouts and stage data providers.
- Audience screens may be recorded, streamed, keyed, routed, or edge-blended.
- Stage screens may show current/next text, notes, clocks, timers, stage messages, video countdowns, and screen previews.

ChurchPresenter should build separate audience and stage render frames so the stage dashboard does not become a special audience Look.

## Fixed Output Layers

ProPresenter's audience output is organized around fixed layers:

- Audio.
- Messages.
- Props.
- Announcements.
- Slide.
- Media.
- Live Video.
- Mask.

These layers are independently routed, cleared, and automated. Even when the first ChurchPresenter UI exposes only slide and media clears, the backend should preserve stable layer identities so future props, messages, announcements, masks, and live video do not require a rewrite.

## Looks

Looks are per-screen layer routing presets. In the Looks matrix, layers are routed to audience screens, and alternate themes can be applied to presentation content for a screen.

Typical pattern:

- Main room: slide plus media layers.
- Stream: slide content with a lower-third theme, possibly without full background media.
- Lobby: announcement layer.
- Overflow: mirrored or adapted main content.

Looks are not the same as themes. A theme formats content; a Look decides which layers and theme variants each screen receives.

Current Looks behavior:

- Looks open from `Screens > Edit Looks`.
- Rows are output layers and columns are configured audience screens.
- **Enable Identify** helps visually identify what is going to outputs.
- Each saved setup is a **Look Preset**; presets can be renamed or deleted.
- Look presets are effectively unlimited.
- A Look is made live with **Make Live**.
- Looks can also change from slide actions, the Screens menu, or macros.
- Alternate themes can be selected from the Presentation/Slide layer dropdown for a screen, enabling lower-third stream formatting from the same slide text.
- If lyrics unexpectedly appear with a different format on a screen, the Look's alternate theme assignment is a likely cause.

Architecture implication: active Look, layer visibility per screen, and per-screen theme override should be observable live state.

## Announcements

Announcements are a separate presentation lane. They can run at the same time as the main presentation, commonly for lobby loops, pre-service slides, or screens that should not follow the main room.

Implementation model:

- Announcement layer state independent from slide/presentation layer state.
- Announcement playlist/presentation playback that can run simultaneously with the main show.
- Looks route announcements to selected audience screens.
- Clear announcement should not clear the main slide layer unless a clear group explicitly includes both.

Current announcement behavior:

- A presentation is assigned to the Announcement Layer from the target icon in the Presentation Header.
- Default target is **Presentation**; choosing **Announcements** turns the target icon green.
- The Announcement layer must also be enabled for the desired screen in Looks.
- Layering still applies: if lower layers remain enabled in the Look, they can appear beneath announcement content.
- Announcement loops commonly use Go To Next timers with the last slide set to loop to beginning.
- Standard presentation content can run on other screens while the announcement presentation continues.
- The megaphone clear icon clears just the Announcement Layer.

Architecture implication: announcement targeting is presentation metadata plus live layer routing, not a separate playlist window.

## Props

Props are reusable text/media overlays that can be shown, hidden, triggered, and automated. They work well for persistent bugs, QR codes, lower-third labels, or visual markers that should survive slide changes.

Implementation model:

- Prop documents with media/text/scene content.
- Prop layer state independent from slides and messages.
- Toggle/show/hide commands.
- Routing through Looks and clearing through prop-specific clears or clear groups.

## Masks

Masks create custom shapes or transparent areas to eliminate unwanted content or create projection effects. The mask layer should be treated as a composition constraint rather than an ordinary slide object.

Architecture implications:

- Masks may be screen-specific.
- Masks need alpha/clip behavior in the compositor.
- Masks should be routed through Looks/screen configuration.
- Clear/disable behavior should be explicit and diagnostic-friendly.

## Edge Blend and Grouped Screens

Edge blend lets multiple projectors create one wider visual canvas. Even if ChurchPresenter does not implement blending immediately, the screen model should not assume one logical screen equals one endpoint.

Reserve room for:

- One logical render split across multiple endpoints.
- Mirrored outputs.
- Grouped output walls.
- Blend regions, overlap width, blend mode, black-level/color correction, and calibration metadata.

Current edge-blend behavior:

- Edge Blend is added from the `+` next to Audience in Screen Configuration.
- Outputs are assigned in the Hardware tab like other screens.
- Selecting the blended area exposes **Edge Blend** and **Color Adjustment** tabs.
- Blend width consumes part of the projector overlap, so effective output width is smaller than raw projector sum.
- Blend modes include Linear, Cubic, and Quadratic.
- Radius and Intensity sliders tune the blend.
- Color Adjustment includes Black Level to compensate for brighter overlaps.
- Renewed Vision explicitly notes perfect blends are difficult because projector quality, lamp age, hours, and lamp brand affect visible overlap.

Architecture implication: edge blend should be a calibrated endpoint group with operator-adjusted parameters and expectations, not a simple scale transform.

## Broadcast Outputs

The public feature pages identify SDI, NDI, Syphon, and alpha key as broadcast workflows.

### Alpha Key

Alpha key output provides clean overlay graphics for switchers, often through key/fill signals or alpha-capable NDI. ChurchPresenter render frames should preserve transparency and alpha-mode metadata so lower thirds and graphics do not assume opaque black backgrounds.

Current alpha behavior:

- Alpha key is used to put ProPresenter graphics over live video through a switcher.
- SDI alpha setup requires Blackmagic drivers and compatible hardware.
- For most SDI workflows ProPresenter configures one DeckLink screen even though two physical signals are sent.
- Alpha tab options include None, Straight key, and Premultiplied key.
- External Key sends separate key/fill outputs to a switcher; Internal Key uses a Blackmagic device as a downstream keyer with video in/out.
- NDI alpha sends one network output that a receiver interprets with alpha.
- Starting in ProPresenter 7.14, any graphics output can be assigned as the key for another output, enabling alpha key over HDMI/DisplayPort graphics outputs.
- Graphics-output alpha key may not be frame synced unless the GPU/device path supports it.
- Straight key outputs full-color fill and leaves transparency entirely to the key signal.
- Premultiplied key outputs fill already multiplied by alpha, matching historical ProPresenter output and common ATEM defaults.
- Media and NDI inputs with alpha can also specify premultiplied/straight handling.

Architecture implication: render frames need alpha intent, alpha type, and endpoint capability metadata. Content alpha interpretation and output alpha encoding are related but distinct.

### SDI

SDI output/input depends on hardware such as Blackmagic DeckLink or UltraStudio products. It requires driver availability, device discovery, video mode selection, audio routing, and possibly key/fill capability. It should be an endpoint type with health and capabilities, not another display window.

### NDI

NDI sends video over Ethernet and can also be used for inputs. It needs network discovery, same-subnet assumptions, bandwidth/CPU diagnostics, loopback testing, and separate health from local displays.

Current NDI behavior:

- ProPresenter uses NDI version 6.
- Sender and receiver generally need to be on the same subnet with network discovery allowed.
- Renewed Vision recommends dedicated hardwired gigabit Ethernet for reliable NDI.
- NDI Tools/Studio Monitor or Video Monitor are recommended for isolating whether a problem is ProPresenter, local loopback, network, receiver app, or receiving computer.
- Local loopback to NDI monitor can bypass the external network and verify what ProPresenter sends.
- Low Bandwidth Mode on cameras/receivers can make signal quality appear low.
- NDI sending/receiving is processor-intensive and bandwidth-intensive.

Architecture implication: NDI endpoint health should include version, source discovery, subnet/network state, bandwidth hints, CPU/performance warnings, and loopback-test status.

### Syphon

Syphon is a macOS local-video sharing path for compatible apps. It belongs behind the same endpoint abstraction, but its implementation is platform-specific.

## Recording, RTMP Streaming, and Resi

Recording and streaming capture one configured screen with settings such as destination, codec, resolution, frame rate, elapsed time, and connection health. RTMP streaming reports health through status colors; Resi integration adds resilient streaming behavior through an external service/hardware workflow.

ChurchPresenter should treat capture/streaming as screen consumers:

- Subscribe to resolved audience or stage screen frames.
- Preserve source screen identity.
- Report capture status, elapsed time, dropped frames, connection state, and error messages.
- Start/stop through the same command executor used by UI, macros, remotes, calendar scheduling, and devices.

Current capture/streaming behavior:

- Capture Settings open from `Screens > Capture Settings`, the Live button, or integration-specific settings.
- Recording source can be any configured Audience or Stage screen.
- Capture supports one ProPresenter screen at a time.
- Recording destination can be disk file or stream.
- Disk recording settings include save location, codec, resolution, and frame rate; recommended resolution generally matches the captured screen.
- Capture can start immediately from the settings window or later from `Screens > Start Capture`.
- Active capture shows a progress indicator in the Preview window and can stop from that indicator, Screens menu, or Capture Settings.
- RTMP streaming requires stream key/provider settings and supports presets.
- RTMP health indicator is green for healthy streaming, yellow for dropped frames, and red for lost connection or stopped stream.
- Resi setup signs in from the Integrations tab, installs a Resi plug-in, and registers the ProPresenter machine as an encoder.
- Resi capture settings include source screen, event name, destination group, resolution/bitrate preset, and stereo AAC audio.
- Resi streams can start from ProPresenter or from Resi Studio; scheduled Resi events notify the ProPresenter operator with a 15-second cancel window.
- Resi integration exposes encoder status, encoder name, Resi Studio management, encoder update checks, and capture settings.

Architecture implication: capture/streaming sessions need source screen, destination type, profile, audio routing, health, remote-start notifications, and integration identity.

## Diagnostics and Recovery

Multi-screen production needs operator-facing diagnostics:

- Which logical screen is live.
- Which endpoint is connected/missing/unhealthy.
- Which Look is active.
- Which layers are routed to a screen.
- Which layer is visible or cleared.
- Which capture/stream session is healthy.
- Whether output failure is render, endpoint, network, driver, or media related.

ChurchPresenter should make this state queryable and visible. Output diagnostics are not only developer logs; they are live-service recovery tools.

## Cross-Feature Interactions

Screens consume content from presentations, media, announcements, messages, props, masks, and live video. Looks connect routing with themes. Broadcast endpoints depend on alpha/audio settings. Stage screens depend on live presentation and timer state. Capture depends on resolved screen frames. This is why the output engine should sit behind application-layer contracts rather than inside a platform-specific output window.
