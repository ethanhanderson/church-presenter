# macOS Code Organization

This document describes how to organize a future native macOS ChurchPresenter app. It is subordinate to the shared architecture in `docs/architecture/` and to the platform implementation guidance in [`macos-native-app.md`](macos-native-app.md).

The rule from the shared architecture still owns every code-placement decision:

```text
Native macOS shell expresses operator intent.
Shared application runtime owns production truth.
```

The macOS app may use Swift, SwiftUI, AppKit, AVFoundation, Core Audio, Core Animation, Metal, ScreenCaptureKit, Keychain, and sandbox/bookmark APIs. Those frameworks belong behind host surfaces and adapters. They must not create a second product model for presentations, playlists, Looks, live layers, packages, diagnostics, or recovery.

## Goals

- Keep platform-neutral contracts, schemas, command semantics, output routing, content graph rules, and diagnostics aligned with `target-architecture.md`, `backend-application.md`, `content-management.md`, `rendering-engine-replacement.md`, `native-hosts.md`, and `file-access-and-cache-resilience.md`.
- Make the macOS codebase feel native without letting SwiftUI/AppKit state become the source of live production truth.
- Split code by ownership: app host, feature presentation, runtime bridge, platform adapters, rendering hosts, machine-local persistence, shared UI, tests, fixtures, and docs.
- Make every feature area scalable enough for the ProPresenter-derived feature map: Show, Editor, Reflow, Themes, Output Setup, Settings/Support, media/audio, stage, overlays, integrations, diagnostics, automation, remotes, capture, and transports.
- Preserve clear dependency direction so future shared runtime code can either be compiled into macOS or mirrored through generated Swift contracts and conformance tests.

## Recommended Workspace Boundary

If there is no macOS project yet, start with an Xcode workspace and local Swift packages rather than one large app target. The app target should be thin; most code should live in package targets with explicit dependencies.

```text
apps/macos/ChurchPresenter/
  ChurchPresenter.xcworkspace
  ChurchPresenterApp/
    ChurchPresenterApp.xcodeproj
    Sources/
    Resources/
    Entitlements/
    Configurations/
  Packages/
    ChurchPresenterAppHost/
    ChurchPresenterFeatures/
    ChurchPresenterRendering/
    ChurchPresenterPlatformAdapters/
    ChurchPresenterMachineState/
    ChurchPresenterDesignSystem/
    ChurchPresenterRuntimeContracts/
    ChurchPresenterRuntimeBridge/
  Tests/
    ChurchPresenterAppHostTests/
    ChurchPresenterFeatureTests/
    ChurchPresenterRenderingTests/
    ChurchPresenterPlatformAdapterTests/
    ChurchPresenterUITests/
    Fixtures/
  Docs/
    decisions/
    adapter-notes/
    test-plans/
```

Use separate Xcode schemes for `ChurchPresenter`, `ChurchPresenterUnitTests`, `ChurchPresenterIntegrationTests`, `ChurchPresenterUITests`, and renderer snapshot or performance suites. Keep package products named by architectural responsibility, not by temporary screens.

## Package and Target Boundaries

### `ChurchPresenterApp`

The app target owns the deployable `.app` shell.

```text
ChurchPresenterApp/
  Sources/
    ChurchPresenterApp.swift
    AppDelegate.swift
    SceneConfiguration.swift
    AppCommands.swift
    AppEnvironment.swift
  Resources/
    Assets.xcassets/
    Localizable.xcstrings
    Info.plist
  Entitlements/
    ChurchPresenter.debug.entitlements
    ChurchPresenter.release.entitlements
  Configurations/
    Debug.xcconfig
    Release.xcconfig
```

Responsibilities:

- Declare the SwiftUI `@main App`, scenes, settings scene, command menus, and AppKit delegate bridge.
- Wire app startup, crash/log sinks, environment construction, and package entry points.
- Contain app identity, resources, entitlements, purpose strings, signing configuration, and localization resources.
- Avoid feature business logic, direct file graph traversal, direct output routing, or direct media-player ownership.

### `ChurchPresenterRuntimeContracts`

This package contains Swift representations of shared contracts. It should be generated or kept schema-aligned where practical.

```text
Packages/ChurchPresenterRuntimeContracts/
  Sources/ChurchPresenterRuntimeContracts/
    Commands/
      LiveCommand.swift
      CommandSource.swift
      CommandTarget.swift
      ActionBatchPreview.swift
    Content/
      PresentationDocumentDTO.swift
      LibraryDTO.swift
      PlaylistDTO.swift
      ThemeDTO.swift
      GeneratedContentDTO.swift
    Media/
      MediaAssetDTO.swift
      MediaCueDTO.swift
      AudioCueDTO.swift
      LiveInputDTO.swift
    Output/
      OutputScreenDTO.swift
      OutputEndpointDTO.swift
      LookPresetDTO.swift
      LayerKind.swift
      ClearGroupDTO.swift
      StageLayoutDTO.swift
    Rendering/
      SceneSnapshotDTO.swift
      SceneNodeDTO.swift
      AudienceFrameDTO.swift
      StageFrameDTO.swift
      RenderDiagnosticDTO.swift
    Diagnostics/
      DiagnosticEventDTO.swift
      RecoveryActionDTO.swift
      HealthStatusDTO.swift
    Persistence/
      ResourceStampDTO.swift
      PackagePreviewDTO.swift
      MachineBindingDTO.swift
```

