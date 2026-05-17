# Windows WinUI Code Organization

This document defines code, file, and feature organization guidance for the ChurchPresenter Windows app. It is subordinate to the shared architecture in `docs/architecture/` and to the Windows platform implementation guidance in `docs/architecture/platforms/winui-native-app.md`.

The purpose of this document is not to redesign product semantics. It describes where Windows code should live, how feature files should be split, and how WinUI host code should depend on the application/runtime layer as ChurchPresenter grows toward the ProPresenter-derived feature set in `docs/reference/propresenter/features.md`.

## Source Of Truth

Follow the shared architecture first:

```text
Native intent
  -> command or document mutation
  -> application runtime
  -> live session snapshot
  -> audience frames / stage frames / diagnostics
  -> Windows host adapters and output endpoints
```

Windows pages, view models, controls, windows, and media/display adapters may present workflows and apply resolved output. They must not fork product truth for documents, live state, Looks, output layers, stage layouts, package/sync behavior, resource health, or recovery decisions.

## Current Solution Boundary

The active Windows solution should stay centered on the existing three-project boundary:

```text
apps/windows/
  ChurchPresenter/
    ChurchPresenter.csproj
    WinUI 3 host, Windows App SDK integration, windows, pages, controls,
    view models, host adapters, XAML resources, packaged app assets.

  ChurchPresenter.Application/
    ChurchPresenter.Application.csproj
    Testable application/runtime layer, document/content services,
    command pipeline, read models, rendering contracts, diagnostics,
    packages, caches, support-file orchestration.

  ChurchPresenter.Core/
    ChurchPresenter.Core.csproj
    Portable low-level content primitives, .cpres bundle I/O, reusable
    file-format/resource code that must not know about WinUI or Windows.

tests/
  ChurchPresenter.App.Tests/
    Unit and contract tests for ChurchPresenter.Application behavior.

  ChurchPresenter.Core.Tests/
    Portable .cpres and core file-format tests.

  ChurchPresenter.Rendering.Benchmarks/
    Rendering and scene/compiler performance checks.
```

Recommended rules:

- `ChurchPresenter` may reference WinUI, Windows App SDK, WinRT, Windows media/display/picker APIs, `CommunityToolkit.Mvvm`, and host-only services.
- `ChurchPresenter.Application` may reference portable .NET libraries and logging abstractions, but must not reference `Microsoft.UI.*`, XAML, `Window`, `Page`, WinRT UI types, or Windows-only media/display APIs.
- `ChurchPresenter.Core` should stay smaller than the application layer and hold portable primitives only.
- Tests should prefer `ChurchPresenter.App.Tests` or `ChurchPresenter.Core.Tests` unless the behavior truly requires a running WinUI host.
- Add new projects only when a real boundary requires it, such as a dedicated WinUI UI-test host, a transport plugin that should be optional, or a reusable cross-host package. Do not split projects merely to make folders look cleaner.

## Recommended Project Trees

These trees are target organization patterns for active projects. They intentionally build from the current layout rather than requiring an immediate rename of every file.

### `apps/windows/ChurchPresenter`

```text
ChurchPresenter/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  Package.appxmanifest

  Assets/
  Hosting/
    AppServices.cs
    AppNavigationRoute.cs
    ServiceCollection extension files by area

  Resources/
    Theme dictionaries, styles, localized strings, icons, app metadata

  Shell/
    Main shell helpers, title-bar models, navigation composition

  Views/
    Show/
      ShowPage.xaml
      ShowPage.xaml.cs
      ShowPage.*.partial.cs
      Dialogs/
      Flyouts/
    Editor/
      EditorWorkspacePage.xaml
      EditPage.xaml
      Dialogs/
    Reflow/
      ReflowPage.xaml
    Themes/
      ThemesPage.xaml
    OutputSetup/
      OutputPage.xaml
      SettingsOutputPage.xaml
      StageOutputPage.xaml
    Settings/
      SettingsPage.xaml
      Settings*DetailPage.xaml
      Dialogs/
    OutputHosts/
      AudienceOutputWindow.cs
      ProgramOutputSurface.xaml
      StageOutputPage.xaml

  ViewModels/
    Shell/
    Show/
    Editor/
    Reflow/
    Themes/
    OutputSetup/
    Settings/

  Controls/
    Common/
    Show/
    Editor/
    Output/
    Rendering/

  Services/
    Host service interfaces and implementations that depend on WinUI,
    Windows App SDK, WinRT, DispatcherQueue, media player controls,
    display APIs, clipboard, drag/drop, or packaged app APIs.

  Adapters/
    Display/
    FileSystem/
    Media/
    Rendering/
    Capture/
    Credentials/
    Notifications/
    Accessibility/

  Converters/
  Interop/
  Diagnostics/
  Packaging/
```

