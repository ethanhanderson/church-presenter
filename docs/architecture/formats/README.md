# ChurchPresenter portable file formats

ChurchPresenter persists church content as versioned ZIP or JSON archives under the content root. These formats mirror ProPresenter portability boundaries described in [`docs/reference/propresenter/library-show-management/libraries-playlists-integrations.md`](../../reference/propresenter/library-show-management/libraries-playlists-integrations.md) and [`docs/reference/propresenter/features/libraries-playlists-and-generated-content.md`](../../reference/propresenter/features/libraries-playlists-and-generated-content.md).

Shared content rules live in [`content-management.md`](../content-management.md). Platform I/O lives in `ChurchPresenter.Core` (`.cpres`) and `ChurchPresenter.Application` (collection and support packages).

## Format index

| Extension | Package boundary | ProPresenter analogue | Spec |
|-----------|------------------|----------------------|------|
| `.cpres` | Presentation document or presentation bundle | Presentation export / Presentation Bundle | [`cpres-v1.md`](cpres-v1.md) |
| `.cplibrary` | Library package with owned presentations | Library sync category + selective export | [`cplibrary-v1.md`](cplibrary-v1.md) |
| `.cpplaylist` | Playlist package with presentation occurrences | Playlist export (optional media) | [`cpplaylist-v1.md`](cpplaylist-v1.md) |
| `.cpsupport` | Shared support / configuration package | Support Files sync category | [`cpsupport-v1.md`](cpsupport-v1.md) |

Slide image folders produced by **Export as Images** are a raster export, not a portable document format. They do not round-trip into editable presentations.

## ProPresenter parity map

| ProPresenter export / sync category | ChurchPresenter format | Media in package |
|-------------------------------------|------------------------|------------------|
| Presentation export (slides only) | `.cpres` with `bundleMode: document` | Metadata only; no `media/` or `fonts/` binaries |
| Presentation Bundle | `.cpres` with `bundleMode: bundle` | Embedded `media/*`, `fonts/*`, optional `themes/*` |
| Library (presentations) | `.cplibrary` | Full `.cpres` per referenced presentation (mode chosen at export) |
| Playlist export | `.cpplaylist` | Presentations per item; `includeMedia` controls bundle mode |
| Playlist export with media | `.cpplaylist` with `includeMedia: true` | Same, all embedded presentations use bundle mode |
| Support Files | `.cpsupport` | JSON configuration only |
| Sync snapshot (future) | TBD multi-category archive | Operator-selected categories |

## Ownership and references

- A **library** owns presentation documents (`.cpres` files). Catalog `library.json` lists `PresentationRefDto` entries pointing at bundle paths.
- A **playlist** references library-owned presentations by path. The same presentation may appear multiple times; each playlist row is an independent occurrence with its own `arrangementId`, `destinationLayerId`, and future cue context.
- Exporting a library or playlist **copies** referenced `.cpres` archives into the package; it does not transfer ownership until import creates new local library/playlist records and remapped paths.
- Deleting a playlist item is non-destructive to the presentation bundle. Deleting a library-owned presentation is destructive.

## Implementation map (Windows)

| Format | Read / write | Export UI | Import UI |
|--------|--------------|-----------|-----------|
| `.cpres` | `ChurchPresenter.Core/Cpres/`, `CpresDocumentService`, `PresentationProjectService` | Show sidebar: **Export as Bundle**; Editor save | File picker, library/playlist import, package import |
| `.cplibrary` | `CollectionPackageService` | Show sidebar: library **Export Package** | Show sidebar: **Import Package** |
| `.cpplaylist` | `CollectionPackageService` | Show sidebar: playlist **Export Package** | Show sidebar: **Import Package** |
| `.cpsupport` | `SupportPackageService` | Settings / future sync UI | Operator query preview + import |

Boundary descriptors used in tests and services: `ChurchPresenter.Application/Backend/Content/ContentPackaging.cs`.

## On-disk naming

Stable ids (`presentationId`, library `id`, playlist `id`) live in catalog JSON, registry `Index.json`, and bundle `manifest.json` — **not** in file or folder names.

| Item | On-disk name | Collision rule |
|------|----------------|----------------|
| Presentation | `Presentations/{Title}.cpres` | `Welcome_2.cpres`, `Welcome_3.cpres`, … |
| Library folder | `Libraries/{Name}/Library.json` | `Seasonal_2/`, … |
| Playlist folder | `Playlists/{Name}/Playlist.json` | `Sunday Set_2/`, … |
| Package embedded `.cpres` | `Presentations/{Title}.cpres` | Same suffix rule inside the archive |

`DomainIndexEntry.folderName` records the allocated library/playlist directory. Legacy id-based folders remain readable until the next save/rename migrates them.

Implementation: `ContentNaming`, `ContentRegistryFolders`, `IContentDirectoryService.GeneratePresentationPath(string title)`.

All portable archives include `manifest.json` with `formatVersion` (semver). Bump the major segment for breaking required-field or layout changes. Readers normalize older shapes into the canonical content graph rather than preserving multiple write paths.

## Implementation status (Windows)

| Item | Spec | Status |
|------|------|--------|
| Document vs bundle `.cpres` export | `bundleMode`, strip binaries for document | Implemented (`CpresBundleExporter`, sidebar submenu) |
| `bundleMode` on save | Written on every manifest | Implemented |
| Library package manifest flags | `includeMedia`, fonts, themes | Implemented; export UI uses full-bundle defaults |
| Playlist optional media | `includeMedia` + export dialog | Implemented |
| Playlist boundary descriptor | Includes presentation documents | Fixed in `ContentPackaging.cs` |
| Font copy requirements on import preview | `CopyEmbeddedFontPayload` | Implemented |
| Playlist item kinds (headers, placeholders, media) | Future `PlaylistItemKind` shapes | Not yet — v1 still `PresentationRefDto` only |
| Library export media/fonts UI | Checkbox options | Deferred (defaults to full bundle) |
| Support package operator UI | Settings export/import | Service complete; UI still limited |
