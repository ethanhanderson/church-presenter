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

### Repository configuration files

Keep only configuration that affects real builds or product behavior:

| File | Role |
| --- | --- |
| `global.json` | Pins the .NET SDK (currently **10.0.301**). |
| `.editorconfig` | C# / XAML style and analyzer severities for the whole repo. |
| `apps/windows/ChurchPresenter/ChurchPresenter.csproj` | WinUI host TFM, Windows App SDK packages, MSIX tooling, publish profiles. |
| `apps/windows/ChurchPresenter.Application/*.csproj`, `ChurchPresenter.Core/*.csproj`, `tests/**/*.csproj` | Per-project TFMs and package references. |

Avoid repo-level `Directory.Build.props`, checked-in `*.binlog` / `build-out.txt` / `.agent-build.log` files, and other dev-only MSBuild overlays unless they solve a concrete cross-project build requirement. Agent E2E output under `.agent-logs/` is gitignored scratch, not product configuration.

Packaged build/run scripts live under `scripts/windows/` — only the core winapp workflow (`Build-PackagedApp`, `Run-PackagedApp`, `Stop-ChurchPresenter`, `Invoke-AgentE2EVerification`, `Install-WinAppCli`, `Unregister-PackagedApp`, plus `ChurchPresenter.Paths.ps1`). One-off refactor or bisect helpers do not belong there.

Portable format and UX reference docs live under `docs/architecture/` (`formats/cpres-v1.md`, `design-references.md`). Tests use inline or temp data — no checked-in fixture trees at repo root.
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
    ShellNavigation.cs
    ShellNavigationCoordinator.cs
    NavigationService.cs
    ServiceCollection extension files by area

  Resources/
    Theme dictionaries, styles, localized strings, icons, app metadata

  Features/
    Show/                         # target: segmented Show feature (see "Show feature organization")
      Shell/
      Sidebar/
      Deck/
      BrowseStack/
      Output/
      MediaDrawer/
      ShowControls/
      Transitions/
      Dialogs/
      Arrangements/
      Hosting/
      Components/

  Views/
    Show/                         # current: monolithic page (migration source)
      ShowPage.xaml               # ~4.4k lines XAML — region hosts only after reorg
      ShowPage.xaml.cs
      ShowPage.*.partial.cs
      Panels/
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

The current `Backend/`, `Models/`, and `Services/` layout is the right high-level shape. Over time, group service implementations by product area while keeping public contracts easy to find.

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
| Show | `Features/Show` (target) or `Views/Show` + `ViewModels/Show` (current): sidebar tree, slide deck, browse stack, output preview/clears, media drawer, transition toolbar, Show Controls panel, dialogs/flyouts, deck thumbnails, selection chrome, keyboard/focus, drag/drop coordinators | `Services/Show` (browse, deck actions, media drawer, controls, workspace), `Services/Pipeline` (`ICueDispatcher`, `ILiveSessionQueryService`, `CuePipelineRuntime`), live-session snapshots, clear groups, prepared cues, transition defaults | None unless reading `.cpres` content | App tests for show queries, pipeline dispatch, and commands; host tests for navigation lifecycle, thumbnail phases, and command dispatch/accessibility |
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

## Show Feature Organization

Show is the only **cached** top-level page (`NavigationCacheMode.Required`). It is also the largest WinUI surface in the app today: `ShowPage.xaml` (~4.4k lines) plus partial code-behind (~7.7k lines total), a singleton `ShowViewModel` with partials (~4.4k lines), and roughly twenty application services injected into `ShowPage` code-behind in addition to the view model — a **dual-authority** pattern where both the page and the VM can mutate workflow state.

Runtime contracts for cached Show reload are documented in `docs/architecture/platforms/winui-native-app.md` ("Top-level navigation and page state model"). Organization guidance here complements those contracts: split by **operator subsystem**, keep compositor-sensitive lifecycle in `Hosting/`, and route live actions through `ICueDispatcher` / read models through `ILiveSessionQueryService`.

### Functional Subsystems (current behavior)