Rules:

- Types in this package are data contracts, not UI state containers.
- DTOs should use stable ids, explicit versions, portable enums, and loss-tolerant decoding for forward compatibility.
- Do not import SwiftUI, AppKit, AVFoundation, Metal, ScreenCaptureKit, Keychain APIs, or package-specific service implementations.
- Prefer `Sendable`, value semantics, immutable properties where possible, and explicit conversion boundaries into UI projections.

### `ChurchPresenterRuntimeBridge`

This package adapts the macOS host to the shared application runtime. It is the only place that should know whether the runtime is embedded, out-of-process, generated, or reimplemented in Swift with conformance tests.

```text
Packages/ChurchPresenterRuntimeBridge/
  Sources/ChurchPresenterRuntimeBridge/
    Bootstrap/
      RuntimeBootstrapper.swift
      RuntimeConfiguration.swift
      RuntimeServiceRegistry.swift
    Commands/
      RuntimeCommandDispatcher.swift
      LiveCommandFactory.swift
      CommandAuditContextProvider.swift
    Queries/
      RuntimeQueryClient.swift
      QuerySubscription.swift
      ReadModelVersion.swift
    Documents/
      DocumentMutationClient.swift
      PresentationTextWorkflowClient.swift
      ThemeMutationClient.swift
    Diagnostics/
      RuntimeDiagnosticSink.swift
      RuntimeRecoveryClient.swift
    HostFeedback/
      HostFeedbackReporter.swift
      EndpointFeedbackMapper.swift
      PlayerFeedbackMapper.swift
```

Rules:

- Feature view models depend on protocols from this bridge, not on concrete runtime implementation details.
- All live operations dispatch `LiveCommand` or call explicit runtime recovery operations.
- All document edits call document services or mutation clients.
- Query subscriptions should carry version stamps so UI caches invalidate from runtime state instead of path strings or local guesses.

### `ChurchPresenterAppHost`

This package owns the host shell, navigation, window coordination, app lifecycle glue, and cross-feature state that is truly host-local.

```text
Packages/ChurchPresenterAppHost/
  Sources/ChurchPresenterAppHost/
    Shell/
      MainShellView.swift
      MainShellModel.swift
      ShellDestination.swift
      SidebarState.swift
    Navigation/
      AppNavigator.swift
      WindowRoute.swift
      DeepLinkRouter.swift
      FocusedSelectionValues.swift
    Menus/
      MainMenuCommands.swift
      ShowCommandMenu.swift
      EditorCommandMenu.swift
      OutputCommandMenu.swift
      DiagnosticsCommandMenu.swift
    Windows/
      OperatorWindowCoordinator.swift
      OutputWindowCoordinator.swift
      StageWindowCoordinator.swift
      InspectorWindowCoordinator.swift
    Lifecycle/
      StartupCoordinator.swift
      TerminationCoordinator.swift
      PermissionPromptCoordinator.swift
      DisplayChangeObserver.swift
    DependencyInjection/
      AppContainer.swift
      FeatureModuleRegistry.swift
      AdapterRegistry.swift
```

Host-local state may include selected page, selected operator item, focused editor object, expanded sidebars, filters, sheet state, window placement, local drag state, and optimistic affordances. It must not include authoritative live layer state, Look routing, player truth, clear-group expansion, media relink truth, package conflict decisions, or stage layout resolution.

### `ChurchPresenterFeatures`

This package owns workflow presentation. Split by feature area with consistent subfolders.

```text
Packages/ChurchPresenterFeatures/
  Sources/ChurchPresenterFeatures/
    Show/
      Views/
      ViewModels/
      Commands/
      Panels/
      Inspectors/
      Models/
    Editor/
      Views/
      ViewModels/
      Canvas/
      Inspectors/
      Tools/
      Commands/
      Models/
    Reflow/
      Views/
      ViewModels/
      TextProjection/
      Commands/
    Themes/
      Views/
      ViewModels/
      Inspectors/
      Templates/
      Commands/
    OutputSetup/
      Views/
      ViewModels/
      Screens/
      Looks/
      StageLayouts/
      Capture/
      Commands/
    SettingsSupport/
      Views/
      ViewModels/
      Diagnostics/
      Packages/
      Migration/
      Sync/
      Integrations/
      Repair/
    MediaAudio/
      Views/
      ViewModels/
      Inspectors/
      CueLists/
      Preview/
      Commands/
    Automation/
      Views/
      ViewModels/
      Macros/
      Timelines/
      DeviceBindings/
      Commands/
```

Each feature area should follow the same internal pattern:

```text
FeatureName/
  Views/
    FeatureNameView.swift
    FeatureNameToolbar.swift
    FeatureNameSidebar.swift
    FeatureNameDetail.swift
  ViewModels/
    FeatureNameViewModel.swift
    FeatureNameSelectionModel.swift
    FeatureNameQueryProjection.swift
  Commands/
    FeatureNameCommands.swift
    FeatureNameCommandFactory.swift
  Panels/
    FeatureNamePanel.swift
  Inspectors/
    FeatureNameInspector.swift
  Models/
    FeatureNameUIState.swift
    FeatureNameRowModel.swift
```

