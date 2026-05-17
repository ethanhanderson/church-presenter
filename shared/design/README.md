# Design references (non-runtime)

Use this folder for **screenshots, flow sketches, and UX notes** captured from the historical Tauri reference app under `legacy/tauri_old/` while refining the WinUI 3 UI.

## How to populate

1. Run from `legacy/tauri_old/` (`bun install`, `bun tauri dev`).
2. Capture each primary surface: **Show**, **Edit**, **Reflow**, **Themes**, **Settings**, **Output** (audience window).
3. Name files descriptively, e.g. `show-library-playlist.png`, `edit-canvas.png`.
4. Cross-link scenarios in `docs/migration/parity-matrix.md` when behavior is non-obvious.

## Missing dialog components

The repo’s `legacy/tauri_old/src/components/dialogs/` tree does not include all modules referenced by `components/dialogs/index.ts`. Until those exist, record **expected dialogs** (New Presentation, Settings, Song/Set browsers, etc.) here as notes or mock screenshots from design tools.
