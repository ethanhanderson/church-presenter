# WinApp MCP tools — what they do and when to use them

Server id: **`project-0-church-presenter-winapp`** (alias `winapp`). Always read a tool's JSON schema under `mcps/project-0-church-presenter-winapp/tools/<tool>.json` before calling it. Almost every tool takes `appId` (returned by an attach call). Prefer `automationId` over visible `name`.

This is descriptive context, not a list of known failures — pick the tool that matches the situation.

## Session lifecycle

| Tool | What it does | When to use |
|------|--------------|-------------|
| `attach_to_pid` | Attaches to a running process by PID; returns an `appId`. | Default for ChurchPresenter — pass the PID from `.agent-logs/last-run.json` (`mcpAttachArgs.pid`) after `Run-PackagedApp.ps1 -Mode Detach`, or the `launched (PID: N)` line under `-Mode DebugOutput`. |
| `attach_to_app` | Attaches by process/app name. | Only when the PID is unknown and exactly one instance is running. |
| `launch_app` | Launches an executable. | Not for packaged ChurchPresenter (no package identity → `REGDB_E_CLASSNOTREG`). Use `winapp run` via the scripts instead. |
| `list_apps` | Lists tracked apps and PIDs; shows "process may have exited" for dead ones. | Confirm the app is still alive — especially to detect a crash after an action. |
| `close_app` | Closes a tracked app. | Teardown. |

## Inspecting the UI tree

| Tool | What it does | When to use |
|------|--------------|-------------|
| `get_snapshot` | Dumps the element tree of the main window to `maxDepth`. | First map of a page; note AutomationIds. Increase `maxDepth` for nested frames/panes. |
| `get_snapshot_hwnd` | Snapshot of a specific top-level window by HWND. | Separate windows (audience/stage output) or popups not under the main window. |
| `find_elements` | Filtered search (controlType / idContains / nameContains). | Targeted lookup — faster than a full snapshot when you know part of the id/name/type. |
| `find_all_elements` | Returns all matches with indices. | Disambiguate duplicates; get the `index` to pass to `click_element`/`invoke_element`. |
| `read_element` | Detailed properties of one element (`IsEnabled`, `IsOffscreen`, bounds, patterns). | Check state before acting — e.g. is a tab enabled, is a control off-screen. |
| `read_element_by_index` / `get_all_values` / `get_grid_item` / `get_focused_element` | Property/value reads for indexed items, value-bearing controls, grid cells, and the focused element. | Assertions and value verification. |

## Interacting

| Tool | What it does | When to use |
|------|--------------|-------------|
| `click_element` | Simulates a click on an element. | Buttons and general click targets. Pair with `wait_for_element`/`wait_for_condition` to confirm the result. |
| `invoke_element` | Fires the element's UIA pattern: Invoke, Toggle, **and Select for selection items**. | Selecting a `SelectorBar`/`SelectorBarItem` tab or other `ListItem` selection control; `ContentDialog` buttons; toggles — anywhere a plain click may not flip selection/state. |
| `right_click_element` | Opens the element's context menu. | Context-menu flows (e.g. clear-groups, slide/item menus). |
| `set_element_value` / `fill_form` | Sets text/value on inputs. | TextBoxes and forms. |
| `select_option` | Expands a ComboBox and selects an option by text. | Dropdowns/ComboBoxes. |
| `expand_collapse_element` | Toggles an ExpandCollapse control. | Tree/NavigationView items with a chevron (target the item that supports the pattern, not its child Text). |
| `scroll_into_view` / `scroll_element` / `realize_virtualized_item` | Brings items into view / scrolls / realizes virtualized items. | Virtualized slide/media lists where an item is off-screen or not yet created. |
| `drag_element` | Drag-and-drop between elements. | Media-onto-slide and reorder flows. |
| `press_key` / `press_key_combo` | Sends keys (`RETURN`, `ESCAPE`, `TAB`, `F5`, combos). | Accept a `ContentDialog` default (`RETURN`) or cancel (`ESCAPE`); keyboard accelerators; SelectorBar arrow navigation after focusing. |

## Waiting and cache

| Tool | What it does | When to use |
|------|--------------|-------------|
| `wait_for_element` | Waits for an element to appear (timeout). | After navigation or opening a dialog — instead of fixed sleeps. |
| `wait_for_condition` | Waits for a predicate over the tree. | Async UI that mutates in place. |
| `wait_for_input_idle` | Waits for input-idle. | Optional; if it returns NotImplemented on this WinUI host, skip and use snapshots/waits. |
| `invalidate_cache` | Drops the cached window reference. | After navigation, a new window, or any non-MCP UI change — then re-snapshot. |

## Evidence and diagnostics

| Tool | What it does | When to use |
|------|--------------|-------------|
| `take_screenshot_optimized` | Saves a screenshot (set `maxTokens` to cap size). | Visual ground truth — confirm what actually rendered before concluding something is missing/broken. Attach the image in your reasoning. |
| `annotate_screenshot` | Draws annotations on a screenshot. | Highlighting regions for a report. |
| `get_tree_hash` | Hash of the current tree. | Cheap change detection between steps. |
| `start_event_monitor` / `get_event_log` / `stop_event_monitor` | Records UIA events. | Flaky/async UI where you need the sequence of UIA events (note: this does not capture native crashes — use `Run-PackagedApp.ps1 -Mode DebugOutput -Symbols` for those). |
| `list_desktop_windows` / `list_windows` / `restore_window` | Enumerate top-level windows / restore a minimized window. | Reaching dialogs/popups or other windows by HWND, then `*_hwnd` tools. |
| `get_clipboard` | Reads the clipboard. | Verifying copy actions. |
| `click_element_hwnd` | Clicks within a specific HWND. | Popups/secondary windows not addressable from the main window tree. |

## Recurring grounding rule

Snapshot, screenshot, and `.xaml` source are complementary. When a control seems absent or an action seems to "do nothing," cross-check all three (and remember the user may be interacting with the same app) before changing code — don't infer from a single snapshot.
