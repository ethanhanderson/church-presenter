# Windows WinUI Native App Implementation

This document describes how the ChurchPresenter Windows app should implement the platform-agnostic architecture in `docs/architecture/` using modern WinUI 3, Windows App SDK, .NET, and C#.

It is subordinate to the shared architecture. Product semantics for documents, commands, live state, output layers, Looks, stage layouts, packages, caches, diagnostics, and recovery come from:

- `docs/architecture/target-architecture.md`
- `docs/architecture/backend-application.md`
- `docs/architecture/content-management.md`
- `docs/architecture/rendering-engine-replacement.md`
- `docs/architecture/native-hosts.md`
- `docs/architecture/file-access-and-cache-resilience.md`
- `docs/reference/propresenter/features.md`

The Windows app implements native UI, display/window/media/file/accessibility/package integrations below that boundary. It must not fork production semantics into XAML pages, code-behind, view models, output windows, or media controls.

## Source Of Truth

The shared rule remains:

```text
Native intent
  -> command or document mutation
  -> shared application runtime
  -> live session snapshot
  -> audience frames / stage frames / diagnostics
  -> native host adapters and output endpoints
```

For Windows this means:

- WinUI pages and view models express operator intent and host-local presentation state.
- The application/runtime layer owns `LiveCommand`, `ActionBatch`, live-session snapshots, output layer state, Look routing, stage layout state, player semantics, diagnostics, and recovery.
- The content/application services own document mutations, content graph safety, package/import/sync decisions, resource stamps, cache invalidation, and repair workflows.
- Windows adapters own concrete `Window`, `AppWindow`, media player, display, picker, capture, credential, notification, and accessibility APIs, then translate platform feedback into shared capability and health models.

## Microsoft Platform Baseline

The Windows app should target:

- **WinUI 3** as the native desktop UI framework. Microsoft Learn describes WinUI 3 as the modern native Windows desktop UI framework delivered with Windows App SDK, supporting .NET/C# and Fluent controls.
- **Windows App SDK** for WinUI, app lifecycle, windowing, resource management, deployment, notifications, and related Windows desktop APIs.
- **Modern .NET and C#** for the host and application projects, with dependency injection, async I/O, structured logging, nullable reference types, analyzers, and xUnit/FluentAssertions tests.
- **Packaged deployment by default** for production church/operator installs unless an explicit installer strategy chooses unpackaged or packaged-with-external-location.

Implementation should follow current Microsoft Learn pages for:

- WinUI 3 overview: <https://learn.microsoft.com/windows/apps/winui/winui3/>
- Windows App SDK overview: <https://learn.microsoft.com/windows/apps/windows-app-sdk/>
- Windowing overview for WinUI 3 and Windows App SDK: <https://learn.microsoft.com/windows/apps/develop/ui/windowing-overview>
- Windows App SDK app lifecycle: <https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle>
- Media players: <https://learn.microsoft.com/windows/apps/develop/ui/controls/media-playback>
- Accessible Windows apps: <https://learn.microsoft.com/windows/apps/develop/accessibility>
- Windows App SDK deployment overview: <https://learn.microsoft.com/windows/apps/package-and-deploy/deploy-overview>

## Project Organization

Use a layered Windows solution that keeps WinUI dependencies out of the shared application runtime.

Recommended shape:

```text
apps/windows/
  ChurchPresenter/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    ShellViews/
    ShellViewModels/
    OutputHosts/
    Rendering/
    Media/
    Display/
    FileSystem/
    Accessibility/
    Diagnostics/
    Packaging/
  ChurchPresenter.Application/
    Backend/
    Models/
    Services/
  ChurchPresenter.Core/
    Portable document/package/content primitives
tests/
  ChurchPresenter.Core.Tests/
  ChurchPresenter.App.Tests/
```

Boundaries:

- `ChurchPresenter` is the Windows host. It can reference WinUI, Windows App SDK, WinRT, Windows media, app lifecycle, packaged deployment APIs, and Windows-specific adapters.
- `ChurchPresenter.Application` is the product runtime/application layer. It should expose commands, queries, models, services, render contracts, diagnostics, and adapter interfaces without referencing WinUI/XAML.
- `ChurchPresenter.Core` holds portable document/package/content primitives that must remain platform-neutral.
- Tests for runtime semantics belong outside the WinUI host wherever possible. WinUI tests should focus on host application of runtime contracts.

