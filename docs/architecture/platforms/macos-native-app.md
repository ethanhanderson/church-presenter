# Native macOS App Implementation

This document defines how a modern native macOS ChurchPresenter app should implement the shared architecture in `docs/architecture/`. It is intentionally subordinate to the platform-agnostic contracts:

- Product semantics, command execution, live state, output routing, content graph, packages, diagnostics, and recovery are defined by [`target-architecture.md`](../target-architecture.md), [`backend-application.md`](../backend-application.md), [`content-management.md`](../content-management.md), [`rendering-engine-replacement.md`](../rendering-engine-replacement.md), and [`file-access-and-cache-resilience.md`](../file-access-and-cache-resilience.md).
- macOS owns only the native host concerns described in [`native-hosts.md`](../native-hosts.md): windows, menus, views, platform adapters, display and media APIs, accessibility, security prompts, packaging, and app lifecycle.
- The ProPresenter feature reference in [`features.md`](../../reference/propresenter/features.md) informs the shared model. This document does not add a second product model for macOS.

The core rule remains: the native macOS shell expresses operator intent; the shared application runtime owns production truth.

## Implementation Goals

The macOS app should feel like a first-class Mac app while preserving the same production behavior as the Windows host.

- Use Swift and SwiftUI for the operator shell, settings, menus, and app-native workflows.
- Use AppKit where macOS requires direct control over windows, displays, responder-chain behavior, menu validation, output fullscreen behavior, accessibility elements, and hosted rendering surfaces.
- Use AVFoundation, Core Audio, ScreenCaptureKit, Core Animation, Metal, Core Graphics, and system security APIs behind adapters that report capabilities and health to the shared runtime.
- Keep portable content and machine-local bindings separate in storage, import/export, sync, diagnostics, and repair.
- Keep rendering host-neutral until the host adapter applies an immutable scene or frame to a macOS surface.

## Suggested Project Layout

Use a native Swift package or Xcode workspace under a future macOS app root, for example:

```text
apps/macos/ChurchPresenter/
  ChurchPresenter.xcodeproj
  ChurchPresenterApp/
    ChurchPresenterApp.swift
    AppDelegate.swift
    Entitlements/
    Info.plist
  AppHost/
    RuntimeBootstrap/
    Commands/
    Navigation/
    Diagnostics/
  Features/
    Show/
    Editor/
    Reflow/
    Themes/
    OutputSetup/
    Settings/
  Rendering/
    SceneAdapter/
    OutputRenderer/
    ThumbnailRenderer/
    EditorCanvas/
    Metal/
  PlatformAdapters/
    Display/
    Media/
    Audio/
    Capture/
    FileAccess/
    Credentials/
    Accessibility/
    DeviceControl/
  Persistence/
    MachineBindings/
    BookmarkStore/
    CacheStore/
  Tests/
    Unit/
    Integration/
    UI/
    RenderingSnapshots/
```

Keep this organization aligned to the shared boundary:

| macOS area | Shared contract it implements |
|---|---|
| `AppHost/RuntimeBootstrap` | runtime service registration, command bus, query surfaces, diagnostics sinks |
| `Features/*` | host-side view state and command/query presentation |
| `Rendering/*` | `SceneSnapshot`, `AudienceFrameSet`, `StageFrameSet`, adapter diagnostics |
| `PlatformAdapters/Display` | `OutputEndpoint`, `ScreenMapping`, endpoint capability and health |
| `PlatformAdapters/Media` | `MediaRenderPayload`, `AudioRenderPayload`, live video input records, player state feedback |
| `PlatformAdapters/FileAccess` | resource stamps, security-scoped bookmarks, relink and permission diagnostics |
| `Persistence/MachineBindings` | monitor ids, device ids, window placement, credentials references, caches |

Do not introduce macOS-only persistence formats for presentations, playlists, Looks, stage layouts, clear groups, media cue metadata, packages, or generated-content provenance unless the shared docs define an extension point for them.

## App Lifecycle and Composition

Use a SwiftUI `@main App` as the public app entry point, with AppKit bridging for lifecycle and output windows.

- Define the main operator shell with `WindowGroup`, because SwiftUI provides standard macOS window behavior, state restoration hooks, and default window-management commands.
- Define `Settings` with SwiftUI for app settings that are host-local or portable by category.
- Use SwiftUI `commands` and `CommandMenu` for menu bar actions, keyboard shortcuts, and contextual command availability.
- Bridge AppKit through `NSApplicationDelegateAdaptor` or a lightweight AppKit coordinator for startup ordering, activation events, termination prompts, display notifications, output window management, and Apple Event or URL activation.
- Use SwiftUI `Scene` phase changes for host lifecycle hints only. Runtime live-state persistence, cache invalidation, recovery, and output state must remain application-service decisions.

