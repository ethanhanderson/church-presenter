# `.cpres` presentation bundle (v1)

A library-owned **presentation document** in ChurchPresenter persists as a **ZIP** archive (deflate) with the `.cpres` extension. This matches the product model in [`content-management.md`](../content-management.md) (presentation documents, library ownership, playlist references) and ProPresenter **presentation export** vs **presentation bundle** behavior in [`docs/reference/propresenter/library-show-management/libraries-playlists-integrations.md`](../../reference/propresenter/library-show-management/libraries-playlists-integrations.md).

JSON property names use **camelCase** so bundles stay compatible with on-disk catalog and presentation files.

## Document vs bundle mode

ProPresenter distinguishes:

- **Presentation export** — slide information and structure without media binaries.
- **Presentation Bundle** — presentation plus linked media (and, in ChurchPresenter, fonts and embedded theme snapshots).

Both use the same `.cpres` archive layout. Mode is determined by manifest flags and the presence of binary entries:

| Mode | `bundleMode` | `media/` entries | `fonts/` entries | Use case |
|------|--------------|------------------|------------------|----------|
| Document | `"document"` | absent or manifest-only refs | absent or manifest-only refs | Lightweight share, lyrics-only, re-link media on import |
| Bundle | `"bundle"` | required for each `manifest.media[].path` | required for each `manifest.fonts[].path` | Offline portability, email/USB handoff |

Writers set `bundleMode` explicitly. Readers accept either mode; missing binaries with live refs surface relink/missing-asset diagnostics instead of failing open.

## Required archive entries

| Path | Description |
|------|-------------|
| `manifest.json` | Identity, metadata, media/font inventories, theme binding, provenance |
| `slides.json` | Array of slide documents (scene graph, actions, transitions, notes) |
| `arrangement.json` | `order` (slide ids), `sections` (groups), arrangement metadata |

`CpresBundleReader.Open` requires the three JSON files and validates that `manifest.json` contains `formatVersion` and `presentationId`.

## Optional archive entries

| Path | Description |
|------|-------------|
| `themes/*.json` | Embedded theme snapshots (portable when global theme library unavailable) |
| `media/*` | Binary media referenced by `manifest.json` `media[].path` |
| `fonts/*` | Font files referenced by `manifest.json` `fonts[].path` |

Theme entries are discovered by scanning `themes/**/*.json`.

## `manifest.json`

```json
{
  "formatVersion": "1.0.0",
  "presentationId": "550e8400-e29b-41d4-a716-446655440000",
  "title": "Welcome",
  "author": "Media Team",
  "createdAt": "2026-01-01T00:00:00.0000000Z",
  "updatedAt": "2026-06-14T12:00:00.0000000Z",
  "bundleMode": "bundle",
  "aspectRatio": "16:9",
  "outputScaleMode": "fit",
  "slideSize": { "width": 1920, "height": 1080 },
  "themeId": "theme-default",
  "themeBinding": { "mode": "linked", "themeId": "theme-default" },
  "media": [],
  "fonts": [],
  "externalSong": null,
  "sync": null
}
```

### Core fields

| Field | Description |
|-------|-------------|
| `formatVersion` | Semver; bump for breaking schema changes |
| `presentationId` | Stable document id (survives rename/move) |
| `title`, `author` | Display metadata |
| `createdAt`, `updatedAt` | ISO-8601 UTC |
| `bundleMode` | `"document"` or `"bundle"` (recommended on all new writes) |
| `aspectRatio`, `outputScaleMode`, `slideSize` | Canvas sizing for editor, reflow, and render fit |
| `themeId` | Primary theme reference (global theme library or embedded) |
| `themeBinding` | Linked vs detached theme application |
| `externalSong` | SongSelect / external song link metadata |
| `sync` | Optional sync/conflict metadata |

### `media[]` entries

| Field | Description |
|-------|-------------|
| `id` | Stable asset id referenced by slide layers and actions |
| `filename` | Original file name for display and relink hints |
| `path` | Archive path (e.g. `media/background.mp4`) |
| `mime` | MIME type |
| `sha256` | Content hash for integrity and cache stamps |
| `byteSize` | Uncompressed size |
| `type` | `image`, `video`, `audio`, etc. |
| `width`, `height`, `duration` | Optional technical metadata |

