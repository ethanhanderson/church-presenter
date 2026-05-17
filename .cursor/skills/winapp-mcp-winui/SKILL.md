---
name: winapp-mcp-winui
description: Automates WinUI and other Windows desktop UIs via the WinApp MCP (desktop-pilot). Guides session lifecycle, UIA discovery, reliable interactions, waits, WinUI3 virtualization and dialogs, visual regression, and event-based debugging. Use when implementing or testing ChurchPresenter Windows UI, running E2E desktop flows, reproducing bugs through UI automation, or hardening edge cases (async load, off-screen items, ContentDialogs, duplicate labels).
---

# WinApp MCP (WinUI workflows)

Upstream reference: [WinApp MCP documentation](https://github.com/floatingbrij/desktop-pilot-mcp/blob/main/DOCUMENTATION.md).

Most tools take **`appId`** from `launch_app` or `attach_to_app` / `attach_to_pid`. Errors return strings prefixed with `ERROR: `. Prefer **`automationId`** over **name** (stable; names change with localization).

## Session lifecycle

- **`launch_app(exePath, arguments?)`** — GUI apps only; waits ~2s for main window. Follow with **`wait_for_input_idle`** or **`wait_for_element`** before interacting.
- **`attach_to_app(processName)`** — First matching process; use **`attach_to_pid`** when multiple instances exist.
- **`list_apps`**, **`close_app`** — Track and tear down sessions.

## Before touching controls

1. **`get_snapshot(appId, maxDepth?)`** — Map the tree (default depth 3; increase only if needed). Note **AutomationId** values for stable selectors.
2. If you already know targets, **`find_elements`** / **`find_all_elements`** is cheaper than repeated full snapshots.
3. **`invalidate_cache`** after UI changes that were **not** caused by a tracked MCP action (async refresh, navigation, dialog replacement).

Caching: descendant cache ~2s; mutations auto-invalidate. Do not spam **`get_snapshot`**—use **`element_exists`**, **`find_elements`**, or **`read_element`** for checks.

## Implementing features (happy path)

- Navigate: **`click_element`** → **`wait_for_element`** for the next shell (page, form, dialog).
- Forms: **`fill_form`** with JSON mapping AutomationId/name → value (faster than many **`type_text`** calls).
- Dropdowns: **`select_option`** (expand/select in one step), not click→wait→click.
- Keyboard shortcuts: **`press_key_combo`** (e.g. save).
- Confirm state: **`get_all_values`** or **`read_element`** on key fields.

## Fixing bugs

- Reproduce: **`attach_to_app`** or **`launch_app`**, then minimal steps with **`click_element`** / **`type_text`**.
- When **click does nothing**: **`invoke_element`** (Invoke/Toggle patterns)—common for **ContentDialogs**, menus, some toggles.
- **Wrong element targeted**: **`find_all_elements`** → use **`index`** or disambiguate **`automationId`** / **`controlType`**.
- **Async / flaky UI**: **`wait_for_condition`** on `name`, `value`, `isEnabled`, etc., not fixed sleeps.
- **Stuck modifiers / mouse**: **`release_all`** (emergency).

## End-to-end and regression

- Flow template: attach → navigate → **wait** → act → **wait** → assert (**`read_element`**, **`element_exists`**, **`get_all_values`**).
- Visual evidence: **`take_screenshot`** / **`take_screenshot_optimized`** (`maxTokens` to limit image size for LLM context).
- Compare runs: **`screenshot_diff`** between baseline and after-change PNGs.
- Cheap “did the tree change?” checks: **`get_tree_hash`** before/after an action.

## Edge cases (WinUI and desktop)

| Situation | Tooling |
|-----------|---------|
| List/Grid item not found | **`scroll_into_view`**, **`scroll_element`**, then **`realize_virtualized_item`** — WinUI **ListView** / **GridView** / **ItemsRepeater** virtualize; items may be absent from UIA until realized. |
| Item in large/virtualized list | **`find_item_by_property`** on the container (**Name** / **AutomationId**). |
| DataGrid cell | **`get_grid_item`** (row/column indices). |
| Separate window (dialog, file picker) | **`list_desktop_windows`** → HWND → **`get_snapshot_hwnd`**, **`click_element_hwnd`**, **`set_value_hwnd`**. |
| Dynamic or typo-tolerant labels | **`find_elements_fuzzy`**. |
| Duplicate visible names | **`find_all_elements`** + **`index`**, or enforce **`automationId`** in XAML. |

## Debugging async UI and odd updates

- **`start_event_monitor`** (`focus`, `structurechanged`, or `propertychanged`) → reproduce → **`get_event_log`** → **`stop_event_monitor`**.
- **`read_element`**: **IsEnabled**, **IsOffscreen**, patterns—useful for disabled buttons and invisible-but-present nodes.

## Project-specific reminder

ChurchPresenter WinUI: set **`AutomationProperties.AutomationId`** in XAML for anything agents or tests must target reliably; without it, automation depends on fragile **name** text.