Use `Panels/` for reusable high-density workflow panels, `Inspectors/` for property editing surfaces, `Commands/` for host intent factories and menu/focused-value actions, and `Models/` only for UI projection types that are not shared contracts.

### `ChurchPresenterDesignSystem`

This package owns reusable native UI pieces and styling primitives.

```text
Packages/ChurchPresenterDesignSystem/
  Sources/ChurchPresenterDesignSystem/
    Tokens/
      CPSpacing.swift
      CPTypography.swift
      CPColors.swift
      CPShapes.swift
    Components/
      Sidebar/
      Cards/
      Transport/
      Inspectors/
      Badges/
      Meters/
      EmptyStates/
      Search/
    Layout/
      SplitPane.swift
      InspectorColumn.swift
      AdaptiveGrid.swift
    Accessibility/
      AccessibilityStateLabels.swift
      CommandAccessibility.swift
    PreviewSupport/
      DesignSystemFixtures.swift
```

Rules:

- Components should be reusable because their interaction and visual language is shared, not because two files happen to look similar today.
- Do not put feature-specific commands or runtime clients in this package.
- Prefer standard SwiftUI/AppKit controls first. Add custom controls for slide decks, output previews, transport strips, meters, and canvas/editor interactions where native controls cannot express the workflow clearly.

### `ChurchPresenterRendering`

This package owns macOS scene and frame application. It consumes immutable runtime contracts and reports host diagnostics.

```text
Packages/ChurchPresenterRendering/
  Sources/ChurchPresenterRendering/
    Core/
      RenderSurface.swift
      RenderSurfaceConfiguration.swift
      FrameApplyPlan.swift
      RenderTimingRecorder.swift
    SceneAdapters/
      SceneAdapter.swift
      TextSceneNodeAdapter.swift
      ShapeSceneNodeAdapter.swift
      MediaSceneNodeAdapter.swift
      LiveVideoSceneNodeAdapter.swift
      WebSceneNodeAdapter.swift
      GroupSceneNodeAdapter.swift
    Audience/
      AudienceFrameRenderer.swift
      AudienceLayerRenderer.swift
      AudiencePreviewView.swift
    Stage/
      StageFrameRenderer.swift
      StageLayoutRenderer.swift
      StagePreviewView.swift
    Editor/
      EditorCanvasView.swift
      EditorAdornerLayer.swift
      SelectionAdornerRenderer.swift
      GuideRenderer.swift
      SnapLineRenderer.swift
    Thumbnails/
      ThumbnailRenderer.swift
      ThumbnailCacheKey.swift
      ThumbnailRenderQueue.swift
    Backends/
      CoreAnimation/
      Metal/
      CoreGraphics/
      AVFoundation/
    Diagnostics/
      RenderDiagnosticMapper.swift
      SnapshotRenderer.swift
      RenderPerformanceLog.swift
```

Restrictions:

- Do not read presentation files, content roots, support files, or media folders directly.
- Do not decide what is live, which Look is active, which layer clears, or which stage layout applies.
- Do not serialize editor adorners into document models.
- Do not turn screenshots of output windows into the main capture pipeline when resolved frames or renderer-owned textures are available.

### `ChurchPresenterPlatformAdapters`

This package owns macOS APIs behind capability, health, and feedback contracts.

```text
Packages/ChurchPresenterPlatformAdapters/
  Sources/ChurchPresenterPlatformAdapters/
    Display/
      MacDisplayDiscoveryService.swift
      MacOutputEndpointAdapter.swift
      OutputWindowController.swift
      DisplayMappingReconciler.swift
      DisplayCapabilityMapper.swift
    Media/
      AVMediaPlayerAdapter.swift
      MediaCuePlayer.swift
      MediaMarkerObserver.swift
      MediaThumbnailProvider.swift
    Audio/
      CoreAudioRouteAdapter.swift
      AudioDeviceDiscoveryService.swift
      AudioTestToneService.swift
    LiveVideo/
      AVCaptureDeviceAdapter.swift
      LiveVideoInputDiscoveryService.swift
      LiveVideoPreviewProvider.swift
    Capture/
      ScreenFrameCaptureAdapter.swift
      RecordingAdapter.swift
      StreamingAdapter.swift
      SyphonEndpointAdapter.swift
      NDIEndpointAdapter.swift
      SDIEndpointAdapter.swift
    FileAccess/
      SecurityScopedBookmarkStore.swift
      FilePickerAdapter.swift
      ResourceAccessCoordinator.swift
      RelinkPromptAdapter.swift
    Credentials/
      KeychainCredentialStore.swift
      AccountAuthorizationPresenter.swift
    Automation/
      MIDIAdapter.swift
      DMXAdapter.swift
      StreamDeckAdapter.swift
      NetworkLinkAdapter.swift
      HTTPControlAdapter.swift
    Accessibility/
      AccessibilityElementFactory.swift
      CanvasAccessibilityAdapter.swift
    Notifications/
      UserNotificationAdapter.swift
      AppLifecycleNotificationAdapter.swift
```

Adapters translate macOS facts into shared capability and health models. They do not mutate portable content, expand commands, or make product-state decisions.

### `ChurchPresenterMachineState`

This package owns machine-local persistence and cache locations for the macOS host.