Current folders such as `Views/`, `ViewModels/`, `Controls/`, `Services/`, `Converters/`, and `Hosting/` are valid. As each feature grows, prefer adding feature subfolders under those roots rather than creating parallel root folders for the same concern.

For example, move toward `Views/Show/ShowPage.xaml`, `ViewModels/Show/ShowViewModel.cs`, and `Controls/Show/SlideCardControl.xaml` over a flat `Views/` and `ViewModels/` namespace that accumulates every workflow.

### `apps/windows/ChurchPresenter.Application`

```text
ChurchPresenter.Application/
  Backend/
    Commands/
      LiveCommandModels.cs
      LiveCommandExecutor.cs
      LiveAutomationModels.cs
    Content/
      ContentModels.cs
      ContentPackaging.cs
    Media/
      MediaContracts.cs
      MediaCueContracts.cs
      MediaPlaybackContracts.cs
      MediaPlaylistContracts.cs
    Output/
      OutputModels.cs
      OutputRoutingContracts.cs
    Overlays/
      OverlayModels.cs
    Rendering/
      RenderModels.cs
      SlideSceneModels.cs
      SlideSceneCompiler.cs
      BackendRenderEngine.cs
    Stage/
      StageModels.cs
      StageDataProviders.cs
    Diagnostics/
      Runtime diagnostic and recovery contracts

  Models/
    App/workspace/settings DTOs
    Content and catalog DTOs
    Presentation document DTOs
    Media/audio DTOs
    Output and routing DTOs
    Query read models
    Support package DTOs
    Transition/theme DTOs

  Services/
    Content/
    Documents/
    Media/
    Output/
    Runtime/
    Show/
    Settings/
    Support/
    Themes/
    Packages/
    Diagnostics/
    Integrations/

  Abstractions/
    Optional future home for pure interfaces when Services becomes too large
```

The current `Backend/`, `Models/`, and `Services/` layout is the right high-level shape. The main migration need is to make `Services/` less flat over time by grouping service implementations by product area, while keeping public contracts easy to find.

`Backend/` should be reserved for runtime production contracts and engines: commands, immutable frames, layer payloads, scene compiler contracts, stage providers, media playback contracts, output models, overlays, automation, and diagnostics. Ordinary persistence services and UI query services should stay in `Services/`.

### `apps/windows/ChurchPresenter.Core`

```text
ChurchPresenter.Core/
  Cpres/
    CpresBundleReader.cs
    CpresBundleWriter.cs
    CpresManifest models
    Resource and package primitives

  Resources/
    Portable resource messages or data needed by core file-format code

  Serialization/
    Optional future home for reusable portable serialization helpers
```

Keep `ChurchPresenter.Core` free of application workflow services. If code knows about live sessions, Looks, playlists, output screens, settings, monitors, media playback, or diagnostics workflows, it belongs in `ChurchPresenter.Application`.

## Feature Area Ownership Map