The shell should bootstrap in this order:

1. Create the app object and register crash/log sinks.
2. Initialize machine-local settings, bookmark store, and secure credential access.
3. Start the shared runtime and content services.
4. Restore machine-local output bindings and validate displays/endpoints.
5. Open the operator shell and diagnostics channel.
6. Apply any restored output windows only after runtime queries confirm the active show/session state.

The app must tolerate partial startup. Missing content roots, stale bookmarks, denied media capture permission, unavailable displays, and failed transport adapters should produce diagnostics and recovery actions instead of preventing the operator shell from opening.

## Runtime Boundary

The macOS host should expose a small set of host services to the shared runtime:

- `MacCommandSource`: translates menu items, keyboard shortcuts, clicks, gestures, device events, and automation input into `LiveCommand` values with source metadata.
- `MacQueryPresenter`: subscribes to read models for Show, Editor, Output Setup, Settings, diagnostics, and support tools.
- `MacFrameHost`: applies `AudienceFrameSet` and `StageFrameSet` changes to previews, outputs, thumbnails, and capture consumers.
- `MacHostFeedbackSink`: reports display, player, renderer, file-access, capture, and transport health back to runtime diagnostics.
- `MacRecoveryPresenter`: presents runtime-suggested recovery actions and dispatches the selected command or service operation.

Host view models may store selection, focus, pane layout, filters, local drag state, modal state, and optimistic UI affordances. They must not store authoritative live layers, Look routing, clear group expansion, stage layout resolution, package conflict decisions, or media relink truth.

Use Swift Observation (`@Observable`, `@Bindable`, and focused values) for host view models where it fits SwiftUI. Treat observed view models as projections of runtime queries, not as the runtime itself.

## Shell, Menus, and Workflow Windows

The macOS shell should organize the same workflow families named in `native-hosts.md`:

- Show
- Editor
- Reflow
- Themes
- Output Setup
- Settings and Support

Implementation guidance:

- Use SwiftUI `NavigationSplitView`, tab/sidebar patterns, inspectors, sheets, popovers, and native menus where they fit the workflow.
- Use AppKit-backed views for high-density decks, canvases, timeline surfaces, custom drag/drop, output previews, and any surface that requires precise responder-chain, focus-ring, or accessibility control.
- Put command-producing UI behind explicit action methods, for example `takeSlide(slideID:)`, `triggerMediaCue(cueID:)`, `activateLook(lookID:)`, `clearLayer(kind:)`, and `runMacro(macroID:)`. Those methods dispatch runtime commands instead of mutating host state.
- Use menu commands for live operation actions that need keyboard parity: next/previous, take selected, clear slide, clear media, clear group, black screen if modeled, stage message, open output setup, open diagnostics, relink selected media, and emergency recovery actions.
- Use focused values to let menu items follow the active window and selection without making global singletons own selection state.

The Show surface must keep the shared distinctions visible:

- selected operator item,
- live state per output layer,
- composed frame per logical screen,
- concrete endpoint/device health.

## View Models and State Flow

Prefer a unidirectional flow:

```text
Runtime query/read model
  -> Mac view model projection
  -> SwiftUI/AppKit view
  -> operator intent
  -> LiveCommand or document-service mutation
  -> runtime snapshot/read model update
```

Rules:

- View models may cache query projections for UI performance but must invalidate from runtime version stamps.
- Long operations should be Swift concurrency tasks owned by coordinators or services, not by view structs.
- Use `@MainActor` for UI-facing models and AppKit/SwiftUI integration. Use actors or serial queues for player, capture, file access, and rendering resource managers where thread-affinity is explicit.
- Document edits, generated content updates, package operations, and relink operations should call application services and receive graph-aware results.
- Live-edit-safe document changes should invalidate future prepared cues and render caches without tearing down the currently live payload unless a command replaces that payload.

## Display and Output Host

Use AppKit and Core Graphics for display discovery and output window management.

Recommended adapter responsibilities:

