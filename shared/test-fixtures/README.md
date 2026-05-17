# Test fixtures

JSON fragments below match the **camelCase** shape used by the historical Tauri reference app (`legacy/tauri_old/src/`) and the Rust `.cpres` writer.

## `minimal-presentation/`

Use these files to build a `.cpres` ZIP in tests or manually:

1. Add `manifest.json`, `slides.json`, `arrangement.json`
2. Optionally add `themes/<id>.json` if themes are referenced
3. Zip with deflate; extension `.cpres`

The `ChurchPresenter.Core.Tests` project validates round-trip open/save against these shapes.