| Feature area | WinUI host ownership | Application/runtime ownership | Core ownership | Test ownership |
|---|---|---|---|---|
| Show | `Views/Show`, `ViewModels/Show`, show cards, sidebars, clear bars, media drawer UI, keyboard/focus behavior | Show read models, playlist occurrence queries, slide/media command creation, live-session snapshots, clear groups, prepared cues | None unless reading `.cpres` content | App tests for show queries and commands; host tests for command dispatch/accessibility |
| Editor | Editor pages, canvas host, adorners, selection handles, drag/drop, dialogs, keyboard gestures | Presentation document mutation, object models, validation, scene invalidation, action edits | Portable document primitives | App tests for document mutations; host tests for editor canvas apply/selection behavior |
| Reflow | Text-first WinUI surface, grouping/arrangement UI, split/combine gestures | Reflow projection, text mutation workflow, group/arrangement preservation, provenance | Portable document primitives | App tests for text workflow and document round trips |
| Themes | Theme library UI, preview controls, theme picker dialogs | Theme records, theme application, variant resolution, generated-content templates | Portable theme document primitives if needed | App tests for theme resolution and persistence |
| Output Setup | Display topology UI, Looks editor, stage layout assignment UI, endpoint binding dialogs | Logical screens, Looks, clear groups, stage layout records, output routing, endpoint read models | None | App tests for routing; host tests for display binding/window lifecycle |
| Settings and Support | Settings pages, progressive disclosure, package/import/export dialogs, diagnostics UI | Settings services, support packages, sync/migration, content bootstrap, audit/repair, diagnostics/recovery queries | Portable package primitives | App tests for settings, audit, packages, migration |
| Media and Audio | Media drawer, thumbnail previews, import/relink pickers, host media players, audio controls | Media assets, media/audio cues, cue playlists, playback state semantics, markers, resource stamps | Portable media metadata only if stored in bundle format | App tests for media graph and cue behavior; host tests for player adapter feedback |
| Live Video and Capture | Camera/device pickers, preview surfaces, recording/streaming UI, Windows device permissions | Logical live-video inputs, capture sessions, frame consumer contracts, health models | None | App tests for contracts; host/integration tests for devices where practical |
| Integrations | Sign-in prompts, account state UI, import wizards, progress and conflict dialogs | Planning Center-style plans, SongSelect/CCLI, MultiTracks, Bible providers, ProContent-like sources, reporting/provenance, credential state models | None | App tests with fake providers; host tests for UX wiring |
| Diagnostics | Host diagnostics pages, support-package export UI, failure notifications | Runtime diagnostics, command provenance, content health, recovery actions, package/sync conflicts | Core file-format diagnostics where portable | App tests for diagnostic classification; host tests for surfaced recovery |
| Rendering hosts | WinUI scene/frame application, output windows, previews, thumbnails, editor canvas adapters | Scene compiler, immutable scene/frame/payload contracts, frame resolver, diagnostics, cache stamps | Portable render data only when saved in content packages | App tests and rendering benchmarks; host tests for adapter apply |
| Platform adapters | Display, file picker, media player, capture, credentials, notifications, accessibility, packaging | Adapter contracts, capability models, health/feedback query models | None | Host adapter tests/fakes; app tests for capability interpretation |
| Automation and control | Keyboard shortcut UX, local macro buttons, future remote status pages | Live command API, macro expansion, playback marker events, timecode/calendar/device event translation, authorization/source metadata | None | App tests for command expansion and source metadata |

## Dependency Direction

Use one-way dependencies:

```text
ChurchPresenter (WinUI host)
  -> ChurchPresenter.Application
      -> ChurchPresenter.Core
```

Allowed call patterns:

- Page and control event handlers call view model methods.
- View models call application services, read-model query services, and command dispatch/facade services.
- Host adapters implement application-defined contracts or consume application-defined frame/read models.
- Application services call core bundle/document primitives.
- Application services expose DTOs, commands, read models, diagnostics, and adapter contracts back to the host.

Disallowed call patterns:

- `ChurchPresenter.Application` referencing XAML, WinUI controls, `DispatcherQueue`, Windows pickers, Windows media player controls, `Window`, `AppWindow`, or WinRT UI types.
- Output windows reading presentations, catalogs, content roots, or media folders directly.
- View models serializing documents or rewriting support files directly.
- Host media controls deciding live player truth independently from runtime playback state.
- Render controls resolving Looks, stage layouts, dynamic tokens, or clear groups.

## Naming Conventions

Use names that describe product responsibility, not UI placement alone.

Recommended suffixes:

- `*Page` for navigable WinUI pages.
- `*Window` for top-level windows and output windows.
- `*Dialog` for XAML-backed `ContentDialog` types; `*DialogContent` for reusable dialog content hosted by a configured `ContentDialog`.
- `*Flyout` or `*FlyoutContent` for flyout-specific views.
- `*Control` for reusable XAML controls with templates or reusable behavior.
- `*ViewModel` for WinUI-facing state and commands.
- `*ItemViewModel`, `*SectionViewModel`, and `*Display` for bindable row/card/chip models.
- `I*Service` and `*Service` for application or host services.
- `I*Adapter` and `WinUi*Adapter` for host implementations of platform bindings.
- `*Dto` for persistence or transport-shaped data.
- `*Models` for cohesive sets of related records in the application layer.
- `*Contracts` for records/interfaces that define cross-layer runtime contracts.
- `*QueryService` for read-only read-model surfaces.
- `*Facade` for a small, explicit orchestration API over multiple runtime services.