- Discover `NSScreen.screens`, names, frames, backing scale factors, color spaces, maximum frame rates, EDR values, and `CGDirectDisplayID` values.
- Map concrete displays to shared `OutputEndpoint` records and machine-local `ScreenMapping` bindings.
- Keep logical screens stable when physical displays connect, disconnect, rename, move, or change scale/color characteristics.
- Create one managed `NSWindow` or rendering surface per concrete local display endpoint.
- Report endpoint capability flags: fullscreen, transparency, HDR/EDR, maximum FPS, color space, scale factor, mirroring/grouping limitations, and capture support.
- Report endpoint health: connected, missing, remapped, wrong resolution, permission denied, host apply failed, render timing degraded, or transport unavailable.

Output windows should be owned by an `OutputWindowController`, not by SwiftUI view models. Each controller should:

- keep a stable reference to the logical screen and endpoint binding,
- use an AppKit content view that hosts the renderer surface,
- subscribe only to resolved frames for its logical screen,
- apply diffs from immutable frames,
- surface AppKit/window failures as host diagnostics.

For fullscreen behavior:

- Prefer AppKit fullscreen behavior (`toggleFullScreen(_:)`) for operator-visible or user-managed windows because it preserves expected macOS window control.
- For production output windows, support a dedicated presentation mode that places an output window on the target `NSScreen`, hides chrome, suppresses accidental input, and applies valid `NSApplication.PresentationOptions` only when the operator intentionally locks output.
- Never make the output window decide which layers appear. The window applies the frame resolved by the runtime for its logical screen.

When displays change, the adapter should not mutate portable screen or Look records. It should update machine-local endpoint state and ask the runtime for remap/recovery options.

## Rendering Adapter

The macOS renderer consumes host-neutral scenes and frames from `rendering-engine-replacement.md`.

Suggested implementation:

- Build a renderer facade with separate consumers for audience output, stage output, operator preview, thumbnails, editor canvas, screen preview/multiview, and diagnostics snapshots.
- Use a Core Animation layer tree for straightforward 2D composition where it is sufficient.
- Use Metal-backed rendering for high-layer-count output, advanced blending, masks, alpha/key output, EDR/HDR paths, screen capture/export surfaces, and performance-critical previews.
- Use `CAMetalLayer` or `MTKView` for output and preview surfaces that need frame pacing, backing-scale control, and low-overhead display.
- Use Core Text, AttributedString, or TextKit-backed layout in the adapter for `TextSceneNode` rendering, but keep measured layout results tied to scene/resource stamps.
- Use `CAShapeLayer`, Core Graphics, or Metal path rendering for shape and vector nodes based on complexity and performance.
- Use `AVPlayerLayer`, decoded sample buffers, or Metal texture paths for media nodes depending on whether the renderer needs effects, alpha, synchronization, or capture output.
- Use `AVCaptureVideoPreviewLayer` or sample-buffer/Metal paths for live video nodes depending on the cue and capture requirements.

Adapter restrictions:

- Do not read presentation documents or content roots directly.
- Do not interpret slide actions, macro actions, Looks, or clear groups.
- Do not flatten unsupported imported effects silently. Preserve diagnostics tied to the scene node and source reference.
- Do not use a screenshot of the output window as the primary capture pipeline when the runtime can provide resolved frames or renderer-owned textures.

Frame pacing and performance:

- Use display-linked rendering tied to the output screen where possible.
- Keep drawable sizes aligned to backing size for text and UI clarity; allow lower internal render targets only when quality policy permits.
- Track render timings per scene, frame, layer, and endpoint.
- Include display scale, color space, EDR/HDR settings, frame rate, dropped frames, and renderer backend in diagnostics.

## Stage, Preview, Thumbnail, and Editor Rendering

Stage outputs are not audience outputs with a different theme. They consume `StageFrameSet` and provider snapshots.

Implementation guidance:

- Build a `StageRendererAdapter` that consumes stage layout elements, provider snapshots, current/next context, timers, clocks, media countdown, stage messages, capture status, and preview references.
- Build operator previews as subscribers to resolved audience/stage frames.
- Build thumbnails as scene/frame consumers with thumbnail render intent; do not autoplay video or audio in thumbnails.
- Build editor canvases from the same scene model plus editor-only adorners: selection boxes, resize handles, guides, rulers, validation overlays, snapping, and drag/drop targets.
- Keep editor adorners out of serialized content and output frames.
- Use cache keys that include content stamps, resource stamps, theme stamps, token/provider stamps, render intent, display scale, and renderer schema version.

## Media Playback Adapter

Use AVFoundation for local and streamed media playback.

Adapter responsibilities:

- Load assets and player items from runtime-resolved URLs or provider references.
- Observe `AVPlayerItem.status`, `AVPlayer.timeControlStatus`, rate changes, failures, buffering, duration, current time, and end-of-item events.
- Implement cue-specific policy: in/out, delay, rate, loop, retrigger, stop behavior, poster frame, scaling/crop, duration, transitions, and playback markers.
- Raise marker events into the shared command executor.
- Report player identity, owning layer/cue/action, readiness, current time, marker state, loop state, dropped/stalled playback, and errors to runtime diagnostics.
- Keep `MediaAsset` identity separate from `MediaCue` usage. The same `AVAsset` may feed multiple cues with different policy.

Use `AVPlayer` for normal single-item playback, `AVQueuePlayer` or `AVPlayerLooper` where cue policy requires queueing or looping, and lower-level sample-buffer or Metal texture paths when the renderer needs custom effects, alpha, synchronized capture, or multi-output composition.

The adapter must not publish empty active media-player sets or clear playback locally. Clear and recovery actions are runtime commands.

## Audio Routing Adapter

The shared model separates visual output from audio routing. The macOS audio adapter should translate shared audio route records into platform capabilities.

Implementation guidance:

- Use AVFoundation/AVFAudio and Core Audio for playback, output device selection, channel mapping, delay, mute/solo, test tones, and device health.
- Represent aggregate devices, HDMI/DisplayPort audio, USB interfaces, virtual devices, and transport audio as machine-local endpoint capabilities.
- Keep per-cue volume, routing, markers, and stop behavior in runtime-owned cue state.
- Report missing devices, route mismatch, permission failures, output format mismatch, underruns, and recovery options.
- Avoid assuming every visual display has usable audio or that every capture/transport endpoint wants program audio.

## Live Video, Cameras, and Inputs

Use AVFoundation capture sessions for cameras, microphones, and compatible external capture devices. Represent each concrete device as a machine-local binding for a portable logical input.

Adapter responsibilities:

- Discover `AVCaptureDevice` inputs and summarize capabilities for runtime records.
- Request camera and microphone authorization only when the operator configures or activates a feature that needs it.
- Include `NSCameraUsageDescription` and `NSMicrophoneUsageDescription` in `Info.plist`; missing purpose strings can terminate the app when access is requested.
- Use `AVCaptureDevice.authorizationStatus(for:)` and `requestAccess(for:)` to distinguish authorized, denied, restricted, and not-determined states.
- Model Continuity Camera and external devices as device capabilities, not as portable content identities.
- Provide live previews and sample buffers through the renderer adapter where live video nodes require composition.

For screen/window capture, use ScreenCaptureKit for current macOS capabilities. `AVCaptureScreenInput` is available but Apple's documentation directs macOS 12.3 and later apps to ScreenCaptureKit for screen recording. Use ScreenCaptureKit for input-style screen/window capture, diagnostics, operator screen capture, or external capture workflows. Do not use it to re-capture ChurchPresenter's own resolved output as a substitute for renderer/capture frame subscription.

## Capture, Recording, Streaming, and Transports

Capture consumers subscribe to resolved screen frames. They must not fork rendering.

macOS adapter choices:

- Use renderer-produced Metal textures, `CVPixelBuffer`, `CMSampleBuffer`, or `IOSurface` handoff for recording, streaming, Syphon-like local video, and future NDI/SDI integrations.
- Use AVFoundation and VideoToolbox for file recording/encoding where local recording is implemented.
- Use ScreenCaptureKit only when the source is a macOS display/window/application outside the ChurchPresenter render pipeline, or when a diagnostic workflow explicitly needs OS-level capture.
- Use Syphon or similar macOS-only local video transports behind an `OutputEndpoint` adapter. Keep Syphon availability and health as endpoint capability, not portable content.
- Treat NDI, SDI, RTMP, and Resi-like streaming as endpoint or capture-consumer adapters that consume resolved frames and program audio.

Each consumer should report source logical screen, profile, resolution, frame rate, alpha/key mode, audio route, encoder state, dropped frames, destination health, and recovery options.

## File Access, Sandboxing, and Bookmarks

The macOS file-access adapter implements `file-access-and-cache-resilience.md`.

Rules:

- Portable content is the source of truth. Security-scoped bookmarks, resolved URLs, thumbnails, extracted resources, and media previews are machine-local accelerators or bindings.
- Use the App Sandbox for Mac App Store distribution. For Developer ID distribution, keep the sandbox strategy explicit and test both security and media workflows before release.
- Use standard SwiftUI file import/export or AppKit `NSOpenPanel`/`NSSavePanel` for user-selected files and folders.
- Store persisted access with security-scoped URL bookmarks only when the app needs access after relaunch.
- Resolve bookmarks with security scope, handle stale bookmarks by recreating them, call `startAccessingSecurityScopedResource()` before use, and call `stopAccessingSecurityScopedResource()` when done.
- Prefer read-only bookmarks for referenced media and read-write bookmarks only when the selected operation needs mutation.
- Keep bookmark data out of portable packages and sync snapshots unless the user explicitly exports machine-local bindings.
- Classify failures as missing, moved, permission denied, locked, corrupt, unsupported format, stale bookmark, provider offline, credential expired, package conflict, or machine-binding mismatch.

Content roots:

- Store app-owned managed content and caches in the app container or an approved app group container.
- Store church-selected external roots via security-scoped folder bookmarks.
- Store original source identity, resolved path, storage policy, resource stamp, and relink state in the shared resource model.
- Do not traverse content folders directly from SwiftUI lists. Query content services and audit services instead.

## Credentials, Accounts, and Privacy

Use Keychain for credentials, tokens, activation secrets, and integration secrets. Store only references or account state in machine-local settings.

Privacy and entitlement guidance:

- Include purpose strings for camera, microphone, screen capture if required by the selected APIs, network services, Apple Events if needed, and any personal-data access.
- Request permissions at the point of use and feed denied/restricted states into runtime diagnostics.
- Use App Sandbox entitlements for user-selected files, network client/server behavior, camera, microphone, USB, Bluetooth, and app groups only when a feature requires them.
- Avoid broad file access or Full Disk Access assumptions. If an operator grants broader access manually, classify it as machine-local capability state.

## Accessibility and Keyboard Operation

SwiftUI and AppKit provide default accessibility for standard controls, but ChurchPresenter has several custom high-density production surfaces that need explicit work.

Requirements:

- Every command-producing control needs an accessibility label, role, value/state, and hint when the visible label is insufficient.
- Slide cards, media cues, timers, Looks, clear buttons, stage messages, output endpoints, and diagnostics rows must expose selected, live, unavailable, missing, and error states distinctly.
- Custom canvas and output-preview controls should expose meaningful accessibility elements instead of raw visual layers.
- Use AppKit accessibility protocols or `NSAccessibilityElement` for custom `NSView` surfaces where SwiftUI modifiers cannot describe the interaction model.
- Preserve complete keyboard navigation for Show operation, menus, dialogs, output setup, relink workflows, and emergency recovery.
- Support VoiceOver, Voice Control, Switch Control where practical, High Contrast, Reduce Motion, larger text where UI allows, and localization-friendly date/time/timer formatting.

The live output itself may intentionally be non-interactive, but operator previews and stage-layout editors need accessibility metadata because they are control surfaces.

## Signing, Notarization, and Distribution

Support two release channels explicitly:

- Mac App Store: sandboxed, App Store signing, App Review, Mac App Store entitlements.
- Direct distribution: Developer ID signing, Hardened Runtime, notarization, stapling, and a signed DMG, ZIP, or installer package.

Developer ID releases should:

- sign every executable, helper, XPC service, plugin, and framework,
- use the Hardened Runtime,
- avoid `get-task-allow` in release entitlements,
- include secure timestamps,
- use `notarytool` or Xcode's current distribution workflow for notarization,
- staple tickets to distributed artifacts where applicable,
- test Gatekeeper first-launch behavior on a clean machine.

If the app hosts third-party plugins, hardware drivers, or professional video integrations, review Hardened Runtime and library validation entitlements carefully. Keep exceptions narrow and document the feature that requires them.

## Diagnostics and Support Surfaces

The macOS diagnostics surface should display runtime diagnostics plus host adapter facts.

Host-specific diagnostic categories:

- app lifecycle and startup health,
- content root and bookmark resolution,
- cache state and stale-resource invalidation,
- display topology and output window apply status,
- renderer backend, frame timing, color/HDR state, and dropped frames,
- AVPlayer item readiness, time control status, marker state, stalls, and errors,
- capture device authorization and availability,
- ScreenCaptureKit permission/stream errors where used,
- audio route/device health,
- capture/stream/transport encoder and destination health,
- Keychain/account/integration status,
- entitlement, sandbox, signing, and notarization build metadata.

