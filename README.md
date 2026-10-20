# ChurchPresenter

A native Windows church presentation app (WinUI 3 / .NET 10) with a shared cue-and-output runtime and a portable `.cpres` document format.

I built this for my church, NCBF. Sunday operators need a desktop app that can take slides and media live, route audience and stage screens, and keep library content as files on disk.

<!-- Screenshot placeholder: Show workspace — library/playlist sidebar, slide deck, program preview -->
<!-- Screenshot placeholder: Editor — slide canvas and inspector -->
<!-- Screenshot placeholder: Audience / stage output windows -->

## What it is

ChurchPresenter is a native desktop presentation host for worship-tech operators. Behavior is modeled on [ProPresenter](docs/reference/propresenter/README.md): libraries own presentations, playlists reference them, a click takes a slide live, and output is a stack of independent layers.

The Windows app is the working host. A macOS host is specified and not started.

## Problem

NCBF's production team needs software that:

- Runs a service from a Windows machine with audience and stage outputs.
- Stores presentations as church-owned files on disk.
- Keeps operator UI and live output on separate paths, so navigating the app does not rebuild what the congregation sees.
- Can grow toward a second native host without rewriting production logic.

I started this as a Tauri app and moved it to WinUI 3 so the operator shell and output compositor could use Windows composition APIs.

## Features

What is in the Windows host today:

- **Show** — libraries and playlists in the sidebar, a slide deck, click-to-take-live, and select-without-going-live (Ctrl-click / right-click).
- **Media drawer** — click a file to the media layer; drag it onto a slide to attach a slide-linked media action.
- **Show Controls** — Audio, Macros, Messages, Props, Stage, and Timers.
- **Editor, Reflow, Theme Library, Settings** — slide editing, text-first reflow, themes, and machine/show settings.
- **Output** — audience and stage windows, operator preview, Looks routing, transitions, and clear groups.
- **Output layers** — Audio, Messages, Props, Announcements, Slide, Media, Live Video, and Mask. Clearing slide or media does not clear the other layers.
- **Documents** — library-owned `.cpres` presentations (document or bundle), plus `.cplibrary`, `.cpplaylist`, and `.cpsupport` packages.

## Architecture

Native hosts express operator intent. The shared application runtime owns live production truth.

```text
Native intent
  → command or document mutation
  → ChurchPresenter.Application runtime
  → live session snapshot
  → audience / stage frames
  → host adapters and output windows
```

| Project | Role |
| --- | --- |
| [`apps/windows/ChurchPresenter`](apps/windows/ChurchPresenter) | WinUI 3 host: windows, pages, view models, composition adapters, MSIX packaging. |
| [`apps/windows/ChurchPresenter.Application`](apps/windows/ChurchPresenter.Application) | Shared runtime: content graph, cue pipeline, live session, output routing, rendering contracts. |
| [`apps/windows/ChurchPresenter.Core`](apps/windows/ChurchPresenter.Core) | Portable `.cpres` ZIP/JSON reader, writer, and exporter. No WinUI. |
| [`apps/macos`](apps/macos) | Planned Swift/SwiftUI host. Currently a placeholder. |

The live path is a single cue pipeline under `ChurchPresenter.Application/Backend/Pipeline`: command bus, cue preparation, layer router, frame composer, host adapters. `ProgramRenderService` owns one canonical render per logical screen; audience windows, stage windows, and the operator preview are mirrors of that render.

`.cpres` is a ZIP (deflate) with `manifest.json`, `slides.json`, and `arrangement.json`. Document mode stores structure; bundle mode embeds media, fonts, and optional theme snapshots. Spec: [`docs/architecture/formats/cpres-v1.md`](docs/architecture/formats/cpres-v1.md).

Architecture map: [`docs/architecture/README.md`](docs/architecture/README.md). Windows layout: [`apps/windows/README.md`](apps/windows/README.md).

## Tech stack

| Layer | Choice |
| --- | --- |
| Language / SDK | C# / .NET 10 (`global.json` pins SDK **10.0.301**) |
| UI | WinUI 3 via Windows App SDK **2.2.0** |
| Host TFM | `net10.0-windows10.0.26100.0` (min version `10.0.17763.0`) |
| Libraries / tests TFM | `net10.0` |
| App model | Packaged WinUI (MSIX), x64 |
| MVVM / DI | CommunityToolkit.Mvvm, `Microsoft.Extensions.DependencyInjection` |
| Rendering | Windows Composition + Win2D |
| Formats | `.cpres` / `.cplibrary` / `.cpplaylist` / `.cpsupport` |

## Testing and verification

Tests are **xUnit** (`xunit` 2.9.2) with FluentAssertions and Moq.

| Project | What it covers |
| --- | --- |
| [`tests/ChurchPresenter.App.Tests`](tests/ChurchPresenter.App.Tests) | Application runtime: content, documents, media, output, pipeline, show, settings, themes, rendering, and ProPresenter-contract tests. |
| [`tests/ChurchPresenter.Core.Tests`](tests/ChurchPresenter.Core.Tests) | `.cpres` round-trip and error handling. |
| [`tests/ChurchPresenter.Rendering.Benchmarks`](tests/ChurchPresenter.Rendering.Benchmarks) | BenchmarkDotNet checks for pipeline latency and the slide scene compiler. |

From the repo root:

```powershell
dotnet test tests/ChurchPresenter.Core.Tests/ChurchPresenter.Core.Tests.csproj
dotnet test tests/ChurchPresenter.App.Tests/ChurchPresenter.App.Tests.csproj
```

Packaged verification is `scripts/windows/Invoke-AgentE2EVerification.ps1`: it runs those tests, builds the x64 Debug package, then launches the app with Microsoft **winapp CLI**. That script is the supported run path. The WinUI project is a packaged app, so launch goes through winapp CLI rather than `dotnet run` or a raw `ChurchPresenter.exe`.

## Build and run

Requirements: Windows 10/11 x64, .NET SDK 10.0.301, Windows App SDK 2.2.0, PowerShell 7 (`pwsh`). Once: `winget install Microsoft.winappcli`.

```powershell
# Build
dotnet build apps/windows/ChurchPresenter/ChurchPresenter.csproj -c Debug -p:Platform=x64
# or
pwsh -File scripts/windows/Build-PackagedApp.ps1

# Tests + packaged build + launch
pwsh -File scripts/windows/Invoke-AgentE2EVerification.ps1

# Launch only (after a successful build)
pwsh -File scripts/windows/Run-PackagedApp.ps1
```

Solution: [`apps/windows/ChurchPresenter/ChurchPresenter.slnx`](apps/windows/ChurchPresenter/ChurchPresenter.slnx).

## Status

**In progress.** The Windows host is the active app: Show, Editor, Reflow, Theme Library, Settings, the cue pipeline, canonical render, and `.cpres` I/O are in the tree and covered by tests. This is not a released product. I have not published installers. This repo does not record whether the app has been used in a live NCBF service.

The macOS app is not started. `apps/macos/` contains only a placeholder. Architecture docs for a Swift/SwiftUI host exist; there is no Xcode project.

## Roadmap

- Keep building the Windows host against the ProPresenter-derived contracts in `docs/architecture/` and `docs/reference/propresenter/`.
- Close remaining package and operator-UI gaps already called out in the format specs (support-package UI, richer playlist item kinds).
- Start the macOS host on the same application runtime and `.cpres` format.
- Add operator screenshots and a Show-flow recording to this README.

## Author

Ethan Anderson — [ehadev.com](https://ehadev.com) · [GitHub](https://github.com/ethanhanderson)
