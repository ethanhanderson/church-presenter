# Design references

Use this doc to capture **screenshots, flow sketches, and UX notes** while aligning WinUI (and future macOS) surfaces with ProPresenter behavior in `docs/reference/propresenter/`.

## When to add references

- A surface is being redesigned and needs a before/after or target mock.
- ProPresenter behavior is non-obvious from reference docs alone — link the relevant `docs/reference/propresenter/features/*.md` page in the note.
- Dialog or flyout flows are planned but not yet implemented in the native app.

## How to capture (WinUI)

1. Run the packaged app: `pwsh -File scripts/windows/Invoke-AgentE2EVerification.ps1` or build + `Run-PackagedApp.ps1`.
2. Capture **Show**, **Editor**, **Reflow**, **Themes**, **Settings**, and **Output** (audience window) as needed.
3. Store images under `docs/design-references/` with descriptive names (e.g. `show-library-playlist.png`, `edit-canvas.png`).
4. Prefer short markdown notes beside images when behavior must be explicit (keyboard flow, layer clears, transition precedence).

## Packaged artwork

MSIX and Store logos live in `apps/windows/ChurchPresenter/Assets/`. Promote cross-platform artwork into `docs/design-references/` only when both native apps should share the same canonical reference file; keep licensing and attribution with third-party assets.
