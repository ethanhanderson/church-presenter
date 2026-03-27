# Church Presenter

Monorepo layout for native desktop apps (macOS and Windows) and shared reference material.

## Layout

| Path | Purpose |
|------|---------|
| `apps/macos/` | Future native macOS app (Swift / SwiftUI). |
| `apps/windows/` | Future native Windows app (WinUI 3 / Windows App SDK). |
| `shared/` | Non-runtime shared artifacts: contracts, design references, assets, test fixtures. |
| `docs/` | Architecture and migration documentation. |
| `legacy/tauri/` | Previous Tauri + Vite + React app; run dev/build from this directory. |

## Legacy Tauri app

From `legacy/tauri/`:

```bash
bun install
bun run dev
# or: bun run tauri dev
```