| Subsystem | Operator responsibility | Primary host ownership (current) | Application/runtime ownership |
|---|---|---|---|
| Sidebar | Libraries and playlists tree, search, inline rename, drag/drop reorder and import | `ShowPage` sidebar region + `ShowSidebarTreeViewModels` | `IShowContentBrowseService`, `IWorkspaceService`, clipboard services |
| Slide deck | Slide cards, live/selection chrome, group footers, deck toolbar, keyboard navigation | `ShowPage` deck region + `ShowSlideDeckItem` + `ShowViewModel` deck partials | `ISlideItemActionService`, `ICueDispatcher`, `ILiveSessionQueryService` |
| Browse stack | Multi-presentation horizontal browse, section headers, auto-advance | `ShowPage` browse region + `ShowPresentationDeckSection` | `IShowContentBrowseService`, `ActivePresentationService` |
| Transitions | Global/per-slide/per-media duration and transition pickers | `ShowPage` toolbar + `ShowViewModel.TransitionToolbar` + flyouts | `IShowTransitionDefaults`, `ShowTransitionToolbar`, theme transition resolver |
| Media drawer | Media catalog cards, click-to-media-layer, drag-to-slide actions | `ShowPage.MediaPanel` partial + `MediaPanelItemViewModel` | `IShowMediaDrawerService`, `IMediaLibraryService`, `ICueDispatcher` |
| Output preview & clears | Program preview slot, layer clear bars, clear-group flyout | `ProgramOutputSurface` (layout slot) + shell `OperatorPreviewSession` preview-sized overlay + clear partials | `ILiveSessionQueryService`, `IOutputRoutingService`, clear commands via `ICueDispatcher`; preview raster via `IProgramRenderService.EnsurePreviewSurface` |
| Show Controls | Audio/stage/timers/messages panel | `ShowControlsPanelView` + `ShowViewModel.ShowControls` | `IShowControlsService`, `IShowTimerService`, stage query projections |
| Arrangements | Arrangement picker, grouping, palette | Dialogs + `ShowViewModel.ArrangementsAndPlayback` | Document/arrangement services |
| Thumbnails | Deck card previews, navigation pixel restore, off-frame warm | `IShowDeckThumbnailService`, `ISlideDeckThumbnailCache`, `ThumbnailEngine` | Scene compile contracts; raster gated by `IShowRenderSession` |
| Selection model | Ctrl/shift click select-only; normal click take-live; outside click clears selection | `ShowPage` input handlers + deck item state | Selection is host-side; live chrome from `ILiveSessionQueryService` |
| Workspace persistence | Favorites, recents, toolbar transitions, pane sizes | `ShowViewModel` + `ShowPage` layout state | `ISettingsService`, `IWorkspaceService`, `ShowSessionCache` |

### Current Organization Problems

Avoid perpetuating these patterns in new Show work:

- **God files**: monolithic `ShowPage.xaml` and singleton `ShowViewModel` own unrelated subsystems.
- **Code-behind business logic**: document mutations, browse graph edits, and live routing decisions live in `ShowPage` partials instead of application services or child view models.
- **Dual authority**: ~20 services injected into `ShowPage` while `ShowViewModel` also holds overlapping workflow state.
- **Models in `Views/`**: arrangement/picker display records (`ArrangementGroupEntry`, `TransitionPickerModels`) live beside pages.
- **Namespace inconsistency**: `ChurchPresenter.Views` for `ShowPage` vs `ChurchPresenter.ViewModels.Show` for deck models.
- **Service locator**: `ShowControlsPanelView` resolves `ShowViewModel` via `App.Services` instead of constructor injection.
- **Application layer leakage risk**: deck item models must not reference `Microsoft.UI.*` types.

### Target Folder Structure

Migrate toward a feature-rooted tree under `ChurchPresenter/Features/Show/` (namespaces should follow folders on move):