```text
Packages/ChurchPresenterMachineState/
  Sources/ChurchPresenterMachineState/
    Settings/
      MachineSettingsStore.swift
      UserPreferenceStore.swift
      WindowPlacementStore.swift
    Bindings/
      OutputEndpointBindingStore.swift
      DeviceBindingStore.swift
      ContentRootBindingStore.swift
    Bookmarks/
      BookmarkRecord.swift
      BookmarkResolutionResult.swift
      BookmarkMigrationService.swift
    Caches/
      CacheRootProvider.swift
      ThumbnailCacheStore.swift
      RenderCacheStore.swift
      IntegrationCacheStore.swift
      DiagnosticsSnapshotStore.swift
    Migrations/
      MachineStateMigration.swift
      MachineStateSchemaVersion.swift
```

Machine-local data includes display ids, endpoint bindings, device selections, security-scoped bookmarks, credential references, window placement, local recents, caches, diagnostic snapshots, and activation state. It must not become a portable content package unless the operator explicitly exports machine bindings.

## Dependency Direction

Use one-way dependencies.

```text
ChurchPresenterApp
  -> ChurchPresenterAppHost
  -> ChurchPresenterFeatures
  -> ChurchPresenterDesignSystem

ChurchPresenterFeatures
  -> ChurchPresenterRuntimeBridge
  -> ChurchPresenterRuntimeContracts

ChurchPresenterRendering
  -> ChurchPresenterRuntimeContracts

ChurchPresenterPlatformAdapters
  -> ChurchPresenterRuntimeContracts
  -> ChurchPresenterMachineState

ChurchPresenterRuntimeBridge
  -> ChurchPresenterRuntimeContracts

ChurchPresenterAppHost
  -> ChurchPresenterRendering
  -> ChurchPresenterPlatformAdapters
  -> ChurchPresenterMachineState
```

Avoid dependencies from contracts back into host packages, from design system into feature packages, from platform adapters into feature view models, and from rendering into content persistence.

Feature views should depend on view models. View models should depend on runtime bridge protocols, platform capability protocols only when the workflow is explicitly about setup, and design-system components. Platform adapters should report facts through feedback sinks rather than calling feature view models.

## Feature Ownership Map

| Feature area | Primary package/folder | Owns | Must delegate to runtime/shared services |
|---|---|---|---|
| Show | `ChurchPresenterFeatures/Show` | playlist operation UI, slide deck projection, live/next previews, clear buttons, Show Controls layout, selected operator item, keyboard/menu intent | slide/media/audio activation, action expansion, live layer state, Look routing, clear groups, stage messages, timer/macro execution |
| Editor | `ChurchPresenterFeatures/Editor`, `ChurchPresenterRendering/Editor` | editor shell, tools, canvas adorners, selection handles, inspectors, validation display, local editing focus | document mutation, scene compilation, theme resolution, media references, action edits, save/repair |
| Reflow | `ChurchPresenterFeatures/Reflow` | text projection UI, split/combine affordances, arrangement-aware editing surface | document mutations that preserve slide ids, groups, arrangements, actions, provenance, cache invalidation |
| Themes | `ChurchPresenterFeatures/Themes` | theme library UI, template inspectors, generated-content template previews, variant editing surface | theme document persistence, variant resolution, package/sync behavior, Look-selected variants |
| Output Setup | `ChurchPresenterFeatures/OutputSetup`, `PlatformAdapters/Display`, `Rendering/Audience`, `Rendering/Stage` | screens/endpoints setup UI, display mapping presentation, Look/stage layout editors, capture setup panels | logical screen records, Look routing semantics, stage layout records, endpoint recovery, clear policies, capture source semantics |
| Settings and Support | `ChurchPresenterFeatures/SettingsSupport`, `MachineState` | settings UI, support panels, package/migration/sync UX, diagnostics presentation, repair prompts | package graph decisions, sync/merge semantics, audit results, repair operations, portable vs machine-local scope |
| Media and Audio | `ChurchPresenterFeatures/MediaAudio`, `PlatformAdapters/Media`, `PlatformAdapters/Audio` | Media Bin/Audio Bin UI, cue inspectors, preview controls, adapter state display | asset identity, cue usage semantics, active player truth, playback marker command dispatch, relink state, audio routing records |
| Live Video | `PlatformAdapters/LiveVideo`, `Rendering/SceneAdapters` | camera/device discovery, permission prompts, live preview host, sample-buffer handoff | logical input identity, cue-like trigger behavior, layer routing, health and recovery commands |
| Integrations | `SettingsSupport/Integrations`, `PlatformAdapters/Automation`, runtime bridge integration clients | sign-in UI, account health display, import/refresh prompts, device binding setup | generated/imported provenance, local ids, refresh/reporting semantics, license/copyright metadata, command/query authorization |
| Diagnostics | `SettingsSupport/Diagnostics`, `RuntimeBridge/Diagnostics`, adapter diagnostics folders | host health presentation, logs, support package UX, adapter detail panes | diagnostic classification, recovery action generation, content graph audit, runtime command provenance |
| Rendering Hosts | `ChurchPresenterRendering/*`, `AppHost/Windows` | native surfaces, frame diff application, renderer timing, preview/thumbnail/editor adapter views | scene/frame contents, route resolution, stage provider data, clear/suppressed state, transition choice |
| Platform Adapters | `ChurchPresenterPlatformAdapters/*` | macOS display/media/audio/capture/file/credential/device APIs, capability and health mapping | product semantics, portable records, command expansion, package semantics, recovery decisions |
| Automation and Remotes | `Features/Automation`, `PlatformAdapters/Automation` | setup UI, device discovery, event intake, local server/client host plumbing | command creation, query authorization, macro execution, timecode/calendar scheduling semantics, Network Link conflict handling |

