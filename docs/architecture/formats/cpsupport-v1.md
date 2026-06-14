# `.cpsupport` support package (v1)

A **support package** is a ZIP archive (deflate) with the `.cpsupport` extension. It exports portable production configuration from `Configurations/`—Looks, logical screens, stage layouts, clear groups, timers, messages, props, macros, show defaults, and related JSON—without machine-local bindings.

ProPresenter analogue: **Support Files** in sync/migration ([`administration-portability-and-preferences.md`](../../reference/propresenter/features/administration-portability-and-preferences.md)).

## Required archive entries

| Path | Description |
|------|-------------|
| `manifest.json` | Package manifest and file inventory |
| `Configurations/<file>.json` | One entry per support JSON file |

Entries under `MachineState/` must never appear in a portable export.

## `manifest.json`

```json
{
  "formatVersion": "1.0.0",
  "packageType": "support",
  "exportedAt": "2026-06-14T12:00:00.0000000Z",
  "replaceMissingFiles": false,
  "files": [
    {
      "path": "Configurations/Show.json",
      "sha256": "..."
    }
  ]
}
```

| Field | Description |
|-------|-------------|
| `formatVersion` | Semver (`1.0.0`) |
| `packageType` | Always `"support"` |
| `exportedAt` | ISO-8601 UTC |
| `replaceMissingFiles` | Default import behavior when local file missing |
| `files` | Inventory with package-relative `path` and content `sha256` |

## Portable vs machine-local

**Includes:** logical screen definitions, Look routing matrices, stage layout templates, clear groups, timer/message/prop/macro definitions, shared show transition defaults, group/label defaults, media import presets where stored as configuration.

**Excludes:** monitor ids, display coordinates, SDI/NDI device instance ids, audio hardware selections, credentials, tokens, caches, recent files, activation state.

After import, hosts run a **reconciliation** step to bind portable logical screens to local outputs.

## Import semantics

Preview classifies each file as add, replace, unchanged, conflict, or warning. Destructive replace requires `SupportPackageImportOptions.AllowDestructiveReplace`. Import writes only into `Configurations/`; it does not modify presentations, libraries, or playlists.

## Implementation (Windows)

| Concern | Location |
|---------|----------|
| Export / import | `SupportPackageService` |
| Models | `SupportPackageModels.cs` |
| Tests | `SupportPackageServiceTests` |