```text
Features/Show/
  Shell/
    ShowPage.xaml                 # ≤300 lines: region hosts + lifecycle hooks only
    ShowPage.xaml.cs
    ShowCoordinatorViewModel.cs     # thin: child VM wiring, navigation callbacks
  Sidebar/
    ShowSidebarView.xaml
    ShowSidebarViewModel.cs
    SidebarDragDropCoordinator.cs
  Deck/
    ShowDeckView.xaml
    ShowDeckViewModel.cs
    SlideCardControl.xaml           # or Controls/Show when shared
    DeckInputCoordinator.cs
  BrowseStack/
    BrowseStackView.xaml
    BrowseStackViewModel.cs
  Output/
    ShowOutputColumnView.xaml       # preview slot chrome, clear bars, transport
    ShowOutputViewModel.cs
  MediaDrawer/
    MediaDrawerView.xaml
    MediaDrawerViewModel.cs
  ShowControls/
    ShowControlsPanelView.xaml      # inject VM; no App.Services
    ShowControlsViewModel.cs
  Transitions/
    TransitionToolbarView.xaml
    TransitionToolbarViewModel.cs
    Flyouts/
  Dialogs/
    NewPresentationDialog.xaml
    ArrangementsDialogContent.xaml
  Arrangements/
    ArrangementViewModel.cs
    (portable display records — not in Views/)
  Hosting/
    ShowRenderSession.cs            # move from Hosting/ when Show-owned
    ShowDeckThumbnailService.cs
    SlideDeckThumbnailCache.cs
  Components/
    Show-specific reusable controls not yet promoted to Controls/Common
```

Parallel application-layer grouping under `ChurchPresenter.Application/Services/Show/`:

```text
Services/Show/
  Browse/
  Sidebar/
  Deck/
  MediaDrawer/
  Controls/
  Queries/
```

`ChurchPresenter/Hosting/` retains **shell-wide** navigation (`ShellNavigation`, `NavigationService`, `ShellNavigationCoordinator`). Show-specific render-session and thumbnail orchestration may live under `Features/Show/Hosting/` once the feature folder exists.

### Show File Placement Rules

- **ShowPage shell**: navigation lifecycle (`OnNavigatedTo`/`Loaded`/`Unloaded`), `TeardownForNavigation` delegation, `IShowRenderSession` phase callbacks, and region hosts only — no subsystem markup beyond `ContentControl`/`UserControl` placeholders.
- **No direct service injection in `ShowPage`**: child feature views receive view models via DI; the coordinator resolves children once.
- **Child feature view models**: scoped or transient per subsystem; register in `Hosting/` service extensions — not a second singleton god VM.
- **Drag/drop**: coordinators (`*DragDropCoordinator`) own WinUI `DragDrop` events and call application services; keep testable without a loaded page.
- **Live take / clear / transport**: always `ICueDispatcher` (or typed command services), never `CuePipelineRuntime` from WinUI.
- **Live chrome / clear active state**: subscribe to `ILiveSessionQueryService`; update stable item view models in place.
- **Preview**: `OperatorPreviewSession` on `MainWindow` overlay; `ProgramOutputSurface` is a layout placeholder — no mirror brush on the cached page.
- **Thumbnails**: `IShowDeckThumbnailService` + `IShowRenderSession.ThumbnailRasterPhase`; never raster from `ItemsRepeater` property getters during navigation transitions.
- **Portable models**: arrangement/transition/browse DTOs belong in `ChurchPresenter.Application/Models` or feature VM folders — not `Views/Show/`.
- **Reusable controls**: promote to `Controls/Show/` when used outside one feature subfolder.

### Show Page Reorganization (phased migration)

Execute in order. Each phase should compile and ship independently; do not big-bang rename namespaces.

**Phase 1 — Extract regions to `UserControl`s (host layout only)**

- Carve `ShowPage.xaml` into region hosts: `ShowSidebarView`, `ShowDeckView`, `BrowseStackView`, `ShowOutputColumnView`, `MediaDrawerView`, `TransitionToolbarView`.
- Move matching XAML and code-behind from partials into each control's `.xaml.cs`.
- Leave data context as the existing `ShowViewModel` initially.
- Goal: `ShowPage.xaml` ≤300 lines; no behavior change.

**Phase 2 — Split view model**