## Naming Conventions

Use names that communicate architectural role.

- Swift packages: `ChurchPresenter<AppArea>`, for example `ChurchPresenterRendering`.
- Feature folders: product workflow names, for example `Show`, `OutputSetup`, `SettingsSupport`.
- Views: noun plus `View`, for example `ShowView`, `MediaCueGridView`, `LookMatrixView`.
- View models: noun plus `ViewModel`, for example `ShowViewModel`, `OutputSetupViewModel`.
- UI projections: noun plus `RowModel`, `CardModel`, `PanelModel`, or `QueryProjection`, for example `SlideCardModel`, `OutputEndpointRowModel`, `ShowQueryProjection`.
- Commands/factories: verb-oriented host intent, for example `ShowCommands`, `LiveCommandFactory`, `EditorCommandFactory`.
- Protocols: capability names, for example `RuntimeCommandDispatching`, `OutputEndpointManaging`, `MediaPlaybackHosting`, `SecurityScopedBookmarkResolving`.
- Adapters: platform prefix plus capability, for example `MacDisplayDiscoveryService`, `AVMediaPlayerAdapter`, `CoreAudioRouteAdapter`.
- DTOs/contracts: shared model name plus `DTO` only when there is a separate UI/domain projection, for example `AudienceFrameDTO`.
- Coordinators/controllers: use these names only for lifecycle or AppKit ownership, for example `OutputWindowController`, `StartupCoordinator`.

Prefer stable domain terms from the shared architecture: `Presentation`, `Library`, `Playlist`, `MediaAsset`, `MediaCue`, `AudioCue`, `LiveCommand`, `ActionBatch`, `LiveSessionSnapshot`, `OutputScreen`, `OutputEndpoint`, `LookPreset`, `StageLayout`, `ClearGroup`, `SceneSnapshot`, `AudienceFrame`, and `StageFrame`.

Avoid synonyms that create accidental second models, such as using `Display` when the code means logical `OutputScreen`, `Monitor` when it means concrete endpoint, `Project` when it means presentation document, or `Scene` when it means SwiftUI scene.

## File-Splitting Patterns

Split by responsibility before splitting by line count.

For SwiftUI feature surfaces:

- `FeatureView.swift`: composition root for the workflow.
- `FeatureSidebar.swift`: navigation or source list region.
- `FeatureToolbar.swift`: workflow toolbar and command affordances.
- `FeatureDetail.swift`: selected item/detail body.
- `FeatureInspector.swift`: property editing or diagnostics inspector.
- `FeatureViewModel.swift`: UI-facing projection and intent methods.
- `FeatureQueryProjection.swift`: mapping from runtime read models to UI models.
- `FeatureCommands.swift`: menu/focused command registration.

For AppKit or rendering surfaces:

- `SurfaceView.swift`: SwiftUI representable or wrapper.
- `SurfaceNSView.swift`: concrete `NSView` or hosted view.
- `SurfaceCoordinator.swift`: SwiftUI/AppKit lifecycle bridge.
- `SurfaceRenderer.swift`: applies immutable scene/frame input.
- `SurfaceAccessibilityAdapter.swift`: custom accessibility elements.
- `SurfaceSnapshotTests.swift`: deterministic rendering or accessibility tests.

For adapters:

- `CapabilityDiscoveryService.swift`: enumerates platform facts.
- `BindingStore.swift`: persists machine-local mappings when needed.
- `Adapter.swift`: performs platform operation.
- `FeedbackMapper.swift`: maps platform errors/status into runtime diagnostics.
- `FakeAdapter.swift`: test double in test support, not production source.

Keep one primary type per file when the type owns meaningful behavior. Small private helper types may stay with the owner. Do not create one-file-per-enum churn for tiny closed implementation details.

## SwiftUI, AppKit, Observable Models, and Services

Use SwiftUI for the operator shell, settings, common panels, inspectors, popovers, sheets, and native menus. Use AppKit for output windows, high-density custom views, precise responder-chain behavior, display lifecycle, custom accessibility, canvas surfaces, and renderer hosting.

Recommended state flow:

```text
Runtime query/read model
  -> @MainActor Observable view model projection
  -> SwiftUI/AppKit view
  -> operator intent method
  -> LiveCommand or document-service mutation
  -> runtime snapshot/read model update
```

Guidelines:

- Mark UI-facing view models `@MainActor`.
- Use Swift Observation for view models that SwiftUI binds to directly.
- Keep long-running work in coordinators, services, actors, or runtime clients, not in view structs.
- Use focused values and command validation so menu commands follow the active window/selection.
- Keep AppKit controllers responsible for native window/view lifecycle, not product semantics.
- Use actors or serial queues for resource managers with thread-affinity: media players, capture sessions, file access, render queues, and cache writers.
- Treat `@State` and `@Observable` as presentation state. They are not the live session.

Service protocols should describe capabilities from the host's point of view:

```swift
protocol RuntimeCommandDispatching {
    func dispatch(_ command: LiveCommandDTO) async throws -> CommandResultDTO
}

protocol OutputEndpointManaging {
    func refreshEndpoints() async
    func apply(_ frame: AudienceFrameDTO, to endpointID: OutputEndpointID) async
}

protocol MediaPlaybackHosting {
    func prepare(_ payload: MediaRenderPayloadDTO) async throws -> MediaPlayerHandle
    func apply(_ policy: MediaPlaybackPolicyDTO, to handle: MediaPlayerHandle) async
}
```

Feature code should depend on protocols like these. Concrete implementations should be registered at app composition time.

## Shared Runtime vs Host-Specific Placement

Put portable or shared-contract code in runtime contracts or shared runtime implementation:

- command kinds, command source metadata, action batches, query contracts,
- content graph records and schemas,
- presentation, theme, media, cue, playlist, and support DTOs,
- Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, masks,
- scene snapshots, frame sets, render payloads, diagnostics, resource stamps,
- package, sync, migration, audit, repair, and recovery semantics.

Put macOS-specific code in host packages:

- SwiftUI/AppKit views, menus, windows, commands, focus, gestures, accessibility wrappers,
- `NSScreen`, `NSWindow`, `CGDirectDisplayID`, presentation options, display notifications,
- AVFoundation player and capture objects, Core Audio device routing, ScreenCaptureKit use,
- Core Animation, Metal, Core Graphics, Core Text/TextKit rendering implementation,
- Keychain, entitlements, security-scoped bookmarks, file panels, sandbox prompts,
- Syphon-like endpoints and macOS-only transport binding details,
- notarization, signing, installer/update channel details.

If a concept affects saved church production behavior, start by assuming it belongs in shared runtime/content contracts. If it affects only how this Mac realizes the behavior, it belongs in machine-local state or an adapter.

## Reusable Views and Panels

Reusable views should be organized by product interaction pattern:

- Deck/card grids: slide cards, media cue cards, playlist item cards.
- Transport strips: next/previous, play/pause, scrub, marker, loop, clear/recover.
- Health badges: endpoint, player, resource, integration, capture, command status.
- Inspectors: labeled property groups, validation rows, source/provenance rows.
- Output previews: frame preview surface, layer badges, endpoint overlays, dropped-frame markers.
- Source lists: libraries, playlists, media folders, support categories.
- Empty states: missing content root, no playlist selected, no endpoint mapped, denied permission.

Reusable panels should expose intent callbacks or command protocol dependencies, not runtime singletons. A `ClearGroupButton` can receive a `ClearGroupDTO` and an `onActivate` closure. It should not know how to expand the clear group.

## Inspectors

Use inspectors for object, cue, theme, output, and diagnostics details. Keep inspector data source-specific:

- Slide/object inspectors edit document object properties through document mutation clients.
- Media/audio cue inspectors edit cue usage metadata, not asset identity unless explicitly in asset details.
- Theme inspectors edit reusable theme/template records.
- Output inspectors edit logical screen, endpoint binding, Look, stage, capture, and transport setup through runtime/support services.
- Diagnostics inspectors display runtime diagnostics and host adapter facts with runtime-provided recovery actions.

Do not let inspectors directly mutate files, clear live layers, or repair support records without going through runtime services.

## Commands

Commands should be explicit, typed, source-aware, and testable.

Organize command-producing code into:

```text
Commands/
  ShowCommands.swift
  EditorCommands.swift
  OutputCommands.swift
  RecoveryCommands.swift
  MenuCommandValidation.swift
  KeyboardShortcutMap.swift
```

Rules:

- Menu commands, keyboard shortcuts, slide clicks, cue clicks, macro buttons, device events, HTTP/API actions, and remotes should all produce the same runtime command kinds.
- Include source metadata: local UI area, window, focused selection, user action, device/API identity where applicable, correlation id, and preview/validation mode.
- Commands should not mutate view model state as a substitute for runtime completion. Optimistic UI state may be shown, but runtime query updates reconcile truth.
- Document edits are not live commands unless they intentionally affect live production; route them to document mutation services.

## DTOs and UI Models

Keep DTOs and UI models separate.

- DTOs mirror shared contracts, decode from runtime responses, and preserve ids, versions, diagnostics, and unknown future values where possible.
- UI models are shaped for display: formatted strings, icons, section headers, badge states, selection affordances, sorting/grouping projections, and accessibility labels.
- UI models should keep source ids so actions can dispatch commands without reverse lookup through visible text.
- Do not persist UI models.

Example:

```text
PlaylistOccurrenceDTO
  -> ShowPlaylistRowModel
  -> ShowPlaylistRowView
  -> takeOccurrence(occurrenceID:)
  -> LiveCommandDTO.takePlaylistOccurrence(...)
```

## Render Hosts

Use separate render host types for each consumer:

- `AudienceFrameRenderer`: applies resolved audience frames to output windows and output previews.
- `StageFrameRenderer`: applies stage frames and provider snapshots.
- `ThumbnailRenderer`: renders thumbnails with thumbnail intent and cache keys.
- `EditorCanvasRenderer`: renders scenes plus editor adorners.
- `ScreenPreviewRenderer`: renders multiview and preview surfaces from resolved frames.
- `CaptureFrameProvider`: hands renderer-owned frames/textures/buffers to capture and transport consumers.