Avoid:

- Putting `Microsoft.UI.Xaml` types into `ChurchPresenter.Application`.
- Letting WinUI view models serialize presentation documents directly.
- Letting output windows inspect presentation files or content roots.
- Creating parallel Windows-only live-state or Look-routing logic.

## Startup And Lifecycle

Use the Windows App SDK desktop lifecycle. The `Application` object enters through `App.OnLaunched`, and Windows App SDK desktop apps are running or not running; they do not use UWP-style suspend/resume.

Startup sequence:

1. Initialize the Windows App SDK runtime according to the chosen packaging model.
2. Build the host service provider and logging pipeline.
3. Create and activate `MainWindow` early so fatal startup delays do not leave the operator with no visible app.
4. Start content/runtime bootstrap asynchronously after the shell exists.
5. Publish bootstrap progress, degraded content state, missing binding state, and repair actions through runtime diagnostics/read models.
6. On `Window.Closed`, persist host-local window/layout state, release native players/capture sessions, flush logs, and ask runtime/application services to stop cleanly.

Guidance:

- Keep activation, file-open, protocol, restart, and future notification handling as command/query entry points into the runtime.
- Do not run display enumeration, media device enumeration, content audits, or cache rebuilds in `MainWindow` constructors.
- Treat background startup failures as degraded diagnostics unless the runtime cannot safely open the workspace.
- Persist machine-local shell state separately from portable content/support files.

## Shell, Pages, And View Models

WinUI should provide a native shell for the workflow families described in `native-hosts.md`:

- Show
- Editor
- Reflow
- Themes
- Output Setup
- Settings and Support

The shell can use WinUI navigation, title bar customization, `CommandBar`, `NavigationView`, dialogs, flyouts, teaching tips, split panes, tab-like selectors, and native controls. Prefer stock WinUI controls and Fluent styling before custom chrome.

Host-side state may include:

- selected operator item,
- page filters,
- expanded panes,
- editor focus and selection adorners,
- active dialog/flyout state,
- local layout density,
- pending validation display state,
- optimistic UI affordances that are reconciled with runtime results.

Host-side state must not include:

- authoritative live layer state,
- authoritative player state,
- Look routing,
- clear-group expansion,
- package/sync conflict decisions,
- media relink truth,
- stage layout resolution.

Command dispatch:

- Slide clicks, media cue clicks, keyboard shortcuts, macro buttons, clear buttons, output route changes, stage commands, and capture commands should create typed commands or document-service requests.
- Every command should include source metadata, target scope, correlation id, and preview/validation intent when relevant.
- View models should subscribe to runtime read models for selected item, live layer state, active Look, stage assignments, output health, capture health, player state, and diagnostics.

## Windowing And Output Hosts

Microsoft Learn describes WinUI windowing as a combination of `Microsoft.UI.Xaml.Window` and `Microsoft.UI.Windowing.AppWindow`, both based on the Win32 `HWND` model. Starting with current Windows App SDK versions, a WinUI `Window` exposes its associated `AppWindow`.

Use this split intentionally:

- `Window` owns XAML content and window lifecycle events.
- `AppWindow` owns top-level window management such as presenter, position, size, title bar, icons, visibility, and display placement.
- `DisplayArea` and related windowing APIs translate monitor topology into Windows capabilities for machine-local endpoint bindings.

Windows output host responsibilities:

- Create one native output window per local display endpoint or placeholder preview endpoint.
- Bind a shared `OutputScreen` to a concrete Windows display endpoint through machine-local `ScreenMapping` data.
- Position/size output windows using `AppWindow` in physical device pixels, while XAML content continues to use effective pixels.
- Apply fullscreen output using `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)` where appropriate.
- Track `AppWindow.Changed`, `Window.Activated`, `Window.Closed`, and display topology changes to report endpoint health.
- Keep output windows lightweight and driven by resolved `AudienceFrame` or `StageFrame` input.

Output host restrictions:

- Do not compute Look routing in the output window.
- Do not select layers locally.
- Do not clear layers from output host state.
- Do not read presentation documents or media folders directly.
- Do not derive stage layouts from audience screens.
- Do not create a separate capture render path.

Display binding model:

- Portable content stores logical screens, Looks, stage layouts, masks, and capture defaults.
- Machine-local Windows state stores monitor ids, display coordinates, selected Windows endpoint bindings, refresh/capability cache, output window positions, and local repair hints.
- If a monitor disappears, preserve the logical screen and mark the endpoint binding missing/degraded. Do not delete portable output configuration.

## Rendering Host Adapters

The Windows app applies immutable scenes and frames emitted by the shared runtime.

Adapter families:

- `WinUiAudienceOutputAdapter`
- `WinUiStageOutputAdapter`
- `WinUiOperatorPreviewAdapter`
- `WinUiThumbnailAdapter`
- `WinUiEditorCanvasAdapter`
- `WinUiScreenPreviewAdapter`
- `WinUiDiagnosticsSnapshotAdapter`

These names are illustrative; the important boundary is that each adapter consumes shared scene/frame contracts and emits host diagnostics.

Adapter responsibilities:

- Diff incoming `AudienceFrame` and `StageFrame` records.
- Create/update WinUI visuals, composition resources, text elements, media surfaces, brushes, and transitions.
- Preserve stable native visual caches only while their source scene/resource stamps remain valid.
- Apply resolved transition/build descriptors.
- Report host apply status, timing, unsupported effect diagnostics, missing resources, XAML/media failures, and recovery hints.
- Marshal UI-thread work through the correct dispatcher/queue.

Adapter restrictions:

- Do not mutate documents.
- Do not interpret command semantics.
- Do not decide what is live.
- Do not resolve dynamic text providers.
- Do not resolve Looks or clear groups.
- Do not crawl content roots directly.

Rendering implementation notes:

- Text, shape, media, live video, web, vector, group, and dynamic text scene nodes should map to WinUI/Composition primitives through an adapter layer, not direct document-to-XAML conversion.
- Editor adorners such as selection handles, rotation handles, rulers, guides, snapping, and validation overlays are host-only state and never serialize into documents or output frames.
- Thumbnails and previews should be constrained render intents with cache keys derived from content/resource/theme/token stamps.
- Unsupported imported effects should remain metadata plus diagnostics rather than being flattened away by the Windows host.

## Media Playback And Capture Adapters

Use Windows media APIs as host bindings for shared media/audio/live-video contracts.

Microsoft Learn identifies `MediaPlayerElement` as the WinUI control that uses `MediaPlayer` to render audio and video, with `MediaSource`, `MediaPlaybackSession`, transport controls, poster source, stretch policies, display requests, and playback events. These APIs should be wrapped behind ChurchPresenter adapters.

Windows media adapter responsibilities:

- Own concrete `MediaPlayer`, `MediaPlayerElement`, `MediaSource`, `MediaPlaybackItem`, and `MediaPlaybackList` instances.
- Translate `MediaCue` and `AudioCue` payloads into platform playback setup.
- Apply cue-specific fit/crop/stretch, in/out, loop, rate, delay, duration, volume, marker, poster, and transition settings where supported.
- Report playback state, position, duration, buffering, natural video size, failures, dropped frames where available, and media capability diagnostics to the runtime.
- Raise marker/progress events back into the runtime as command-source events, not direct UI mutations.
- Release or reuse players according to shared player identity and resource-stamp rules.

Media adapter restrictions:

- Do not treat a clicked file path as a cue by itself.
- Do not decide whether media is foreground/background or slide-linked.
- Do not update live state directly when a playback event occurs.
- Do not use `MediaPlayerElement` transport controls as authoritative player state for live production.

File/media access:

- Prefer user-mediated `FileOpenPicker`/folder picker flows for importing or relinking external media when broad file capabilities are unnecessary.
- If packaged app capabilities are required for unattended library access, keep those capabilities minimal and document the product reason.
- Local file paths, OneDrive placeholders, removable drives, network shares, and locked files must flow through resource stamps and availability diagnostics.

Live video and capture:

- Camera/capture device access should be exposed as logical `LiveVideoInput` records with machine-local Windows device ids and capability snapshots.
- Preview/capture adapters should report device unavailable, permission denied, unsupported mode, driver failure, and lost-device states.
- Recording/streaming/NDI/SDI/future transport adapters should consume resolved screen frames and report endpoint/capture health. They must not fork a separate document render pipeline.

## File Access, Resources, And Caches