- Introduce subsystem view models (`ShowDeckViewModel`, `ShowSidebarViewModel`, etc.) and a thin `ShowCoordinatorViewModel`.
- Move partial files (`ShowViewModel.MediaPanel`, `ShowViewModel.ClearActions`, …) into matching child VMs.
- `ShowViewModel` becomes coordinator or is replaced by `ShowCoordinatorViewModel`; register child VMs in DI.
- Move `App.Services` lookups in deck items/thumbnails to injected services or coordinator callbacks.

**Phase 3 — Move code-behind to coordinators**

- Extract drag/drop, context menus, inline rename, splitter drag, and focus restoration into `*Coordinator` or `*InputBehavior` classes per subsystem.
- Route mutations through existing application services (`IShowContentBrowseService`, `ISlideItemActionService`, …).
- Remove direct service fields from `ShowPage`; page forwards events to coordinator/child VM only.
- Fix `ShowControlsPanelView` service-locator pattern.

**Phase 4 — Feature folder move**

- Relocate `Views/Show/*`, `ViewModels/Show/*` (split VMs), and Show hosting services into `Features/Show/<Subsystem>/`.
- Align namespaces with folders (`ChurchPresenter.Features.Show.Deck`, etc.).
- Group `ChurchPresenter.Application/Services/Show/` into `Browse/`, `Sidebar/`, `Deck/`, `MediaDrawer/`, `Controls/`, `Queries/`.
- Update `Controls/Show/` only for controls shared across Show subsystems or other features.

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
- Partial code-behind files are acceptable when each partial maps to a visible page region or host-only interaction group — **interim only** for Show until Phase 1 region extraction (see "Show feature organization").
- Target: complex pages like Show use ≤300-line shell XAML with `UserControl` region hosts under `Features/Show/`.

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
  Browse/
    ShowContentBrowseService.cs
    ActivePresentationService.cs
  Sidebar/
    SidebarPresentationClipboardService.cs
  Deck/
    SlideItemActionService.cs
    SlideClipboardService.cs
  MediaDrawer/
    ShowMediaDrawerService.cs
  Controls/
    ShowControlsService.cs
    OperatorQueryServices.cs
  Queries/
    (projection helpers consumed by host view models)

Services/Pipeline/
  CueDispatcher.cs
  CuePipelineRuntime.cs
  LiveSessionQueryService.cs
  MediaTransportService.cs
  OperatorSelectionService.cs

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
- Dispatch live actions through `ICueDispatcher`, `ILiveCommandBus`, or more specific command-oriented application services — not ad hoc host calls into the pipeline runtime.
- Subscribe to read models from `ILiveSessionQueryService` and related query services for live layers, output health, stage state, timers, media/player state, diagnostics, and content browse state.
- Keep commands small; complex behavior moves into services.
- Preserve stable item view models for live-status command surfaces such as clear buttons. If a read-model refresh describes the same action ids in the same order, update item properties (`IsActive`, enabled state, tint/brush projection) instead of clearing and re-adding the bound collection.

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
IShowControlsService
ILiveSessionQueryService
ICueDispatcher
IOutputHostFeedbackQueryService
IContentDiagnosticsQueryService
```

Avoid having a page infer live or output state by reading another page's view model. Shared state belongs in application read models.

Query services that project live-session state should deduplicate equivalent snapshots before notifying host view models. If the projection's active layers, clear groups, Looks, transport state, endpoint health, and diagnostics are unchanged, suppress the notification. This keeps WinUI refresh, media-readiness, and routing events from rebuilding controls that are already describing the same product state.

For clear actions, use stable ids:

- Built-in layer actions: `clear-layer-{LayerKind}` or an equivalent durable id.
- Configured clear groups: the clear-group id from the support/output model.
- Membership changes: add/remove/reorder/rename/retint/retarget actions by replacing the affected collection entries.
- Status changes: update only the existing item's live-active property and command `CanExecute` state.

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

## Unified Output Pipeline And Rendering Hosts

The Windows app implements the **unified output pipeline** described in `docs/architecture/target-architecture.md` and `docs/architecture/backend-application.md`. The pipeline owns: command bus, cue preparation, prepared-cue cache, layer router, frame composer, and the host adapter contract. The WinUI host owns the platform bindings for the host adapter and the prepared resource handles.

Recommended placement for pipeline code:

```text
ChurchPresenter.Application/Backend/Pipeline/
  Commands/
    LiveCommandBus.cs                 # Channel<LiveCommand> + executor loop
    LiveCommandExecutor.cs
    ICueDispatcher.cs / CueDispatcher.cs
  Cues/
    CueRequest.cs
    CuePreparationCoordinator.cs
    PreparedCueCache.cs
  Routing/
    LayerRouter.cs
    LiveSessionSnapshot.cs
  Rendering/
    FrameComposer.cs
  HostAdapter/
    IAudienceHostAdapter.cs / IStageHostAdapter.cs
  Diagnostics/
    PipelineDiagnostics.cs

