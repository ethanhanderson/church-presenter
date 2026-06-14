# Windows WinUI Native App Implementation

This document describes how the ChurchPresenter Windows app should implement the platform-agnostic architecture in `docs/architecture/` using modern WinUI 3, Windows App SDK, .NET, and C#.

It is subordinate to the shared architecture. Product semantics for documents, commands, live state, output layers, Looks, stage layouts, packages, caches, diagnostics, and recovery come from:

- `docs/architecture/target-architecture.md`
- `docs/architecture/backend-application.md`
- `docs/architecture/content-management.md`
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
- WinUI data binding: <https://learn.microsoft.com/windows/apps/develop/data-binding/data-binding-in-depth>
- Media players: <https://learn.microsoft.com/windows/apps/develop/ui/controls/media-playback>
- Accessible Windows apps: <https://learn.microsoft.com/windows/apps/develop/accessibility>
- Windows App SDK deployment overview: <https://learn.microsoft.com/windows/apps/package-and-deploy/deploy-overview>

### Repository toolchain (pinned)

The Windows solution in `apps/windows/` currently targets:

| Setting | Value |
| --- | --- |
| .NET SDK | **10.0.301** (`global.json`, `rollForward: latestFeature`) |
| WinUI host TFM | `net10.0-windows10.0.26100.0` |
| Library / test TFMs | `net10.0` |
| Minimum supported Windows | **10.0.17763.0** (`TargetPlatformMinVersion`) |
| Windows App SDK | **2.2.0** (`Microsoft.WindowsAppSDK`; WinUI 3 via `UseWinUI`) |
| Windows SDK build tools | `Microsoft.Windows.SDK.BuildTools` **10.0.28000.1839** |
| Packaged run helper | `Microsoft.Windows.SDK.BuildTools.WinApp` **0.3.2** |

Repo-wide build configuration is limited to **`global.json`** (SDK pin) and **`.editorconfig`** (style/analyzers). Project-specific MSBuild settings live in each `*.csproj`. Do not add repo-level `Directory.Build.props` unless a cross-cutting MSBuild property is genuinely required.

WinUI project settings should follow the current **WinUI Blank App** template (`dotnet new wasdk-single`): auto-detected `RuntimeIdentifier`, conditional `PublishProfile`, `EnableMsixTooling`, and the three-package Windows App SDK stack above. Avoid legacy workarounds such as custom AppX sync targets unless a packaged-build regression requires them.

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

### Top-level navigation and page state model

Top-level workspaces are selected from a title-bar `SelectorBar` and routed through a single navigation service that owns one content `Frame` for all top-level routes, following the Microsoft `SelectorBar` + `Frame` + `NavigationTransitionInfo` pattern.

**Show is cached in the content frame.** `ShowPage` uses `NavigationCacheMode.Required` so the same instance reloads via `Loaded`/`Unloaded` when the operator returns to the Show tab. Editor, Reflow, Themes, and Settings are fully transient (`NavigationCacheMode.Disabled`). Leaving Show calls `ShowPage.TeardownForNavigation` once from `NavigationService` before `NavigateToType` (not again from `OnNavigatedFrom`).

**Render-engine scoped stability (cached Show reload):** canonical program output lives on the app-lifetime `ProgramRenderService` anchor (`ProgramRenderAnchor` on `MainWindow`). Each logical screen renders at its topology `RenderSize` (native monitor pixels when mapped to a connected display). Slide scenes compile at presentation size; `SlideSceneFitter` + `SceneSurfaceRenderer` scale and rasterize them into the screen slot so text stays vector-sharp on high-DPI outputs; media/video layers fill the slot per payload fit. The operator preview is **not** bound to the cached Show page: `OperatorPreviewSession` owns a `ScreenMirrorBrush` (`UsePreviewSurface = true`) on a shell `Canvas` overlay (`OperatorPreviewOverlayHost`), painting a **preview-sized** `LayerStackHost` (`GetPreviewSurface`) positioned over the Show preview slot while Show is visible; teardown hides the overlay and releases the preview host — the brush stays connected. Audience/stage output windows mirror the canonical native-resolution surface via `GetScreenSurface`. Slide deck thumbnails use app-lifetime `ISlideDeckThumbnailCache` + offscreen `ThumbnailEngine` (Win2D via `SceneSurfaceRenderer`, same rasterizer as live output). The Show deck **never** starts GPU raster from `ItemsRepeater` property getters. `IShowDeckThumbnailService` orchestrates apply/warm: **Blocked** during Show leave/reload transition (`BeginShowNavigationTeardown`), **Returning** on cached revisit `Loaded` (navigation pixel snapshots restore immediately), **OnFrameReady** progressive warm after compositor settle (`MarkShowOnFrameCompositorReady`), **OffFrame** warm after navigating away (`MarkShowOffFrame` from shell `Navigated`). Cache hits bind immediately; misses raster sequentially on the UI thread at low priority.