Follow `file-access-and-cache-resilience.md`:

```text
Portable content is the source of truth.
Caches are accelerators.
Machine-local bindings adapt portable content to one computer.
```

Windows host responsibilities:

- Provide file/folder pickers, drag/drop import UX, permission prompts, shell thumbnails where appropriate, and Windows-specific path normalization.
- Translate platform failures into shared categories: missing, moved, permission denied, locked/in use, corrupt, unsupported format, provider offline, credential expired, stale cache, package conflict, and machine-binding mismatch.
- Store machine-local file access state separately from portable content.
- Present runtime repair actions such as relink asset, choose replacement, rebuild cache, refresh provider data, re-extract bundle resources, skip unavailable asset, preview package missing dependency, repair manifest/reference, clear stale prepared cue, and reset machine-local binding.

Windows host restrictions:

- Do not make shell thumbnails, extracted bundle files, media previews, or native file handles authoritative.
- Do not invalidate caches from path strings alone. Use resource stamps.
- Do not delete or rewrite portable references because a Windows path is temporarily unavailable.
- Do not tear down the current live frame because a future asset is missing.

Cache locations should be machine-local and versioned. Cache entries should record owner id, resource stamp, cache schema version, invalidation policy, build time, and rebuild path.

## Accessibility, Input, And Localization

Microsoft Learn defines three core accessibility pillars for Windows apps:

- programmatic access through names, roles, and values,
- keyboard navigation,
- color and contrast.

Apply these as implementation requirements:

- Use built-in WinUI controls where possible because they provide standard UI Automation patterns, focus visuals, keyboard behavior, theme support, and high-contrast integration.
- Every command surface needs an accessible name, role, state, keyboard activation path, and visible focus behavior.
- Custom slide cards, media cards, output previews, editor canvas objects, transport controls, and stage layout cells need UI Automation peers or equivalent accessible metadata when built-in controls do not expose enough.
- Support logical tab order and arrow-key navigation in grids, slide decks, playlists, media bins, output setup, and editor object selection.
- Provide access keys/keyboard shortcuts for core live-operation commands without bypassing the runtime command path.
- Use theme resources and resource dictionaries for light, dark, and contrast themes; do not hardcode contrast-critical colors.
- Treat text scaling, high DPI, display scaling, touch, pen, mouse, keyboard, and screen-reader use as first-class host concerns.
- Localize operator UI strings, shortcut labels, diagnostics, validation messages, date/time formats, and copyright/generated-content display strings.

Testing should include:

- keyboard-only operation for Show, output clear/recovery, media bin, and output setup,
- Narrator and Accessibility Insights checks for core workflows,
- high contrast and text scaling checks,
- focus restoration after dialogs/flyouts,
- custom automation peer coverage for non-standard controls.

## Packaging, Deployment, And Updates

The deployment model is a product/distribution choice, but it must be explicit.

Recommended default:

- Use a packaged WinUI 3 / Windows App SDK app for production church machines.
- Keep portable content outside the app install directory.
- Keep machine-local bindings, caches, logs, and diagnostics in appropriate app data locations.
- Use MSIX/installer/update mechanisms that preserve content roots and machine bindings across app updates.

Windows App SDK deployment choices:

- **Framework-dependent** deployment is the default and keeps the app smaller and serviceable through the Windows App SDK runtime/framework package.
- **Self-contained** deployment carries Windows App SDK dependencies with the app, increasing size but controlling the SDK version.
- **Unpackaged or packaged-with-external-location** installs require Windows App SDK runtime initialization and may require the bootstrapper/runtime installer strategy documented by Microsoft.

Deployment requirements:

- Document the supported CPU architectures, minimum Windows version, Windows App SDK version, .NET version, installer/update path, and rollback story.
- Declare only required app capabilities.
- Separate app binary updates from content migrations. Content migrations should preview changes and recovery paths through application services.
- Support diagnostic package export that includes logs, host/app versions, Windows version, display topology, endpoint health, adapter status, content audit summaries, and redacted settings.
- Activation/licensing/account state must remain separate from local document authoring and read-only show preparation.

## Diagnostics And Recovery

The Windows app should surface runtime-provided diagnostics and add host adapter diagnostics.

Runtime diagnostics cover:

- command acceptance/rejection,
- source provenance,
- active layers,
- selected/live distinctions,
- Look routing,
- stage assignments,
- output/frame resolution,
- missing media/resources,
- timers/messages/props/macros,
- package/sync conflicts,
- recovery suggestions.

Windows host diagnostics add:

- WinUI window lifecycle and output-window apply status,
- display topology and monitor binding state,
- `AppWindow` placement/presenter failures,
- XAML/resource/theme failures,
- media player state and failures,
- camera/capture/device failures,
- file picker/import/relink failures,
- packaged deployment/runtime initialization state,
- accessibility validation issues where available,
- UI thread responsiveness and render/apply timing.

Recovery actions should remain command-driven or service-driven:

- clear layer,
- clear configured group,
- recover cleared payload where supported,
- reconnect/remap endpoint,
- reset player,
- relink media,
- resync host,
- repair support-file mismatch,
- retry/stop capture,
- rebuild cache,
- resolve package conflict.

The host can present buttons, dialogs, notifications, and progress UI for these actions, but the action definition and graph effect must come from the runtime/application services.

## Testing And Verification

Testing follows the boundary in `backend-application.md`, `rendering-engine-replacement.md`, and `native-hosts.md`.

Shared/runtime tests:

- command expansion and action ordering,
- live-session mutation,
- clear layers and clear groups,
- Look routing,
- stage-only behavior,
- timer/message/prop state,
- macro expansion,
- cue resolution,
- endpoint mapping,
- package/sync conflicts,
- provenance and diagnostics,
- cache invalidation and recovery suggestions.

Windows host tests:

- command dispatch from WinUI pages/view models,
- frame application into output/preview/editor adapters,
- output window creation, fullscreen, close/reopen, and monitor-remap behavior,
- media player setup, playback-state feedback, cue transitions, marker events, and error reporting,
- file picker/import/relink UX around runtime repair operations,
- accessibility, keyboard navigation, focus restoration, and high contrast,
- packaged startup, activation, update/rollback, and content migration entry points,
- host diagnostics and support-package export.

Manual verification should include:

- launching the packaged app and confirming a visible shell,
- opening a content root with missing and healthy media,
- taking slides/media live through commands,
- switching Looks and stage layouts,
- opening/closing/remapping output windows across monitors,
- clearing individual layers and configured clear groups,
- validating that a missing future asset does not tear down the current live output,
- collecting diagnostics after a simulated host/media/display failure.

## Migration Path

The current Windows app should migrate toward the shared architecture in phases:

1. **Freeze shared contracts** for commands, live-session snapshots, scene snapshots, layer payloads, audience/stage frames, diagnostics, resource stamps, and adapter interfaces.
2. **Isolate host state** by moving authoritative live/output/media state out of WinUI view models and into application services/read models.
3. **Route all live actions through commands** including slide activation, media/audio triggers, clear buttons, Look changes, stage layout changes, macros, timers, capture, and keyboard shortcuts.
4. **Introduce Windows host adapters** for output windows, previews, thumbnails, editor canvas, media playback, display binding, file access, diagnostics, and packaging.
5. **Replace host-specific render logic** with scene/frame consumption behind existing UI surfaces.
6. **Move output routing and clear behavior fully into runtime** so output windows only apply resolved frames.
7. **Expand platform parity** for live video, capture/streaming, NDI/SDI adapters where available, stage dashboards, dynamic text providers, generated overlays, masks, props, messages, and automation devices.
8. **Retire duplicate legacy surfaces** after runtime read models and adapters cover the feature path.

Do not preserve compatibility with unshipped in-branch host shortcuts that conflict with the shared contracts. Replace them with the shared command/query/adapter path.

## Open Implementation Questions

These are Windows-specific questions that should be resolved as implementation proceeds:

- Which Windows App SDK and .NET versions are the supported production baseline for the next milestone?
- Is production deployment packaged MSIX, packaged with external location, self-contained, framework-dependent, or installer-managed?
- Which Windows capture/transport adapters are in scope first: local display only, recording, RTMP, NDI, SDI/Blackmagic, camera inputs, or audio routing?
- What display identity strategy will be used for stable monitor bindings across driver/GPU/topology changes?
- Which accessibility scenarios require custom automation peers beyond stock WinUI controls?
- What diagnostic package redaction policy applies to content paths, account state, credentials, and logs?
