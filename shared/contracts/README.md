# Shared contracts (non-runtime)

Canonical behavioral contracts for Church Presenter live in the TypeScript models under `legacy/tauri_old/src/` and the Rust `.cpres` implementation:

- **Presentation & `.cpres` v1**: `legacy/tauri_old/src/lib/models/types.ts`
- **ZIP layout & atomic save**: `legacy/tauri_old/src-tauri/src/cpres.rs`
- **App paths & catalog files**: `legacy/tauri_old/src/lib/services/appDataService.ts`

## Windows compatibility notes

- JSON uses **camelCase** property names to match existing `.cpres` and catalog files on disk.
- `formatVersion` in `manifest.json` follows semver (e.g. `1.0.0`).
- Do not introduce breaking schema changes without bumping `formatVersion` and documenting migration in `docs/migration/`.
- Windows v1 readers normalize legacy layer-based text into `slides[].textBlocks[]` and
  keep theme bindings additive. Existing layer `content` remains readable for older files,
  but raw text blocks are the portable editing source going forward.

## Files in this folder

| File | Purpose |
|------|---------|
| `cpres-v1-outline.md` | Human-readable outline of bundle layout and required JSON files |