ChurchPresenter.Application/Services/Pipeline/
  CuePipelineRuntime.cs
  LiveSessionQueryService.cs
  MediaTransportService.cs
  OperatorSelectionService.cs

ChurchPresenter.Application/Backend/Rendering/
  RenderModels.cs
  SlideSceneModels.cs
  SlideSceneCompiler.cs
  SlideSceneFitter.cs              # uniform scale from presentation size to screen/preview RenderSize
  BackendRenderEngine.cs

ChurchPresenter/Controls/Rendering/
  WinUiSceneHost.cs                   # ContainerVisual root + per-layer children
  CompositionLayerSurface.cs          # persistent per-slot SpriteVisual pair
  Win2DCanvasLayer.cs                 # vector/text painted into surface brush

ChurchPresenter/Adapters/Rendering/Pipeline/
  ProgramRenderService.cs             # canonical native-resolution render per screen + optional
                                      # preview-sized hosts; implements IAudienceHostAdapter +
                                      # IStageHostAdapter; exposes GetScreenSurface (output windows)
                                      # and GetPreviewSurface (operator preview)
  OperatorPreviewSession.cs           # shell overlay; drives EnsurePreviewSurface from card layout
  WinUiThumbnailAdapter.cs            # CPU SceneCanvasView previews (illustrative)
  WinUiEditorCanvasAdapter.cs
# Mirrors: audience/stage output windows paint GetScreenSurface via ScreenMirrorBrush (Fill).
# Operator preview uses ScreenMirrorBrush.UsePreviewSurface -> GetPreviewSurface.
# Controls/Rendering/Engine: LayerStackHost, SceneSurfaceRenderer, MirrorSurfaceView,
# ScreenMirrorBrush, LayerMaskCompositor.

ChurchPresenter/Adapters/Media/
  WinUiMediaPlaybackAdapter.cs        # MediaPlayer + MediaPlaybackList
  WinUiAudioRoutingAdapter.cs         # MediaPlayer.AudioCategory per endpoint
  WinUiLiveVideoAdapter.cs            # capture frame source -> Composition