Recovery actions shown by the host should be runtime-provided where they affect product state: clear layer, clear group, remap endpoint, reconnect endpoint, reset player, relink media, rebuild cache, re-extract bundle resources, repair support files, retry capture, stop capture, or resolve package conflicts.

## Testing Strategy

Shared runtime tests should remain platform-independent. macOS tests should cover host application of those contracts.

Unit tests:

- view-model projection from runtime read models,
- command dispatch metadata from menus, keyboard shortcuts, and views,
- bookmark store resolution and stale-bookmark handling with test doubles,
- display mapping reconciliation,
- renderer diff planning,
- media adapter state translation,
- diagnostics mapping from platform errors.

Integration tests:

- startup with missing/denied content root,
- relink and cache rebuild flows,
- display connect/disconnect/remap,
- output window creation and frame apply,
- AVPlayer readiness/failure paths,
- capture authorization states,
- ScreenCaptureKit error mapping where used,
- package/import preview with machine-local binding exclusions.

UI and accessibility tests:

- keyboard-only Show operation,
- menu command availability by focus and selection,
- VoiceOver labels and state for slide/media/output/diagnostics surfaces,
- ContentDialog/sheet equivalents for relink, package preview, output setup, and permissions.

Rendering tests:

- scene-to-layer mapping snapshots,
- transparent/alpha output,
- masks and clear groups,
- stage layout provider rendering,
- thumbnail cache invalidation,
- high node/layer counts on Apple silicon and Intel Macs if supported,
- display scale, color space, EDR/HDR, and FPS diagnostics.

Distribution tests:

- sandboxed file access and bookmarks,
- camera/microphone prompt strings,
- Developer ID signing and notarization,
- clean-machine Gatekeeper launch,
- upgrade preserving app container, machine bindings, bookmarks, and caches.

## Migration Path

The macOS app should be built in phases that match the shared architecture migration direction:

1. Define Swift models/protocols that mirror the shared command, query, scene, frame, diagnostic, and machine-binding contracts.
2. Build the SwiftUI/AppKit shell with read-only query projections and no independent live-state semantics.
3. Implement content root selection, bookmark persistence, cache locations, and diagnostics before media-heavy workflows depend on them.
4. Implement Show workflow command dispatch and resolved-frame previews.
5. Add output window controllers for local display endpoints.
6. Add AVFoundation media playback and live-video adapters with host feedback.
7. Add Core Animation/Metal renderer paths for output, thumbnails, previews, and editor surfaces.
8. Add Output Setup for logical screens, endpoints, Looks, stage layouts, clear groups, capture, and transport bindings.
9. Add capture/record/stream consumers from resolved frames.
10. Add advanced parity features: Syphon-like output, NDI/SDI adapters, alpha key/fill, richer text/effects, masks, dynamic providers, remotes, and device protocols.

At each phase, delete temporary host-specific render or live-state logic instead of layering compatibility around unshipped macOS behavior.

## Open Design Questions

These should be resolved against the shared architecture before implementation locks them in:

- Whether the macOS app shares a compiled runtime core with Windows or reimplements the shared contracts in Swift with schema and behavior conformance tests.
- Which renderer backend is the first production path: Core Animation-first with Metal escape hatches, or Metal-first with Core Animation/AppKit hosting for controls.
- How much direct distribution support is required before Mac App Store constraints are known, especially for sandboxing, network/server control surfaces, plugins, and professional video transports.
- Which macOS versions are supported at launch, because ScreenCaptureKit, Swift Observation, SwiftUI window APIs, and newer display/HDR capabilities vary by macOS release.
- Which third-party transport SDKs are in scope for initial macOS parity: Syphon, NDI, Blackmagic/SDI, Resi-like streaming, Stream Deck, MIDI, and DMX.

## Apple Documentation Topics Consulted

Sosumi was used to inspect current Apple documentation for these implementation areas:

- SwiftUI scenes, `WindowGroup`, menu commands, focused values, AppKit integration, and Observation.
- AppKit `NSScreen`, fullscreen/presentation options, output window behavior, and AppKit accessibility.
- AVFoundation player transport, capture setup, screen input deprecation guidance, camera/microphone authorization, and live capture concepts.
- ScreenCaptureKit shareable content, streams, recording outputs, screenshots, picker, HDR/dynamic range, and stream error categories.
- Core Animation/Metal window and `CAMetalLayer` guidance for performant display surfaces.
- macOS App Sandbox, user-selected files, security-scoped bookmarks, entitlements, and persisted file access.
- Developer ID signing, Hardened Runtime, notarization, stapling, and distribution requirements.