Render hosts should take immutable frame or scene inputs and return apply results, timing, and diagnostics. They should not ask the runtime for missing pieces during render. If a frame is incomplete, report diagnostics and let runtime recovery decide next steps.

## Media, Audio, and Output Endpoint Adapters

Media adapters:

- Own concrete `AVPlayer`, `AVQueuePlayer`, `AVPlayerLooper`, sample-buffer, or texture paths.
- Apply runtime-resolved cue policy: in/out, delay, rate, loop, retrigger, stop behavior, marker actions, scaling, crop, poster, volume, and transitions.
- Report readiness, time, marker, stall, failure, loop, and end-of-item status.
- Raise marker events into the command executor.

Audio adapters:

- Own device discovery, route mapping, channel mapping, delay, mute/solo, test tones, underrun reporting, and device health.
- Treat visual outputs and audio routes as related but separate.
- Keep aggregate devices, HDMI/DisplayPort audio, USB interfaces, virtual devices, and transport audio as machine-local capabilities.

Endpoint adapters:

- Own concrete local display, Syphon-like, NDI, SDI, recording, streaming, RTMP, Resi-like, and future transport bindings.
- Report capabilities: fullscreen, transparency, audio, capture, key/fill, mirroring, grouping, edge blend, HDR/EDR, bit depth, FPS, scale, and platform flags.
- Consume resolved frames for logical screens.
- Never compute Look routing or fork content rendering.

## File Access, Caches, and Fixtures

File access should follow `file-access-and-cache-resilience.md`.

Folder placement:

```text
ChurchPresenterPlatformAdapters/FileAccess/
  SecurityScopedBookmarkStore.swift
  FilePickerAdapter.swift
  ResourceAccessCoordinator.swift

ChurchPresenterMachineState/Caches/
  ThumbnailCacheStore.swift
  RenderCacheStore.swift
  IntegrationCacheStore.swift

Tests/Fixtures/
  ContentRoots/
  Packages/
  Media/
  Rendering/
  MachineState/
```

Rules:

- Security-scoped bookmarks are machine-local bindings, not portable content.
- Cache keys must include content/resource/theme/token stamps, render intent, display scale, and renderer schema version where applicable.
- Test fixtures should include normal, missing, moved, denied, stale bookmark, corrupt, unsupported, stale cache, and package conflict cases.
- Fixtures should be small and deterministic. Large media fixtures should be referenced by documented test setup or generated test assets.

## Tests

Organize tests around the architecture boundary.

```text
Tests/
  ChurchPresenterAppHostTests/
    StartupCoordinatorTests.swift
    MenuCommandValidationTests.swift
    FocusedSelectionTests.swift
  ChurchPresenterFeatureTests/
    ShowViewModelTests.swift
    EditorProjectionTests.swift
    ReflowMutationIntentTests.swift
    OutputSetupProjectionTests.swift
  ChurchPresenterRenderingTests/
    SceneAdapterTests.swift
    FrameApplyPlanTests.swift
    ThumbnailCacheKeyTests.swift
    RenderDiagnosticMapperTests.swift
  ChurchPresenterPlatformAdapterTests/
    DisplayMappingReconcilerTests.swift
    BookmarkStoreTests.swift
    MediaPlayerFeedbackMapperTests.swift
    AudioRouteAdapterTests.swift
  ChurchPresenterIntegrationTests/
    MissingContentRootStartupTests.swift
    OutputWindowFrameApplyTests.swift
    RelinkAndCacheRebuildTests.swift
  ChurchPresenterUITests/
    KeyboardShowOperationTests.swift
    OutputSetupAccessibilityTests.swift
    PackagePreviewFlowTests.swift
  Fixtures/
```

Test focus:

- Feature tests verify query projection, command creation, focused selection, local UI state, and recovery presentation.
- Adapter tests verify macOS capability and error mapping using test doubles where real hardware is unavailable.
- Rendering tests verify scene-node adaptation, frame diff plans, snapshot output, alpha/mask behavior, stage provider display, and timing diagnostics.
- UI tests verify keyboard operation, menu command availability, accessibility labels/state, permission/relink sheets, and output setup flows.
- Shared runtime semantic tests should remain outside the macOS host unless a macOS adapter can change behavior.

## Docs Placement

Keep docs close to the boundary they describe.

```text
apps/macos/ChurchPresenter/Docs/
  decisions/
    0001-runtime-bridge-strategy.md
    0002-renderer-backend-choice.md
    0003-sandbox-and-distribution.md
  adapter-notes/
    display-endpoints.md
    avfoundation-media.md
    core-audio-routing.md
    file-bookmarks.md
    syphon-ndi-sdi.md
  test-plans/
    output-window-lifecycle.md
    accessibility-keyboard-operation.md
    signing-notarization.md
```

Use `docs/architecture/` for shared architecture and cross-platform decisions. Use `apps/macos/ChurchPresenter/Docs/` for implementation notes, adapter tradeoffs, setup instructions, and platform release checklists.

## Anti-Patterns to Avoid

