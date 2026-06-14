# `.cpplaylist` playlist package (v1)

A **playlist package** is a ZIP archive (deflate) with the `.cpplaylist` extension. It exports one playlist structure and every presentation bundle referenced by playlist items, preserving order, per-occurrence metadata, and external plan links.

ProPresenter analogue: playlist export with optional media ([`libraries-playlists-integrations.md`](../../reference/propresenter/library-show-management/libraries-playlists-integrations.md)).

## Required archive entries

| Path | Description |
|------|-------------|
| `manifest.json` | Package manifest (see below) |
| `playlist.json` | Portable `PlaylistDto` with item paths rewritten to in-package paths |
| `presentations/<name>.cpres` | One `.cpres` per distinct presentation referenced by playlist items |

Future playlist item kinds (headers, placeholders, media cues) will add non-presentation entries to `playlist.json` without changing the archive shell.

## `manifest.json`

```json
{
  "formatVersion": "1.0.0",
  "packageType": "playlist",
  "name": "Sunday Morning",
  "exportedAt": "2026-06-14T12:00:00.0000000Z",
  "includeMedia": true,
  "includeFonts": true,
  "presentationBundleMode": "bundle"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `formatVersion` | yes | Semver; `1.0.0` for this spec |
| `packageType` | yes | Always `"playlist"` |
| `name` | yes | Playlist name at export time |
| `exportedAt` | yes | ISO-8601 UTC |
| `includeMedia` | recommended | ProPresenter **playlist export with media**. When `false`, embedded `.cpres` files use document mode (slide JSON only) |
| `includeFonts` | recommended | When `false`, omit font binaries from embedded presentations |
| `presentationBundleMode` | recommended | `"document"` or `"bundle"` applied to each embedded `.cpres` |

Missing `includeMedia` defaults to `true` for packages written before the field existed.

## `playlist.json`

Mirrors catalog `PlaylistDto`:

| Field | Description |
|-------|-------------|
| `id` | Source playlist id (import assigns new local id) |
| `name`, `description` | Playlist metadata |
| `createdAt`, `updatedAt` | ISO-8601 timestamps |
| `items` | Ordered list of playlist entries |
| `externalSet` | Optional Planning Center / external service link (`ExternalSetLinkDto`) |
| `sync` | Optional sync metadata (status, conflict URL, errors) |

### Playlist items (v1)

v1 packages use `PresentationRefDto` for each item—the same shape as library presentation refs:

| Field | Description |
|-------|-------------|
| `path` | Package-relative `.cpres` path |
| `title` | Cached title |
| `updatedAt` | Bundle timestamp |
| `arrangementId` | Arrangement chosen for this **occurrence** |
| `destinationLayerId` | Output layer target for this occurrence |
| `thumbnailData` | Optional navigation thumbnail |

**Duplicate occurrences:** the same presentation path may appear multiple times in `items[]`. Import must preserve order and count; remove/reorder operations target the selected occurrence unless the operator chooses all matching instances.

### Playlist items (planned)

Align with `PlaylistItemKind` in `ContentPackaging.cs`:

| Kind | Serialized shape (future) |
|------|---------------------------|
| `PresentationReference` | `PresentationRefDto` (current) |
| `Header` | `{ "kind": "header", "label": "Worship" }` |
| `Placeholder` | `{ "kind": "placeholder", "label": "Sermon" }` |
| `MediaReference` | `{ "kind": "media", "assetId": "...", "cueId": "..." }` |
| `ExternalPlanReference` | `{ "kind": "externalPlan", ... }` with portable external ids |

Non-presentation items do not add `.cpres` entries. Packages that include only headers/placeholders may omit `presentations/` entirely.

## Export semantics

1. Resolve playlist from catalog.
2. Collect presentation paths from all `items` (skip empty paths and future non-presentation kinds).
3. Deduplicate bundle files; preserve per-item refs and occurrence-specific `arrangementId` / `destinationLayerId`.
4. Apply `includeMedia` / bundle mode when copying or rewriting each `.cpres`.
5. Include `externalSet` and `sync` for provenance; credentials stay out of the package.

## Import semantics

Same preview / confirm / remap flow as [`.cplibrary`](cplibrary-v1.md), but appends a new `PlaylistDto` to catalog instead of a library. Imported presentations land under `Presentations/`; playlist item paths remap to local relative paths.

## Relationship to libraries

Importing a playlist package does **not** imply library ownership. Presentations imported with the package are files on disk referenced by the new playlist; operators may later add them to libraries through normal content-management flows. Full library ownership semantics remain in the library catalog.

## Implementation (Windows)

| Concern | Location |
|---------|----------|
| Export / import | `CollectionPackageService` |
| Model | `PlaylistDto`, `PresentationRefDto`, `ExternalSetLinkDto` |
| Tests | `CollectionPackageServiceTests.ExportPlaylistAsync_*` |
| UI | Show sidebar playlist context menu |