```

Pipeline rules on Windows:

- Commands enter the runtime through `LiveCommandBus` (a `System.Threading.Channels.Channel<LiveCommand>`); the bus is drained by a single executor task. UI dispatch back to the WinUI thread uses the host `DispatcherQueue`.
- `CuePreparationCoordinator` runs prepare work on the .NET thread pool; preparation may open `MediaSource`/`MediaPlaybackItem`, compile scene graphs, and warm Composition resources before the cue is taken live.
- `PreparedCueCache` is backed by `Microsoft.Extensions.Caching.Memory.IMemoryCache` with size-limited `MemoryCacheEntryOptions` per tier (hot in-memory, warm on-disk, cold on-origin). Entries declare a size cost so eviction respects the budget.
- `FrameComposer` emits per-screen `AudienceFrameSet`/`StageFrameSet` diffs that the host applies per layer slot. Unaffected slots must not re-render.
- `ProgramRenderService` applies each frame to the canonical `LayerStackHost` at the screen's topology `RenderSize` and, when the operator preview is visible, to a preview-sized host at the card layout pixels. `SlideSceneFitter` scales presentation-sized scenes before Win2D rasterization.
- Host adapters maintain persistent per-screen `ContainerVisual` roots with one child `ContainerVisual` per fixed layer slot (Audio, Messages, Props, Announcements, Slide, Media, Live Video, Mask). Transitions swap front/back `SpriteVisual` pairs via the Compositor animation system.
- Media looping uses `MediaPlaybackList` with `AutoRepeatEnabled = true` and a non-zero `MaxPlayedItemsToKeepOpen`; the host never seeks to zero for loops.
- First-frame visibility is gated on `MediaPlaybackItem.OpenOperationCompleted` and `MediaPlaybackSession` reaching `Playing` before the swap.

Adapter rules:

- Apply immutable scene/frame inputs per layer slot.
- Cache native visuals only while source resource stamps remain valid.
- Report unsupported effects and host apply failures as diagnostics with `prepareTimeMs`, `routeTimeMs`, `composeTimeMs`, `applyTimeMs`, and `mediaReadyTimeMs`.
- Keep editor adorners host-only.
- Do not resolve dynamic text, Looks, clear behavior, or stage providers in WinUI.
- Do not block the command bus thread on host work; report progress via host health events.

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
- Media drawer card identity and ordering belong in application read models; WinUI card view models should update thumbnail/readiness properties in place instead of creating the card only after `ImageSource` exists.
- Windows thumbnail providers, shell thumbnail calls, `BitmapImage`, and media-frame extraction belong in `ChurchPresenter/Adapters/Media` or host services. `ChurchPresenter.Application` should expose thumbnail status, cache keys, and resource stamps without referencing WinUI image types or Windows thumbnail APIs.

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
- Keep test-only data inline or under `tests/*/Fixtures` when a checked-in fixture is genuinely required — prefer temp directories and builders in test code.
- Benchmark the scene compiler and frame resolver when adding rich effects, many layers, stage previews, or media-heavy scenes.
- Host tests should focus on command dispatch, adapter application, output window lifecycle, accessibility, focus restoration, and platform failure reporting.

## Documentation Placement

Use docs by concern:

- Shared product semantics and cross-platform contracts: `docs/architecture/*.md`.
- Windows-specific platform implementation: `docs/architecture/platforms/winui-native-app.md`.
- Windows code/file organization: this document.
- ProPresenter behavior research: `docs/reference/propresenter/features/*.md` and deep reference folders.
- User-facing or operator guides: a separate docs area, not architecture.

Do not duplicate platform-agnostic rules here. Link to the shared architecture and explain how the Windows codebase should honor it.

## Anti-Patterns To Avoid

Avoid these patterns as the app grows:

- A page or view model becoming the source of live production truth.
- Output windows deciding what to show by reading selected UI state.
- Treating selected slide, live slide, preview frame, and endpoint health as one concept.
- `ShowViewModel` or any page view model becoming a persistence service, output router, media player registry, and content graph owner at the same time.
- Injecting many application services into `ShowPage` while also using a singleton `ShowViewModel` for the same workflows (dual authority).
- Resolving child view models through `App.Services` in nested Show controls instead of DI.
- Duplicating Look routing in WinUI controls.
- Clearing layers by hiding local controls instead of dispatching runtime clear commands.
- Rebuilding or swapping active/inactive button controls when only a layer's live-content status changed.
- Using file paths as durable media identity without resource stamps.
- Making thumbnails, shell previews, extracted bundle files, or caches authoritative.
- Binding strongly typed commands to ambiguous XAML command parameters when item-owned commands would be safer.
- Adding host-specific DTO fields such as brushes, `Visibility`, or `ImageSource` to persisted application models.
- Adding Windows App SDK or WinUI references to `ChurchPresenter.Application`.
- Adding new projects or abstractions before a concrete dependency or reuse boundary exists.
- Keeping duplicate legacy surfaces alive after the runtime read model and adapter path replaces them.
- Hardcoding contrast-critical colors in reusable controls instead of using theme resources.
- Creating generic controls that secretly know about content roots, live sessions, or service locators.

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