- Building the Show page as the live-state owner.
- Letting SwiftUI `@State`, `@Observable`, or AppKit controller fields become authoritative layer/player/output truth.
- Having output windows inspect presentations, playlists, or media folders directly.
- Implementing Look routing, clear groups, stage layout resolution, or macro expansion in views.
- Treating Media Bin or Audio Bin as a raw file browser.
- Persisting macOS security-scoped bookmarks, display ids, window positions, credentials, caches, or local recents into portable packages by default.
- Creating macOS-only presentation, playlist, theme, Look, stage layout, or package formats without a shared extension point.
- Reusing one `ViewModel` across unrelated workflows because it is convenient.
- Adding a service locator singleton that any view can call for command dispatch.
- Flattening unsupported imported effects during render or import without diagnostics.
- Re-capturing the app's own output window as the primary recording/streaming path.
- Using concrete `NSScreen`, `AVPlayer`, `AVCaptureDevice`, or Keychain types in shared contracts.
- Writing tests that duplicate the full runtime semantic matrix instead of testing macOS projection and adapter behavior.
- Preserving temporary compatibility shims for unshipped macOS behavior instead of replacing them with the correct architecture.

## Phased Bootstrap and Migration Plan

### Phase 1: Workspace and Contracts

- Create `apps/macos/ChurchPresenter/` with the workspace, thin app target, and local Swift packages.
- Add `ChurchPresenterRuntimeContracts` with command, query, content, output, rendering, diagnostic, resource, and machine-binding DTOs aligned to shared architecture.
- Add contract conformance tests using small shared fixtures before building feature UI.
- Decide whether the macOS app embeds a shared runtime core or consumes/generated-reimplements runtime contracts in Swift. Record the decision in app docs.

### Phase 2: App Host and Machine State

- Implement startup, app container, settings store, bookmark store, cache root provider, Keychain wrapper, and diagnostic sink.
- Open the operator shell even when content roots, bookmarks, displays, media permissions, or transport adapters are degraded.
- Add menu command skeletons and focused selection plumbing without live semantics in the shell.

### Phase 3: Read-Only Feature Projections

- Build read-only Show, Output Setup, Settings/Support, and diagnostics projections from runtime query clients.
- Add design-system primitives for cards, source lists, inspectors, transport strips, health badges, and empty states.
- Verify accessibility labels and keyboard traversal while surfaces are still semantically simple.

### Phase 4: Command Dispatch and Document Mutations

- Wire slide take, media/audio trigger, clear layer, clear group, Look activation, stage message, macro run, and recovery actions through `LiveCommand`.
- Wire Editor, Reflow, Themes, and cue inspectors to document mutation clients.
- Add tests that prove views produce correct commands or mutations with source metadata.

### Phase 5: Rendering Hosts

- Implement scene/frame adapters for thumbnails, previews, editor canvas, audience output, and stage output.
- Add render cache keys based on content/resource/theme/token stamps and render intent.
- Add diagnostics for unsupported effects, missing dependencies, frame apply failures, and timing degradation.

### Phase 6: Output Windows and Endpoint Bindings

- Implement display discovery, machine-local endpoint bindings, output window controllers, display change reconciliation, and endpoint health.
- Keep logical screens and Looks portable; only concrete endpoint bindings are machine-local.
- Add integration tests for display connect/disconnect, remap, missing endpoint, and frame apply.

### Phase 7: Media, Audio, Live Video, and Capture

- Add AVFoundation media playback, Core Audio routing, live video input discovery, camera/microphone permissions, marker feedback, and player diagnostics.
- Add capture, recording, streaming, Syphon-like, NDI, SDI, and RTMP/Resi-like adapters as endpoint or capture consumers of resolved frames.
- Keep capture and transport health in diagnostics with runtime-provided recovery actions.

### Phase 8: Advanced Parity and Hardening

- Add richer Editor/Reflow/Theme workflows, generated scripture/song imports, Planning Center-style plans, MultiTracks/SongSelect/ProContent integrations, macros, timelines, timecode, calendar scheduling, Remote/Control APIs, Network Link, Stream Deck, MIDI, and DMX.
- Harden packaging, sync, migration, audit, repair, sandbox behavior, signing, notarization, upgrade, and support package generation.
- Remove any temporary host-specific render/live-state shortcuts introduced during bootstrap.

## Open Gaps to Track

- Runtime strategy: embedded shared core versus Swift implementation with generated contracts and conformance tests.
- Renderer default: Core Animation-first with Metal escape hatches versus Metal-first with native hosting for controls.
- Minimum macOS version and corresponding SwiftUI Observation, ScreenCaptureKit, display/HDR, and sandbox behavior support.
- Initial transport scope: local displays only, then Syphon/NDI/SDI/RTMP/Resi-like integrations.
- Distribution strategy: Mac App Store sandbox-first, Developer ID direct-first, or both from the start.
- Fixture strategy for large media, live video, pro hardware, network transports, and signing/notarization checks.

## References Used

- [`README.md`](../README.md)
- [`target-architecture.md`](../target-architecture.md)
- [`backend-application.md`](../backend-application.md)
- [`content-management.md`](../content-management.md)
- [`rendering-engine-replacement.md`](../rendering-engine-replacement.md)
- [`native-hosts.md`](../native-hosts.md)
- [`file-access-and-cache-resilience.md`](../file-access-and-cache-resilience.md)
- [`macos-native-app.md`](macos-native-app.md)
- [`features.md`](../../reference/propresenter/features.md)

No additional Apple documentation lookup was required for this organization plan; the existing macOS implementation guide already records the Apple documentation topics it consulted.