Namespace guidance:

- Namespaces should match folder structure for new or moved code.
- Because the current application assembly uses the `ChurchPresenter` root namespace, avoid using namespace alone to infer layer ownership. Prefer clear folder placement and project references.
- If a future namespace cleanup is scheduled, migrate toward assembly-qualified roots such as `ChurchPresenter.Application.Services.Content` and `ChurchPresenter.WinUI.Views.Show` in one deliberate phase, not opportunistically across unrelated feature work.

## File Splitting Patterns

Split by responsibility and workflow, not by arbitrary size.

### Pages

Use this pattern for complex pages:

```text
Views/Show/
  ShowPage.xaml
  ShowPage.xaml.cs
  ShowPage.DeckToolbar.partial.cs
  ShowPage.MediaPanel.partial.cs
  ShowPage.Flyouts.partial.cs
  Dialogs/
  Flyouts/
```

Rules:

- XAML owns visual hierarchy, visual states, resources, and bindings.
- Code-behind owns WinUI event adaptation, focus restoration, dialog/flyout opening, drag/drop bridge code, and control-specific workaround code.
- Code-behind should not own production semantics, persistence decisions, live routing, or document mutation rules.
- Partial code-behind files are acceptable when each partial maps to a visible page region or host-only interaction group.

### View Models

Use partial view models only when each file is a cohesive workflow:

```text
ViewModels/Show/
  ShowViewModel.cs
  ShowViewModel.Playlist.partial.cs
  ShowViewModel.Deck.partial.cs
  ShowViewModel.MediaPanel.partial.cs
  ShowViewModel.OutputPreview.partial.cs
  ShowViewModel.ClearActions.partial.cs
  ShowViewModel.Transitions.partial.cs
```

Rules:

- The root file holds constructor dependencies, lifecycle, top-level state, and initialization.
- Partials group command handlers and observable state for one page region.
- Reusable item models move into their own files.
- If a partial starts doing persistence or graph manipulation, extract an application service.
- If a partial mostly maps runtime read models to bindable display state, consider a small `*Presenter`, `*Mapper`, or query model in the application layer when it is reusable outside the host.

### Services

Prefer one interface per application service boundary when the boundary is consumed across layers or heavily tested.

Use partial service classes only when a service has one durable responsibility with multiple internal slices, such as persistence, repair, coalescing, or graph traversal. Do not use partials to hide unrelated responsibilities in one type.

Recommended split:

```text
Services/Content/
  ContentDirectoryService.cs
  ContentStore.cs
  ContentAuditService.cs
  ContentDiagnosticsQueryService.cs

Services/Show/
  ShowContentBrowseService.cs
  ShowMediaDrawerService.cs
  ShowTransitionDefaults.cs

Services/Runtime/
  LiveProductionFacade.cs
  LiveProductionQueryService.cs
  LiveDiagnosticsRecoveryQueryService.cs

Services/Output/
  OutputRoutingService.cs
  OutputTopologyService.cs
  OutputFrameFacade.cs
```

### Models And Contracts

Use one file per cohesive model family. Avoid giant catch-all model files.

Good examples:

- `PresentationDocumentModels.cs`
- `MediaLibraryModels.cs`
- `OutputRoutingModels.cs`
- `LiveProductionQueryModels.cs`
- `ContentDiagnosticsModels.cs`
- `Backend/Commands/LiveCommandModels.cs`
- `Backend/Rendering/SlideSceneModels.cs`

Split when:

- A model family crosses a product boundary such as media assets vs media cue playback.
- Persistence DTOs and runtime read models are mixed in one file.
- Host-only bindable display models have entered the application layer.
- A file combines command contracts, execution results, diagnostics, and test fakes.

## View, ViewModel, Service, And Adapter Contracts

### Views

Views should:

- Use native WinUI controls and Fluent resources first.
- Keep layout and visual states in XAML.
- Bind to view models or small item-display models.
- Forward gestures, key events, drag/drop, and dialog results to view models.
- Restore focus and accessibility state after dialogs/flyouts.

Views should not:

- Call `LiveCommandExecutor` directly unless the view is a thin host adapter with no view model path.
- Open or parse presentation documents directly.
- Read output routing from local control state.
- Treat selection as live state.

### View Models

View models should:

- Hold host-side state: selection, filters, expanded groups, editor focus, local pane sizing, active dialog/flyout state, optimistic command state, validation display.
- Depend on application service interfaces and host service interfaces through constructor injection.
- Dispatch live actions through `ILiveProductionFacade`, `ILiveCommandExecutor`, or more specific command-oriented application services.
- Subscribe to read models for live layers, output health, stage state, timers, media/player state, diagnostics, and content browse state.
- Keep commands small; complex behavior moves into services.

View models should not:

- Own authoritative live state, player state, Look routing, stage layout resolution, package/sync conflict decisions, media relink truth, or content graph rules.
- Convert files into cues without application service validation.
- Store portable settings in host-local state.
- Use Windows APIs unless the workflow is inherently host-specific and behind a host service would add no value.

### Application Services

Application services should:

- Own product semantics, persistence orchestration, graph safety, command expansion, read models, diagnostics, cache invalidation, and repair choices.
- Use async I/O for file work.
- Expose explicit contracts for host adapters rather than accepting WinUI types.
- Be testable with in-memory paths, fake clocks, fake providers, and fake host feedback.
- Classify errors by content, cue, command, routing, render, endpoint, host, transport, or integration.

Application services should not:

- Require UI threads.
- Show dialogs or notifications.
- Depend on platform display/media/picker classes.
- Treat caches as source of truth.

### Host Adapters

Host adapters should:

- Own platform APIs such as `Window`, `AppWindow`, display enumeration, file pickers, `MediaPlayer`, shell thumbnails, capture devices, credentials, notifications, and accessibility.
- Translate platform capability and health into application-defined records.
- Apply immutable `AudienceFrame`, `StageFrame`, and scene contracts.
- Report host feedback and diagnostics to runtime/application services.

Host adapters should not:

- Resolve Looks, clear groups, dynamic text providers, or stage layouts.
- Mutate documents or support files.
- Crawl content roots directly.
- Suppress runtime diagnostics because the platform appears temporarily healthy.

## Shared Runtime Vs Host-Specific Placement

Place code by the lowest layer that can own it without platform knowledge.

| Code type | Place in |
|---|---|
| `.cpres` bundle read/write, portable manifest/resource primitives | `ChurchPresenter.Core/Cpres` |
| Presentation document models, slide/group/arrangement/theme records | `ChurchPresenter.Application/Models` or `Backend/Content` |
| Live commands, action batches, live-session snapshots | `ChurchPresenter.Application/Backend/Commands` |
| Scene nodes, payloads, frames, stage provider contracts | `ChurchPresenter.Application/Backend/Rendering`, `Backend/Output`, `Backend/Stage` |
| Content registry, catalog, package, sync, audit, migration, repair | `ChurchPresenter.Application/Services` |
| Runtime read models for pages/remotes/API | `ChurchPresenter.Application/Models` and `Services/*QueryService.cs` |
| WinUI pages, windows, controls, converters, styles | `ChurchPresenter` |
| Display enumeration and output window positioning | `ChurchPresenter/Adapters/Display` or `Services` until grouped |
| Windows file pickers, shell thumbnails, drag/drop import bridge | `ChurchPresenter/Adapters/FileSystem` or host `Services` |
| `MediaPlayer`, `MediaPlayerElement`, native audio/video devices | `ChurchPresenter/Adapters/Media` |
| WinUI scene application and output surface controls | `ChurchPresenter/Controls/Rendering` and `Adapters/Rendering` |
| XAML-backed dialogs and flyouts | `ChurchPresenter/Views/<Feature>/Dialogs` or `Flyouts` |
| App lifecycle, DI bootstrap, navigation registration | `ChurchPresenter/Hosting`, `App.xaml.cs`, `MainWindow.xaml.cs` |
| Accessibility peers and keyboard focus helpers | `ChurchPresenter/Accessibility` or feature controls |
| Packaged app activation/deployment helpers | `ChurchPresenter/Packaging` |

## Reusable Controls

Create reusable controls when:

- The same visual/interaction pattern appears in multiple workflows.
- The control owns meaningful keyboard/focus/accessibility behavior.
- The control is a host adapter for a scene, frame, media slot, output preview, or editor canvas.
- XAML complexity is obscuring a page's workflow structure.

Keep controls in feature-specific folders unless they are genuinely shared:

```text
Controls/
  Common/
    EmptyStateControl.xaml
    SectionHeaderControl.xaml
  Show/
    SlideCardControl.xaml
    MediaCueCardControl.xaml
    ClearGroupButton.xaml
  Editor/
    EditorCanvasControl.xaml
    LayerListControl.xaml
  Output/
    OutputPreviewControl.xaml
    EndpointHealthBadge.xaml
  Rendering/
    WinUiSceneHost.cs
```

Reusable controls may expose dependency properties and visual states, but commands should still flow through the owning view model or application service. Do not create a control that becomes a hidden service locator or a second live runtime.

## Dialogs And Flyouts

Use XAML-backed dialogs for complex forms:

```text
Views/Settings/Dialogs/
  ContentRootRepairDialog.xaml
  ContentRootRepairDialog.xaml.cs

Views/Show/Flyouts/
  ClearGroupsFlyoutContent.xaml
  ClearGroupsFlyoutContent.xaml.cs
```

Rules:

- Configure `ContentDialog` with the page `XamlRoot` using the shared dialog setup helper.
- Keep form layout in XAML and validation state in a view model or dialog model.
- Dialog result handlers should call application services for mutations.
- Flyouts may be lightweight, but anything with save/apply behavior should have an explicit model.
- Do not bury portable schema or migration logic in dialog code-behind.

## Commands And Read Models

All live-production actions should converge on typed commands or command-oriented services:

- Slide activation.
- Media/audio cue trigger.
- Live video input activation.
- Clear layer and clear group.
- Look changes.
- Stage layout changes.
- Timer/message/prop/macro operations.
- Capture/stream start, stop, and recovery.
- Keyboard shortcuts.
- Slide actions, playback markers, timeline/timecode/calendar events.
- Future Stream Deck, MIDI, DMX, Network Link, HTTP/TCP API, browser control, and mobile remote actions.

Read models should be explicit and query-oriented:

```text
IShowContentBrowseService
IShowMediaDrawerService
ILiveProductionQueryService
ILiveDiagnosticsRecoveryQueryService
IOutputHostFeedbackQueryService
IContentDiagnosticsQueryService
```

Avoid having a page infer live or output state by reading another page's view model. Shared state belongs in application read models.

## DTOs And Persistence Models

Use DTOs for persisted or package-shaped data and read models for UI/API-shaped data. Do not reuse a bindable WinUI item model as a persisted schema.

DTO guidance:

- Include schema version and stable ids where the record is persisted.
- Separate portable content from machine-local bindings.
- Keep resource stamps with documents/assets/caches that depend on files.
- Preserve provenance for generated/imported content.
- Use explicit missing/degraded states rather than null meaning every failure mode.
- Keep DTOs free of brush, color resource, `ImageSource`, `Visibility`, `Thickness`, and other UI-only types.

Host display models may include UI-friendly fields such as glyphs, localized labels, computed group headers, and theme-resource keys. They should not be saved as content truth.

## Rendering Hosts

Use the rendering architecture's scene/frame boundary:

```text
Application layer:
  SlideSceneCompiler
  BackendRenderEngine
  AudienceFrame / StageFrame contracts
  Render diagnostics

WinUI host:
  WinUiSceneHost
  Audience output adapter
  Stage output adapter
  Operator preview adapter
  Thumbnail adapter
  Editor canvas adapter
```

Recommended placement:

```text
ChurchPresenter.Application/Backend/Rendering/
  RenderModels.cs
  SlideSceneModels.cs
  SlideSceneCompiler.cs
  BackendRenderEngine.cs

ChurchPresenter/Controls/Rendering/
  WinUiSceneHost.cs
  Scene visual helpers

ChurchPresenter/Adapters/Rendering/
  WinUiAudienceOutputAdapter.cs
  WinUiStageOutputAdapter.cs
  WinUiOperatorPreviewAdapter.cs
  WinUiThumbnailAdapter.cs
  WinUiEditorCanvasAdapter.cs
```

Adapter rules:

- Apply immutable scene/frame inputs.
- Cache native visuals only while source resource stamps remain valid.
- Report unsupported effects and host apply failures as diagnostics.
- Keep editor adorners host-only.
- Do not resolve dynamic text, Looks, clear behavior, or stage providers in WinUI.

## Media, Audio, And Output Endpoints

Separate asset identity, cue usage, player state, and endpoint delivery.

Recommended placement:

```text
ChurchPresenter.Application/Backend/Media/
  MediaContracts.cs
  MediaCueContracts.cs
  MediaPlaybackContracts.cs
  MediaPlaylistContracts.cs

ChurchPresenter.Application/Services/Media/
  MediaLibraryService.cs
  CuePreparationService.cs
  LiveMediaQueryService.cs

ChurchPresenter/Adapters/Media/
  WinUiMediaPlaybackAdapter.cs
  WindowsMediaMetadataReader.cs
  WindowsThumbnailProvider.cs
  WindowsCaptureDeviceAdapter.cs

ChurchPresenter/Adapters/Display/
  WindowsDisplayCatalogAdapter.cs
  WindowsOutputWindowAdapter.cs
```

Rules:

- Media Bin and Audio Bin are cue systems, not raw file browsers.
- A clicked media file should become a validated cue/command path before reaching live output.
- `MediaPlayer` and `MediaPlayerElement` are host implementation details.
- Runtime state records owning layer/cue/action, transport state, marker events, routing, and diagnostics.
- Output endpoints consume resolved frames and report capability/health. They do not render from documents directly.
- Capture, recording, streaming, NDI/SDI, and future transports should consume resolved screen frames.

## Platform Adapters

Create explicit adapter contracts when platform behavior can affect product state, diagnostics, or tests.

Adapter families:

- Display/window management.
- File and folder picking, drag/drop import, shell thumbnails, OneDrive/removable/network path handling.
- Media playback and metadata.
- Audio routing and test diagnostics.
- Live video input and capture devices.
- Recording, streaming, NDI/SDI/future transport endpoints.
- Credentials and account token storage.
- Notifications and app lifecycle.
- Accessibility and UI Automation metadata for custom controls.
- Packaged deployment, activation, update, and support-package metadata.

Application-defined contracts should use portable capability and health records. Windows implementations may live in `ChurchPresenter/Adapters/*` or in `ChurchPresenter/Services` until a feature area is large enough to justify a folder move.

## Tests, Fixtures, And Benchmarks

Organize tests by product boundary:

```text
tests/ChurchPresenter.App.Tests/
  Backend/
    Command, render, output, stage, media contract tests
  Services/
    Content, media, output, runtime, settings, support, package tests
  Rendering/
    Scene compiler and render diagnostics tests
  TestSupport/
    Builders, fake services, temporary content roots, sample bundles

tests/ChurchPresenter.Core.Tests/
  Cpres bundle and portable resource tests

tests/ChurchPresenter.Rendering.Benchmarks/
  High-node-count scenes
  Layer/frame resolution
  Thumbnail/preview cache scenarios
```

Guidance:

- Put semantic tests in `ChurchPresenter.App.Tests` before adding host tests.
- Use test builders for presentations, slides, media assets, Looks, stage layouts, timers, and diagnostics.
- Keep fixtures under `shared/` or `tests/*/Fixtures` when they are test data, not runtime content.
- Benchmark the scene compiler and frame resolver when adding rich effects, many layers, stage previews, or media-heavy scenes.
- Host tests should focus on command dispatch, adapter application, output window lifecycle, accessibility, focus restoration, and platform failure reporting.

## Documentation Placement

Use docs by concern:

- Shared product semantics and cross-platform contracts: `docs/architecture/*.md`.
- Windows-specific platform implementation: `docs/architecture/platforms/winui-native-app.md`.
- Windows code/file organization: this document.
- ProPresenter behavior research: `docs/reference/propresenter/features/*.md` and deep reference folders.
- Migration plans with concrete tasks: `docs/architecture/` or `docs/migration/`, depending on scope.
- User-facing or operator guides: a separate docs area, not architecture.

Do not duplicate platform-agnostic rules here. Link to the shared architecture and explain how the Windows codebase should honor it.

## Anti-Patterns To Avoid

Avoid these patterns as the app grows:

- A page or view model becoming the source of live production truth.
- Output windows deciding what to show by reading selected UI state.
- Treating selected slide, live slide, preview frame, and endpoint health as one concept.
- `ShowViewModel` or any page view model becoming a persistence service, output router, media player registry, and content graph owner at the same time.
- Duplicating Look routing in WinUI controls.
- Clearing layers by hiding local controls instead of dispatching runtime clear commands.
- Using file paths as durable media identity without resource stamps.
- Making thumbnails, shell previews, extracted bundle files, or caches authoritative.
- Binding strongly typed commands to ambiguous XAML command parameters when item-owned commands would be safer.
- Adding host-specific DTO fields such as brushes, `Visibility`, or `ImageSource` to persisted application models.
- Adding Windows App SDK or WinUI references to `ChurchPresenter.Application`.
- Adding new projects or abstractions before a concrete dependency or reuse boundary exists.
- Keeping duplicate legacy surfaces alive after the runtime read model and adapter path replaces them.
- Hardcoding contrast-critical colors in reusable controls instead of using theme resources.
- Creating generic controls that secretly know about content roots, live sessions, or service locators.