**Page transitions (Microsoft Learn horizontal slide):** sibling SelectorBar routes use native `SlideNavigationTransitionInfo` on `ContentFrame.NavigateToType` with `IsNavigationStackEnabled = false` (the [SelectorBar navigate-with-transitions](https://learn.microsoft.com/windows/apps/develop/ui/controls/selector-bar#navigate-with-transition-animations) pattern). `IShellNavigationCoordinator` compares SelectorBar indices for slide direction. Cached `ShowPage` reload uses the same slides; compositor safety is enforced by `IShowRenderSession` phase gating (deck warm only after `Unloaded`, preview attach only after compositor settle), not by suppressing Show transitions. Nested Settings hub/detail navigation uses the same slide pattern via `SettingsNavigation`.

Transient pages tear CPU preview work via `NavigationTeardown.TeardownCompositionSurfaces` on `OnNavigatedFrom`.

Implementation: `Hosting/ShellNavigation.cs` (routes, transitions, nested-frame back), `Hosting/ShellNavigationCoordinator.cs`, `Hosting/NavigationService.cs`, `Views/Show/ShowPage`, `Adapters/Rendering/Pipeline/OperatorPreviewSession.cs`, `Services/Show/SlideDeckThumbnailCache.cs`, `Hosting/ShowRenderSession.cs`, `Views/OutputHosts/ProgramOutputSurface`, `Views/Settings/SettingsNavigation`, and `NavigationTeardown` for editor/theme/reflow CPU surfaces.

### Show workflow and functional subsystems

Show is the operator's live-production workspace. Unlike Editor, Reflow, Themes, and Settings, it is the **only** cached top-level page and the deepest integration point between WinUI chrome and the unified cue pipeline.

**Functional subsystems** (current implementation):

- **Sidebar** — libraries and playlists `NavigationView` tree with persistent section headers, search (hidden until content exists), inline rename, and drag/drop for reorder/import. Empty sections use labels, not disabled nav items.
- **Slide deck** — `ItemsRepeater` slide cards with independent **selection chrome** (Ctrl/shift click, right-click) and **live chrome** (green ring from active slide-layer program payload). Normal click takes the slide live on the slide output layer without selecting. Presentations may have zero slides; new blanks start transparent with default text style only.
- **Browse stack** — multi-presentation horizontal browse with section headers, auto-advance flyouts, and deck-per-presentation context.
- **Transition toolbar** — global slide/media duration sliders (100 ms steps), per-item overrides, favorites/recents; precedence: per-slide → presentation-level (when set) → global toolbar; media uses per-item override else global fallback.
- **Media drawer** — bottom panel; **click** sends media to the **media output layer**; **drag** onto a slide attaches or creates slide-linked media actions. Cards appear from read models before thumbnails finish.
- **Output preview and clears** — `ProgramOutputSurface` is the Show preview **layout slot** only; the actual mirror is `OperatorPreviewSession` on the shell overlay, rasterized at the preview card size (`EnsurePreviewSurface` / `GetPreviewSurface`), not a downscaled copy of the full monitor render. Clear actions target **slide** vs **media** layers (not presentation vs media); mask is routable from Looks but is not an output-preview clear target. Same-media replays reuse the existing player; take-live media loads before the enter transition.
- **Show Controls panel** — audio/stage/timers/messages/props surface (`ShowControlsPanelView`).
- **Arrangements** — arrangement picker dialogs and deck grouping palette.
- **Deck thumbnails** — `IShowDeckThumbnailService` + `ISlideDeckThumbnailCache` + `ThumbnailEngine`; navigation pixel snapshots restore on revisit `Loaded`; compositor bind deferred to `OnFrameReady`.
- **Workspace persistence** — favorites, recents, toolbar transitions, and pane sizes via `SettingsService` / `IWorkspaceService` / `ShowSessionCache`.

**Runtime contracts (Show-specific):**

| Contract | Role |
|---|---|
| `IShowRenderSession` | Gates `ThumbnailRasterPhase` (`Blocked` → `Returning` → `OnFrameReady` → `OffFrame`) and `OperatorPreviewAttachAllowed` during cached Show reload |
| `IShowDeckThumbnailService` | Orchestrates thumbnail apply/warm, navigation teardown, off-frame prefetch, compositor-ready progressive warm |
| `OperatorPreviewSession` | Shell-overlay `ScreenMirrorBrush` (`UsePreviewSurface`); preview-sized render at card layout pixels; teardown hides overlay and releases preview host |
| `ProgramRenderService` | Canonical per-screen native-resolution render + optional preview-sized hosts; audience/stage windows use `GetScreenSurface` — not page-owned |
| `NavigationService.TeardownForNavigation` | Single proactive teardown when leaving Show (preview hide, thumbnail cancel) |
| `ICueDispatcher` | Entry point for take-live, clear, media, timer, and transport commands from Show UI |
| `ILiveSessionQueryService` | Live-session snapshots for clear-button active state, live rings, transport read models |
| `CuePipelineRuntime` | Application singleton orchestrating bus, preparation, routing, and host apply — **not** called from WinUI |

**Pipeline integration:** Show view models and services express intent (`ICueDispatcher.Dispatch…`, document/browse services for edits). `CuePipelineRuntime` owns production truth; `LiveSessionQueryService` projects deduplicated snapshots back to WinUI on the `DispatcherQueue`. Show must not fork layer state, player state, or clear semantics locally.

**Organization (current vs target):** Today Show is concentrated in `Views/Show/ShowPage` (~7.7k lines code-behind total), a singleton `ShowViewModel` (~4.4k lines), dual service injection in page + VM, and models scattered under `Views/Show`. Target segmentation, file-placement rules, and a four-phase migration plan are in `docs/architecture/platforms/winui-code-organization.md` ("Show feature organization").

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

Show live-status controls:

- Clear buttons should be stable `AppBarButton`/button elements bound to stable item view models. Do not keep parallel active/inactive buttons and switch `Visibility` to represent the same action.
- Bind visible active state to observable item properties derived from `LiveSessionQuerySnapshot.ActiveLayers`; for example, an item-level `IsActive`, `Background`, `Foreground`, or visual-state property. Microsoft Learn's WinUI data-binding guidance requires bound source objects to be observable, typically through `INotifyPropertyChanged`, for one-way runtime updates.
- Use `ObservableCollection<T>` membership changes only for actual action-list changes: a clear group was added, removed, renamed, reordered, retinted, or retargeted. When the same action remains available and only live layer status changes, update the existing item object and raise property notifications.
- Runtime query services should suppress equivalent live-session projections before notifying WinUI view models. A routing refresh, host feedback tick, or first media-player readiness event should not make clear buttons visually refresh unless the target layer's live-content status changed.
- Marshal query updates to the WinUI thread through `DispatcherQueue`, but keep the semantic comparison and command handling in the application/runtime layer.

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
- Keep a black root XAML/background surface behind the composition host for opaque local-display outputs. This root surface remains visible when layer slots clear or transition away; content layers may be transparent, but the local display window itself should not reveal the Windows desktop unless an explicit alpha/key endpoint mode is active.
- Treat the monitor-poll timer and display topology reconciliation as idempotent. If the existing window already hosts the same `ScreenId`, `EndpointId`, monitor bounds, and fullscreen presenter state, skip host reconfiguration, `Content` reattachment, presenter resets, and media-surface rebinding.

Output host restrictions:

- Do not compute Look routing in the output window.
- Do not select layers locally.
- Do not clear layers from output host state.
- Do not read presentation documents or media folders directly.
- Do not derive stage layouts from audience screens.
- Do not create a separate capture render path.
- Do not use `AppWindow.Show`, `MoveAndResize`, focus restoration, or unchanged monitor-poll ticks as reasons to reapply equivalent layer frames.

Display binding model:

- Portable content stores logical screens, Looks, stage layouts, masks, and capture defaults.
- Machine-local Windows state stores monitor ids, display coordinates, selected Windows endpoint bindings, refresh/capability cache, output window positions, and local repair hints.
- If a monitor disappears, preserve the logical screen and mark the endpoint binding missing/degraded. Do not delete portable output configuration.

### Audience Window Lifecycle Decision

The Windows audience window should use a stable `Window`/`AppWindow` wrapper around one output host page or renderer root. The first show attaches the output content, configures the logical screen/endpoint identity, positions the `AppWindow`, and switches to fullscreen presentation. Later monitor-poll ticks should only validate that the endpoint still exists. They should not call the same initialization path again when nothing changed.

This is especially important for media output. Reattaching the XAML output page can cause the composition host to rebuild layer slots; re-showing/reconfiguring the host can also lead to equivalent `AudienceFrame` application and repeated `MediaPlayerElement.SetMediaPlayer(...)` calls. The correct behavior is that a playing media-layer payload keeps the same runtime `MediaPlayer` identity and visible surface while slide-layer refreshes, focus restoration, or display health checks occur around it.

## Rendering Host Adapters

The Windows app applies immutable scenes and frames emitted by the shared runtime through the **unified output pipeline** (see `docs/architecture/target-architecture.md` and `docs/architecture/backend-application.md`). All audience and stage rendering on Windows binds the pipeline's host adapter contract to WinUI, Composition, Win2D, and Windows.Media.Playback primitives.

**Single canonical render + mirrors.** There is exactly one **native-resolution** render per logical screen. The app-lifetime `ProgramRenderService` (DI singleton) owns one `LayerStackHost` (a `Microsoft.UI.Composition` layer-stack with Win2D surfaces and `MediaPlayer`-backed video) per screen at the screen's topology `RenderSize`, parented to an always-composing, opacity-0 anchor in `MainWindow` so the compositor keeps ticking it regardless of which page is shown. The service implements **both** the platform-agnostic `IAudienceHostAdapter` and `IStageHostAdapter` apply contracts, so the pipeline applies each screen's frame to the canonical host (and to an optional preview host when the operator card is visible). Audience and stage output windows are passive `MirrorSurfaceView` controls: a plain XAML `Rectangle` filled with a `ScreenMirrorBrush` that wraps the screen's canonical `CompositionVisualSurface` from `IProgramRenderService.GetScreenSurface` in a `CompositionSurfaceBrush` with `CompositionStretch.Fill` for 1:1 pixel mapping inside the output `Viewbox`. The framework-managed brush is the Microsoft-recommended way to host Composition content in XAML — its `OnConnected`/`OnDisconnected` coordinate the brush's lifetime with the render thread, so a consumer owns no app-created `CompositionObject` of its own (no `SetElementChildVisual` hand-in visual, no custom clip). One compositor per UI thread makes a surface created on the main thread paintable in any window on that thread. This means:

- audience and stage windows show the canonical native-resolution pixels for the mapped monitor (including video, transitions, clears, and mask);
- the operator preview applies the **same frame and media bindings** but rasterizes at the preview card's layout size via `GetPreviewSurface` / `EnsurePreviewSurface` — it does not downscale the 4K canonical surface into the card;
- re-pointing the preview at a different audience or stage screen is an instant brush swap (`SetScreen`), never a re-attach of page-owned composition;
- the operator preview is purely a host-side "view of a screen" — it is **not** part of the backend host-adapter contract (there is no preview adapter and no separate preview apply in the pipeline bus);
- the canonical render is always on, so the preview works even when the audience/stage output windows are off. The `audienceEnabled`/`stageEnabled` flags gate only whether the output *windows* are shown (`OutputWindowService`) and how feedback visibility is reported — not whether the render runs.

**Screen-native slide scaling.** `SlideSceneCompiler` emits scenes at presentation slide size. `SlideSceneFitter` (shared application layer) uniformly scales scene coordinates, typography, and stroke widths into each screen's `RenderSize` before `SceneSurfaceRenderer` rasterizes via Win2D, with letterboxing when aspect ratios differ. `LayerStackHost` applies the fitted scene directly at slot size. Media and live-video layers use composition surface brushes with fill/cover fit so undersized sources still cover the screen.

Other render surfaces (`WinUiThumbnailAdapter`, `WinUiEditorCanvasAdapter`, reflow/screen previews, diagnostics snapshots) consume the same shared scene/frame contracts but render CPU previews (`SceneCanvasView` / `SoftwareBitmapSource`) and are independent of the canonical output render. Adapter names are illustrative; the boundary that matters is that each consumes shared scene/frame contracts and emits host diagnostics.

Adapter responsibilities:

- Diff incoming `AudienceFrame` and `StageFrame` records per layer slot; unaffected slots must not re-render.
- Create/update WinUI visuals, composition resources, text elements, media surfaces, brushes, and transitions.
- Preserve stable native visual caches only while their source scene/resource stamps remain valid.
- Apply resolved transition/build descriptors using the Composition animation system.
- Report host apply status, timing, unsupported effect diagnostics, missing resources, XAML/media failures, and recovery hints.
- Marshal UI-thread work through the host `DispatcherQueue`; never block the runtime command bus thread on host work.

Adapter restrictions:

- Do not mutate documents.
- Do not interpret command semantics.
- Do not decide what is live.
- Do not resolve dynamic text providers.
- Do not resolve Looks or clear groups.
- Do not crawl content roots directly.

### Composition and Surface Mapping

The unified pipeline expects persistent per-layer host surfaces per logical screen. On Windows this maps to Microsoft.UI.Composition primitives wrapped by an `ElementCompositionPreview`-hosted root visual:

- One `ContainerVisual` per logical screen, mounted into the audience or stage `Window`'s root `UIElement` via `ElementCompositionPreview.SetElementChildVisual`.
- One child `ContainerVisual` per fixed layer slot (Audio diagnostics overlay, Messages, Props, Announcements, Slide, Media, Live Video, Mask), created on `EnsureSurfaces` and reused for the window's lifetime.
- Per-slot transitions use a hidden back `SpriteVisual` and a `Compositor` scalar/Vector3 animation, swapping the front/back roles when the new payload's first frame is ready.
- The Mask slot is applied as a `CompositionMaskBrush` (or `CompositionVisualSurface` over a vector mask layer) constraining the composed audience tree per screen, never as a separate top-of-stack visual.
- Vector text/shapes use Win2D painted into a `CompositionSurfaceBrush` at the slot's native `RenderSize`. `SlideSceneFitter` scales presentation-sized scenes into the slot before rasterizing so typography is not bitmap-upscaled on 4K outputs.

Frame applies always run on the UI thread via `DispatcherQueue.TryEnqueue`; the runtime command bus thread only enqueues diff descriptors.

### Media Slot Mapping

Media and live-video slots bind runtime player handles to Composition surfaces:

- A `Windows.Media.Playback.MediaPlayer` is created per active player, configured with `IsVideoFrameServerEnabled = false` when painting into Composition via `MediaPlayerElement` proxies, or with frame server when feeding a `CompositionSurfaceBrush` directly.
- Microsoft Learn documents `MediaPlayerElement` as the WinUI control that renders audio/video through an underlying `MediaPlayer` and exposes playback through `MediaPlaybackSession`. ChurchPresenter should treat that platform player as a host binding for a runtime-owned slot/player identity, not as an independent source of live state.
- For each logical screen/layer, audience output owns the primary media slot binding. Operator previews reuse the same runtime player/frame identity via the preview `LayerStackHost` apply path; they must not call the media-binding path to create a second muted `MediaPlayer` for the same screen/layer, because that creates divergent pause/seek/progress state.
- When a `MediaPlayerElement` proxy is used for both output and preview, use the documented player attachment APIs consistently and report any platform side effects through host diagnostics. If the platform cannot safely present one `MediaPlayer` through multiple XAML surfaces, use frame-server or composition-texture fan-out while keeping one semantic transport state.
- Looping cues use `MediaPlaybackList` with `AutoRepeatEnabled = true` and a non-zero `MaxPlayedItemsToKeepOpen` so the loop boundary does not reopen the decoder. The runtime never asks the host to seek to zero for looping.
- First-frame visibility is gated on `MediaPlaybackItem.OpenOperationCompleted` followed by `MediaPlaybackSession.PlaybackStateChanged == Playing`. Until both fire, the prior payload remains visible.
- Audio routing flows through `MediaPlayer.AudioCategory` and `AudioDeviceType`; multi-output routing uses one `MediaPlayer` per audio endpoint controlled by the runtime's audio routing snapshot.

### Resource and Cue Cache Mapping

The runtime's `PreparedCueCache` and tiered resource cache (see `file-access-and-cache-resilience.md`) are backed on Windows by `Microsoft.Extensions.Caching.Memory.IMemoryCache` instances with size-limited `MemoryCacheEntryOptions` per tier. Decoded surfaces, opened `MediaPlaybackItem`s, and Composition resources are retained while their cue is live or hinted; cache entries declare a size cost so eviction respects the configured memory budget instead of dropping arbitrary entries.

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
- Translate prepared `MediaCue` and `AudioCue` payloads into platform playback setup, opening `MediaSource` and `MediaPlaybackItem` during cue preparation so the live take consumes a warm decoder.
- Apply cue-specific fit/crop/stretch, in/out, loop, rate, delay, duration, volume, marker, poster, and transition settings where supported.
- Expose runtime transport snapshots for presentation/media, audio, and announcement playback without blending those lanes. The Show playback panel should display the file/cue name and state for the selected lane only.
- Implement gapless looping with `MediaPlaybackList` (`AutoRepeatEnabled = true`, `MaxPlayedItemsToKeepOpen > 0`); never seek to zero for loops.
- Gate first-frame visibility on `MediaPlaybackItem.OpenOperationCompleted` and `MediaPlaybackSession` reaching `Playing`, then swap the front/back Composition surfaces atomically.
- Report playback state, position, duration, buffering, natural video size, failures, dropped frames where available, and media capability diagnostics to the runtime.
- Raise marker/progress events back into the runtime as command-source events, not direct UI mutations.
- Release or reuse players according to shared player identity and resource-stamp rules. When a `ContentInvalidated`/`MediaAssetChanged` event triggers cue re-preparation, prepare the new player off-thread and swap it in via the runtime's `ReplaceLayerPayload` action.

Media adapter restrictions:

- Do not treat a clicked file path as a cue by itself.
- Do not decide whether media is foreground/background or slide-linked.
- Do not update live state directly when a playback event occurs.
- Do not use `MediaPlayerElement` transport controls as authoritative player state for live production.
- Do not create preview-only playback bindings for audience media slots. Preview scale/chrome can differ, but the visual payload and transport state must come from the audience frame/player identity.
- Do not seek to zero for looping; use `MediaPlaybackList` repeat.

### Media Drawer and Thumbnail Strategy

The Windows media drawer should show cards from the application media read model immediately. A `MediaPanelItemViewModel` or equivalent card model is created from asset/cue metadata first: stable id, display name, media kind, availability, duration/size when known, and fallback type artwork. Thumbnail loading is a progressive property update on that existing card. A missing thumbnail, slow Windows provider, or failed decode must never prevent the media file from appearing in the drawer, being attached to a slide action, or being taken live through the media cue path.

Thumbnail generation should be fast, bounded, and cache-aware:

- Use the runtime thumbnail cache first, keyed by resource stamp, requested card size, display scale, and provider version.
- Coalesce concurrent requests for the same thumbnail so startup warmup, drawer open, and visible-card refresh join one load instead of decoding the same file repeatedly.
- For image files, use the fastest platform thumbnail or direct image decode path that produces the requested card-size image.
- For video files, try one primary Windows shell thumbnail request with the intended `ThumbnailMode`, requested size, and `ThumbnailOptions.UseCurrentScale`. If it returns `null`, throws, or takes too long, move directly to deterministic video frame extraction through Windows media editing APIs such as `MediaClip.CreateFromFileAsync` and `MediaComposition.GetThumbnailAsync`.
- Do not run a broad matrix of `GetThumbnailAsync`/`GetScaledImageAsThumbnailAsync` modes and sizes on the drawer hot path. Microsoft documents that `GetThumbnailAsync` may return `null`, and that `GetScaledImageAsThumbnailAsync` can bypass the thumbnail disk cache for larger requests and affect performance; repeated fallback attempts can therefore delay the visible thumbnail with little added value.
- Cache recent provider failures briefly so repeated drawer refreshes do not pay for the same slow miss until the resource stamp changes.

Startup media warmup should run independently of drawer visibility. During bootstrap, Windows services should enumerate the managed media catalog, resolve file/resource stamps, schedule thumbnails, prepare likely media cues, and retain hot `MediaPlaybackItem`/`MediaPlaybackList` or player-slot handles within the configured cache budget. Opening the drawer should read existing progress and raise priority for visible cards; closing the drawer should not dispose runtime-ready media slots or clear the thumbnail cache.

This strategy follows the Microsoft Learn media guidance that `MediaPlayerElement` is a WinUI rendering surface over `MediaPlayer` and `MediaPlaybackSession`; ChurchPresenter keeps player identity in the runtime/adapter boundary and treats thumbnails/posters as UI cache outputs, not as live-production state.

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
- **Current repo pins:** Windows App SDK **2.2.0**, .NET SDK **10.0.301**, WinUI TFM `net10.0-windows10.0.26100.0`, minimum OS **10.0.17763.0** (see **Repository toolchain** above).
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

Testing follows the boundary in `backend-application.md`, `target-architecture.md`, and `native-hosts.md`.

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
