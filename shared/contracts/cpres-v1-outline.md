# `.cpres` bundle format (v1) — outline

A `.cpres` file is a **ZIP** archive (deflate) containing at least:

| Path | Description |
|------|-------------|
| `manifest.json` | `formatVersion`, `presentationId`, title, timestamps, `media[]`, `fonts[]`, optional theme id, external sync fields |
| `slides.json` | Array of slide documents |
| `arrangement.json` | `order` (slide ids) and `sections` (section groups) |
| `themes/*.json` | One file per embedded theme (filename under `themes/`) |
| `media/*` | Binary media referenced by manifest `media[].path` |
| `fonts/*` | Font files referenced by manifest `fonts[].path` |

## Slide text and themes

Newer v1 writers store editable slide text in `slides[].textBlocks[]` instead of treating
styled text layers as the source of truth. Each block has a stable `id`, optional `role`
and `name`, `text`, and an optional `sourceLayerId` for files migrated from layer-based
text. Theme text layers bind to those blocks with `textBinding` (`textBlockId`, `role`,
or `fallbackIndex`) so one slide can render in different styles without rewriting the
slide's raw text.

Slides and manifests may include `themeBinding` metadata. Linked bindings keep a slide
or presentation updated from a source theme; detached bindings mean the resolved theme
style was materialized into slide-local editable layers. Applied themes can also be
stored as `themes/*.json` snapshots with source IDs/version stamps so a `.cpres` remains
portable when the global theme library is unavailable.

Readers should continue to accept older files that only have text layer `content`. On
open or save, implementations may derive `textBlocks[]` from existing text layers and
preserve the original layer IDs as `sourceLayerId` values.

## Save semantics

Implementations should write to a **temporary file in the same directory** and **rename** to the final path for atomic replace (matches `legacy/tauri_old/src-tauri/src/cpres.rs`).

## Import paths

When saving, media/font entries may reference:

- Absolute filesystem paths for newly imported assets, or
- `bundle:<path>` to re-read bytes from an existing bundle path inside the current `.cpres`.