In **bundle** mode, every `media[].path` must resolve to a zip entry. In **document** mode, entries may list logical refs without binaries (import prompts relink or managed-media copy).

### `fonts[]` entries

Bundled fonts keep presentations portable when custom typefaces are not installed on the destination machine.

| Field | Description |
|-------|-------------|
| `id` | Stable font asset id |
| `family`, `fullName`, `postscriptName` | Font matching metadata |
| `path` | Archive path (e.g. `fonts/inter-regular.ttf`) |
| `mime` | Typically `font/ttf`, `font/otf` |
| `sha256`, `byteSize` | Integrity |
| `weight`, `style` | CSS-like weight and style |

Slides reference fonts by family/name in text layers; `fonts[]` supplies installable files for hosts that register embedded fonts at open time.

## `slides.json`

Each slide is a typed document preserving:

- Stable `id`, slide type, disabled state, hot key
- Scene graph: text, shape, media, web, vector, live-input layers with z-order
- `textBlocks[]` for editable lyric/body copy (preferred over legacy layer-only text)
- `themeBinding` per slide when overriding presentation default
- Slide background (solid, transparent, gradient, image, video)
- `transition`, `buildSteps`, `actions[]` (declarative slide actions)
- Operator `notes`, group membership hints

Readers accept legacy files that store text only in layer `content`; open/save may derive `textBlocks[]` and set `sourceLayerId`.

## `arrangement.json`

| Field | Description |
|-------|-------------|
| `order` | Slide ids in default playback order |
| `sections` | Group definitions (verse, chorus, etc.) with stable group ids |
| Arrangement records | Named orderings over groups for playlist per-occurrence selection |

Groups use stable ids; arrangements reference group ids, not copied slide arrays. Playlist `arrangementId` on a ref selects which arrangement applies for that service occurrence.

## Embedded themes (`themes/*.json`)

When a presentation uses a theme not guaranteed on the destination machine, writers embed a snapshot under `themes/`. Each file is a full `ThemeTemplate` JSON document. `themeBinding.mode: linked` means refresh from source theme when available; `detached` means slide-local materialization.

Library and playlist packages may strip `themes/` when `includeEmbeddedThemes: false` while retaining `themeId` for relink.

## Save semantics

Writers use **atomic replace**: create a temp zip in the destination directory, then move/replace the final `.cpres` path. See `ChurchPresenter.Core/Cpres/CpresBundleWriter.cs`.

## Media and font resolution on save

`BundleSaveState` media/font entries resolve bytes from:

- An absolute filesystem path for newly imported assets, or
- `bundle:<path>` to re-read bytes from an entry inside the bundle being replaced (round-trip edits without duplicating binaries on disk).

Export helpers that produce **document** mode omit media/font bytes from `BundleSaveState` but retain manifest entries (and optional `originalPath` in extension data) for relink assistance.

## Export surfaces (Windows)

| UI action | Expected mode | Notes |
|-----------|---------------|-------|
| Editor save | Preserves current bundle contents | Normal authoring path |
| **Export as Bundle** | Copies on-disk `.cpres` as-is | Should offer document vs bundle choice per spec |
| Library / playlist package export | Per package `includeMedia` flag | See [`cplibrary-v1.md`](cplibrary-v1.md), [`cpplaylist-v1.md`](cpplaylist-v1.md) |
| **Export as Images** | N/A (raster folder) | PNG/JPEG per slide; not a `.cpres` substitute |

## Schema changes

Bump `formatVersion` in `manifest.json` (semver) for breaking layout or required-field changes. Document migration in [`content-management.md`](../content-management.md); normalize older shapes into the canonical graph rather than preserving multiple write formats.

## Implementation map (Windows)

| Concern | Location |
|---------|----------|
| ZIP read / manifest validation | `ChurchPresenter.Core/Cpres/CpresBundleReader.cs` |
| ZIP write / atomic save | `ChurchPresenter.Core/Cpres/CpresBundleWriter.cs` |
| Application-facing open/save | `ChurchPresenter.Application/Services/Documents/CpresDocumentService.cs` |
| Typed project open/save | `PresentationProjectService` |
| In-memory presentation model | `PresentationProjectModels.cs` |
| Tests | `ChurchPresenter.Core.Tests` (bundle I/O), `ChurchPresenter.App.Tests/Services/Documents/` |

Tests build minimal bundles inline or in temp directories; there is no checked-in fixture tree for `.cpres` files.
