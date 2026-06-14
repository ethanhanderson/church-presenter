# `.cplibrary` library package (v1)

A **library package** is a ZIP archive (deflate) with the `.cplibrary` extension. It exports one library catalog record and every presentation bundle the library owns, so another ChurchPresenter install can import the library as a new local library with remapped ids and paths.

ProPresenter analogue: selective library export and the **Libraries** sync category ([`libraries-playlists-integrations.md`](../../reference/propresenter/library-show-management/libraries-playlists-integrations.md)).

## Required archive entries

| Path | Description |
|------|-------------|
| `manifest.json` | Package manifest (see below) |
| `library.json` | Portable `LibraryDto` with presentation refs rewritten to in-package paths |
| `presentations/<name>.cpres` | One `.cpres` per distinct presentation referenced by `library.json` |

Path segments use forward slashes. Presentation paths inside `library.json` must be relative package paths (`presentations/...`), never absolute host paths.

## `manifest.json`

```json
{
  "formatVersion": "1.0.0",
  "packageType": "library",
  "name": "Seasonal",
  "exportedAt": "2026-06-14T12:00:00.0000000Z",
  "presentationBundleMode": "bundle",
  "includeMedia": true,
  "includeFonts": true,
  "includeEmbeddedThemes": true
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `formatVersion` | yes | Semver; `1.0.0` for this spec |
| `packageType` | yes | Always `"library"` |
| `name` | yes | Display name at export time |
| `exportedAt` | yes | ISO-8601 UTC timestamp |
| `presentationBundleMode` | recommended | `"document"` or `"bundle"` — mode applied to every embedded `.cpres` (see [`cpres-v1.md`](cpres-v1.md)) |
| `includeMedia` | recommended | When `false`, embedded presentations are document mode (no `media/` binaries) |
| `includeFonts` | recommended | When `false`, embedded presentations omit `fonts/` binaries |
| `includeEmbeddedThemes` | optional | When `false`, strip `themes/` from embedded presentations (theme ids remain as external references) |

v1.0.0 writers may omit the recommended fields; readers treat missing `includeMedia` / `includeFonts` as `true` (full bundle copy) for backward compatibility with packages produced before the field existed.

## `library.json`

Mirrors catalog `LibraryDto`:

| Field | Description |
|-------|-------------|
| `id` | Source library id (import generates a new local id) |
| `name`, `description` | Library metadata |
| `createdAt`, `updatedAt` | ISO-8601 timestamps |
| `defaultFolder` | Optional content-root folder hint |
| `presentations` | Ordered list of `PresentationRefDto` |

Each `PresentationRefDto`:

| Field | Description |
|-------|-------------|
| `path` | Package-relative path to `.cpres` (e.g. `presentations/welcome.cpres`) |
| `title` | Cached display title |
| `updatedAt` | Last known bundle update |
| `arrangementId` | Optional default arrangement when opened from library |
| `destinationLayerId` | Optional output-layer hint |
| `thumbnailData` | Optional base64 navigation thumbnail |

Extension data (`extensionData`) round-trips for forward-compatible metadata.

## Export semantics

1. Resolve the library from `catalog.json`.
2. For each presentation ref, locate the owned `.cpres` on disk.
3. Optionally rewrite each bundle to document or bundle mode per manifest flags (strip or retain `media/`, `fonts/`, `themes/`).
4. Deduplicate: multiple refs to the same source bundle produce one archive entry.
5. Rewrite refs in `library.json` to package-relative paths.
6. Write `manifest.json`, `library.json`, and presentation entries atomically (temp zip + replace).

## Import semantics

1. Validate `manifest.json` (`formatVersion`, `packageType`).
2. Preview: list catalog add, per-presentation add/conflict/unchanged, embedded media copy requirements.
3. On confirm: extract each `.cpres` into `Presentations/` (side-by-side or replace per `CollectionPackageImportOptions`).
4. Assign new library `id`; remap presentation paths in the imported `library.json`.
5. Append library to local `catalog.json`; publish `PackageImportCompleted` on the content change bus.

Destructive replace requires explicit operator confirmation, matching ProPresenter **Replace My Files** warnings.

## Excludes by default

- Playlists, themes library, media bin, support files, machine bindings, credentials, caches.
- Presentations not listed in the exported library.

## Implementation (Windows)

| Concern | Location |
|---------|----------|
| Export / import | `CollectionPackageService` |
| Catalog model | `CatalogDto`, `LibraryDto` |
| Tests | `CollectionPackageServiceTests` |
| UI | Show sidebar library context menu |
