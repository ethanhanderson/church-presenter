---
name: windows-app-organization-maintenance
description: Maintain the Windows app's project organization, file placement, and cleanup workflow using Microsoft Learn WinUI and .NET guidance. Use when editing `apps/windows/**`, reorganizing WinUI files, splitting large mixed-purpose files, moving logic between Windows projects, cleaning dead code or files, or reviewing the app workspace/content folder layout.
---

# Windows App Organization Maintenance

Use this skill when the Windows app has changed and needs an organization pass, cleanup pass, or project-structure review.

## Official Baseline

Anchor decisions to these Microsoft Learn principles:

- Organize .NET code as `solution -> project -> assembly -> namespace -> type`.
- Add or split projects only for concrete reasons such as reuse, separation of concerns, or dependency control.
- Keep namespaces aligned with folder structure.
- Keep WinUI shell, pages, controls, and shared resources separated instead of letting them collapse into `App.xaml.cs`, `MainWindow`, or a single large page file.
- Use a regular .NET class library for pure models, services, utilities, and persistence logic that do not depend on WinUI or XAML.
- Use a WinUI project or WinUI class library only when code needs `Microsoft.UI.*`, XAML compilation, WinUI controls, windows, or other Windows App SDK UI types.

## Project Map

Treat the Windows app stack as three layers:

- `apps/windows/ChurchPresenter/`
  WinUI host. Keep app startup, DI bootstrap, windows, pages, controls, converters, UI services, interop, and view models here.
- `apps/windows/ChurchPresenter.Application/`
  Testable application logic. Keep pure models, orchestration, storage/workspace services, document services, settings, catalog, playback, transition logic, and cleanup/migration logic here.
- `apps/windows/ChurchPresenter.Core/`
  Portable `.cpres` and low-level shared file-format code. Keep bundle read/write and related core resource code here.

Use tests to reinforce those boundaries:

- `tests/ChurchPresenter.App.Tests/` for `ChurchPresenter.Application` behavior.
- `tests/ChurchPresenter.Core.Tests/` for `.cpres` and other core portable behavior.

## Main File Locations

Inside `apps/windows/ChurchPresenter/`, prefer:

- `App.xaml` and `App.xaml.cs` for app startup, global resources, exception wiring, and top-level service bootstrapping.
- `MainWindow.xaml` and `MainWindow.xaml.cs` for shell composition, title bar, top navigation, and page hosting.
- `Views/` for pages, dialogs, windows, and closely related page-only helper types.
- `ViewModels/` for WinUI-facing state and commands that directly support pages, shell, or windows.
- `Controls/` for reusable visual building blocks.
- `Services/` for UI-specific services such as monitor/output/window coordination, clipboard helpers, and other host-only behavior.
- `Converters/` for XAML value converters.
- `Resources/` for app-facing resources and strongly typed resource accessors.
- `Interop/` for Win32 or platform interop helpers.
- `Hosting/` for DI registration/bootstrap helpers.
- `Assets/` for packaged WinUI assets.

Inside `apps/windows/ChurchPresenter.Application/`, prefer:

- `Models/` for DTOs, storage models, transition/playback models, and other pure data types.
- `Services/` for pure application services and interfaces.
- Keep these files free of `Microsoft.UI.*`, XAML code-behind, `Window`, `Page`, and view-only concerns.

Inside `apps/windows/ChurchPresenter.Core/`, prefer:

- `Cpres/` for `.cpres` bundle/document primitives and serialization.
- `Resources/` for core shared resource files only when they belong to the portable layer.

## App Workspace And Content Layout

When changing the app-managed workspace/content folder logic, preserve the current canonical layout defined by `IContentDirectoryService` and related services:

- Document content root: `Documents/Church Presenter` by default, or the machine override stored in `MachineState/ContentRootBinding.json`.
- Canonical TitleCase content folders:
  `Libraries`, `Playlists`, `Presentations`, `Configurations`, `Themes`, `Media`, `Audits`
- Expected subfolders:
  `Presentations/songs`, `Media/Files`
- App-local state:
  `%LocalAppData%/ChurchPresenter/`
- Machine-local state:
  `%LocalAppData%/ChurchPresenter/MachineState`
- Session/workspace UI state:
  `%LocalAppData%/ChurchPresenter/workspace.json`

Lowercase legacy folders exist only for migration compatibility. Do not introduce new writes to the lowercase layout unless a migration step explicitly requires it.

## Placement Rules

- Put new WinUI pages in `Views/`.
- Put new reusable controls in `Controls/`.
- Put new page-shell view models in `ViewModels/`.
- Put new pure services and interfaces in `ChurchPresenter.Application/Services/`.
- Put new pure models or DTOs in `ChurchPresenter.Application/Models/`.
- Put new `.cpres` format code in `ChurchPresenter.Core/Cpres/`.
- Keep interop code out of `ChurchPresenter.Application` and `ChurchPresenter.Core`.
- Keep namespace names consistent with folder paths.

## Splitting Rules

Split a file when one file is carrying multiple responsibilities. Common examples:

- A page code-behind file that also contains reusable models, formatting helpers, or unrelated workflow logic.
- A single service that mixes persistence, migration, validation, and UI notification logic without clear internal structure.
- A window or page file that is large because it owns multiple distinct toolbars, panels, or dialog workflows.
- A class with nested helper records/enums that are reused elsewhere and should live in their own files.

Prefer these split patterns:

- Shell/window logic stays in `MainWindow*`; page logic moves to page files under `Views/`.
- Shared visual elements move from page XAML/code-behind into `Controls/`.
- Pure data types move into `ChurchPresenter.Application/Models/`.
- Pure orchestration or storage logic moves into `ChurchPresenter.Application/Services/`.
- Portable serialization logic moves into `ChurchPresenter.Core/`.
- If a file is large but still one cohesive type, consider partial files only when each partial has a clear slice and naming stays obvious.

## Cleanup Rules

After Windows app edits, explicitly scan for:

- Unused files left behind by renames or feature moves.
- Dead helper methods and stale interfaces.
- Legacy compatibility code that is no longer read or called.
- Duplicate models or path helpers across `ChurchPresenter`, `ChurchPresenter.Application`, and `ChurchPresenter.Core`.
- New code that writes to the wrong app workspace/content location.

Delete or consolidate dead code when safe. Do not leave "temporary" duplicate files if the project no longer needs them.

## Review Procedure

1. Identify which layer each changed file belongs to: WinUI host, application logic, or portable core.
2. Check whether any changed type belongs in a different project or folder.
3. Check whether namespaces still match folders.
4. Split mixed-purpose files into smaller focused files when the boundary is clear.
5. Review workspace/content layout changes against the canonical TitleCase folder structure and machine-local state rules.
6. Remove unused code and files created by recent edits.
7. Verify with the relevant build/tests after reorganization work.

## Preferred Outcomes

- `ChurchPresenter` stays focused on WinUI host concerns.
- `ChurchPresenter.Application` remains the main home for testable business/application logic.
- `ChurchPresenter.Core` stays small and portable.
- Runtime content layout stays predictable and migration-friendly.
- Large files become smaller and more purposeful instead of accumulating more regions and unrelated helpers.