## Phased Migration From Current Shape

The current app already has useful pieces: the three-project solution, `Backend/` runtime contracts, `Services/` application services, WinUI host `Views/`, `ViewModels/`, `Controls/`, `Services/`, and early render host code such as `Controls/Rendering/WinUiSceneHost.cs`.

Migrate in phases so feature work remains reviewable.

### Phase 1: Document And Enforce Boundaries

- Keep `ChurchPresenter.Application` free of WinUI/XAML references.
- Keep host-only APIs in `ChurchPresenter`.
- Add new runtime behavior to `Backend/` and application services instead of page code-behind.
- Register new services in grouped `AppServices` extension methods by product area as registrations grow.
- Add tests for new application services before or alongside host UI changes.

### Phase 2: Group Current Flat Folders By Feature

- Move Show page/view model partials toward `Views/Show` and `ViewModels/Show`.
- Move settings pages toward `Views/Settings` and `ViewModels/Settings`.
- Move output pages/windows toward `Views/OutputSetup` or `Views/OutputHosts`.
- Move reusable output/render controls toward `Controls/Output` and `Controls/Rendering`.
- Move host services that wrap Windows platform APIs toward `Adapters/Display`, `Adapters/Media`, `Adapters/FileSystem`, and `Adapters/Rendering`.

Perform these moves in focused PRs with namespace updates and build verification. Do not mix folder moves with major semantic rewrites unless the rewrite requires the move.

### Phase 3: Split Oversized Workflow Types

- Keep `ShowViewModel` partials, but ensure each partial maps to one visible region or workflow.
- Extract application services when partials own content graph, media graph, output routing, or persistence logic.
- Extract item view models and bindable display records from large page view models.
- Convert complex dialogs to XAML-backed dialog content with explicit models.
- Move page-only helper records beside the page; move reusable helper records to controls or application models.

### Phase 4: Complete Runtime Command And Query Paths

- Route slide activation, media/audio triggers, clear actions, Look changes, stage layout changes, macros, timers, capture, and keyboard shortcuts through shared commands or command-oriented facades.
- Replace page-to-page shared state with application read models.
- Keep clear groups, layer suppression/recovery, active Look, stage assignments, and player state in runtime snapshots/read models.
- Prepare the same command/query path for future remotes, browser control, device adapters, and automation.

### Phase 5: Formalize Host Adapters

- Promote display, output window, media playback, file access, rendering, capture, credentials, notifications, accessibility, and packaging wrappers into explicit adapters.
- Define portable capability/health feedback records in `ChurchPresenter.Application`.
- Make output windows, previews, thumbnails, editor canvas, and capture consumers apply resolved frames instead of rendering directly from selected content.
- Ensure adapter failures flow into diagnostics and support-package export.

### Phase 6: Retire Duplicate And Legacy Paths

- Remove host-specific render logic that duplicates scene/frame contracts.
- Remove direct document or content-root access from pages/output windows.
- Consolidate duplicate models and helper methods across host/application/core.
- Delete compatibility shims for unshipped branch-only shortcuts that conflict with the shared architecture.
- Keep only migration compatibility needed for shipped persisted content and user data.

## Review Checklist

Before merging a Windows app feature or refactor, ask:

- Does the code live in the lowest layer that can own it?
- Does any application/core code reference WinUI, XAML, Windows App SDK UI, or WinRT UI APIs?
- Are live state, selection, preview, and endpoint health still separate?
- Are live actions routed through shared commands or command-oriented services?
- Are document mutations handled by document/content services?
- Are media files modeled as assets/cues instead of raw UI file paths?
- Are output windows and previews consuming resolved frames?
- Are portable content, machine-local bindings, and caches stored separately?
- Are DTOs free of UI-only types?
- Are new controls accessible, keyboard reachable, and theme-resource based?
- Are tests placed at the semantic boundary first, with host tests only for host behavior?
- Did the change avoid editing platform-agnostic docs unless the shared contract actually changed?
