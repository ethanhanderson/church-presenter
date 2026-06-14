# ChurchPresenter UI smoke flows

Use **only when the task needs WinApp MCP** (crash repro, functional testing, agent-driven flows) — not for routine UI/design unless the user asks. After a fresh relaunch (`Run-PackagedApp.ps1 -Mode Detach`), read `last-run.json` and attach. Rebuild when needed before relaunch.

## Attach

1. Read `.agent-logs/last-run.json` → `mcpServer`, `mcpAttachArgs.pid` (or top-level `pid`). Under `-Mode DebugOutput` (no `last-run.json` until exit), use the `launched (PID: N)` log line instead.
2. `CallMcpTool` → server **`project-0-church-presenter-winapp`** → `attach_to_pid` with `{"pid": <number>}`
3. Optional: `wait_for_input_idle` — if it returns NotImplemented, continue with snapshot
4. `get_snapshot` with `maxDepth` 4

Expect main window title **Church Presenter** (with a space). Switch top-level tabs with **`invoke_element`** on the `*SelectorItem` (the `SelectorBar` uses the selection pattern; a plain `click_element` may not change the tab).

## Flow A — Shell navigation (minimal)

1. `invoke_element` → `automationId: ShowSelectorItem` (if not already on Show).
2. `find_elements` → `idContains: ShowContentSplitButton` or sidebar `ShowNavigationView`.
3. `take_screenshot_optimized` with modest `maxTokens` for evidence.

## Flow E — Presentation workspace (Editor / Reflow)

Editor and Reflow are **disabled until a presentation is open**.

1. `read_element` → `EditorSelectorItem` → if `IsEnabled` is false, open or create a presentation first: `invoke_element` → `ShowContentSplitButton`, then `press_key RETURN` to accept the New Presentation dialog default (its Create/Cancel buttons are in a popup layer, not the main snapshot).
2. `wait_for_element` → `EditorSelectorItem` enabled, then `invoke_element` to switch.
3. `invoke_element` → `ShowSelectorItem` to return; `list_apps` to confirm the app is still alive after the round-trip.

## Flow B — Settings round-trip

1. `click_element` → `SettingsSelectorItem`
2. `invalidate_cache` → `get_snapshot` (settings content may be in a nested frame; increase `maxDepth` if needed)
3. `click_element` → `ShowSelectorItem`
4. Confirm Show surface returns (`ShowContentSplitButton` or slide deck region).

## Flow C — Media panel toggle

1. On Show page: `click_element` → `MediaPanelToggleButton`
2. `invalidate_cache` → snapshot diff or `find_elements` on drawer controls (`MediaPanelGridScrollViewer`, etc.)
3. Toggle again; confirm restored layout.

## Flow D — Output title-bar toggles (no extra monitors required)

1. `read_element` on `AudienceTitleToggle` / `StageTitleToggle` — note enabled state.
2. Optional click; `invalidate_cache` after navigation-style UI changes.
3. Screenshot if visual regression matters.

## WinUI pitfalls (this app)

| Issue | MCP approach |
|-------|----------------|
| Switching tabs (`SelectorBar`) | `invoke_element` on `*SelectorItem` (selection pattern); `click_element` may not change selection |
| Virtualized slide/media lists | `scroll_into_view`, `realize_virtualized_item`, `find_item_by_property` |
| `ContentDialog` | Buttons render in a popup layer, often absent from the main snapshot — `invoke_element` by name, `press_key RETURN`/`ESCAPE`, or `list_desktop_windows` + HWND tools. Read the dialog `.xaml` for button names. |
| Async load after navigation | `find_elements` / `wait_for_element`; avoid long fixed sleeps |
| Duplicate labels | `find_all_elements` + `index`, or use `automationId` |
| Tree stale after action | `invalidate_cache`; the user may also be interacting concurrently |
| Control seems missing / action seems no-op | Cross-check screenshot + `.xaml` source + snapshot before concluding — don't guess |

## Adding new agent-tested UI

When implementing features agents must drive, add `AutomationProperties.AutomationId` in XAML and document the id here.
