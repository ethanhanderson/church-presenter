---
name: winapp-mcp-winui
description: ChurchPresenter packaged build/launch/debug workflow (Microsoft winapp CLI scripts). WinApp MCP only when the task needs crash repro, functional in-app testing, or agent-driven interaction — not for routine UI/design work unless the user asks. Relaunch when runtime verification or MCP is needed. Do not use Visual Studio F5, dotnet run, or launch_app on ChurchPresenter.exe for verification.
---

# ChurchPresenter packaged verification (winapp CLI + WinApp MCP)

**This is the only supported workflow** for building, launching, and logging the packaged WinUI app. **WinApp MCP is optional** — use it only when the task requires driving or observing the live app.

| Layer | Tool | When |
|-------|------|------|
| Build / stop / launch / logs | `scripts/windows/*.ps1` + **Microsoft winapp CLI** | Compile gate; runtime verification; crash debugging |
| UI automation | **WinApp MCP** (`project-0-church-presenter-winapp`) | Crash repro, **functional** testing, multi-step flows — **not** routine UI/design unless user asks |

**Rule:** `.cursor/rules/windows-build-verification.mdc`  
**MCP upstream:** [desktop-pilot-mcp docs](https://github.com/floatingbrij/desktop-pilot-mcp/blob/main/DOCUMENTATION.md)  
**winapp CLI docs:** [Debugging with Package Identity](https://learn.microsoft.com/windows/apps/dev-tools/winapp-cli/debugging)

Read tool schemas under `mcps/project-0-church-presenter-winapp/tools/` before calling MCP tools.

## When agents MUST use winapp scripts (build / launch / logs)

Run packaged build (and launch when runtime matters) when **any** of these apply:

1. **Big refactors** — output engine, pipeline, routing, packaging, startup, navigation shells
2. **Behavior changes** — logic, commands, playback, routing, dialogs, anything that must work at runtime
3. **User explicitly requests** — "test it", "verify", "run the app", "reproduce the crash"
4. **Before marking substantial Windows work complete** — at minimum build passes; launch/MCP only if the task needs them

**Skip** for: comment/docs-only edits, or pure Application/Core changes with no WinUI/host impact (unit tests may suffice).

## When to use WinApp MCP (task-by-task)

**Do not attach MCP by default.** Decide per task:

| **Use MCP** | **Usually skip MCP** |
|-------------|----------------------|
| Reproducing or diagnosing a **crash** / native fail-fast | **UI redesign** — layout, styling, spacing, theme tweaks |
| **Functional** testing — take-live, transport, clear layers, settings persistence, dialogs | Visual polish the user will review themselves |
| Multi-step operator flows the agent must **execute** in-app | Static structure/XAML that only needs `dotnet build` |
| User asks to automate, test, or verify in the running app | Design iteration ("make this panel cleaner") |

**UI/design work:** build to verify compile; relaunch if you need runtime confirmation; **MCP only if the user asks** or a functional regression is in scope.

**Functional work:** relaunch before MCP; drive flows from `references/churchpresenter-ui-flows.md`.

Each new task in a conversation gets its own MCP decision — styling earlier does not mean skip MCP for a later bugfix.

## Iteration loop — rebuild + relaunch on every app change

While editing ChurchPresenter, treat verification as a **repeatable loop**, not a one-time launch at the end.

**Always (when building or relaunching):**

1. `Stop-ChurchPresenter.ps1` before packaged build or `winapp run`
2. **Relaunch** when verifying runtime behavior, debugging crashes, or **before any MCP attach** — fresh PID in `.agent-logs/last-run.json`
3. **MCP attach** only when the current task needs it (see above) — never against a PID from before your latest relaunch

**Rebuild when needed:**

- Run `Build-PackagedApp.ps1` (or orchestrator without `-SkipBuild`) after any edit that should affect the running app (`apps/windows/**`, and Application/Core when it changes host behavior)
- **`-SkipBuild` is allowed only** when a packaged build already **succeeded after your most recent edits** in the current session — not because an earlier agent turn built once

| Goal | Command |
|------|---------|
| Rebuild + relaunch + skip unit tests | `Invoke-AgentE2EVerification.ps1 -SkipTests` |
| Relaunch only (build already current) | `Invoke-AgentE2EVerification.ps1 -SkipTests -SkipBuild` |
| Logs while iterating | `Stop-ChurchPresenter.ps1` then `Run-PackagedApp.ps1 -Mode DebugOutput` |

Step-by-step equivalent:

```powershell
pwsh -File scripts/windows/Stop-ChurchPresenter.ps1
pwsh -File scripts/windows/Build-PackagedApp.ps1 -SkipStop   # skip if already built since your last edit
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode Detach
```

After each relaunch, read **fresh** `last-run.json` before `attach_to_pid`. Pure UI styling with no MCP: build may be enough; let the user review visuals unless they ask you to drive the app.

## Phase 0 — Prerequisites

Microsoft winapp CLI on PATH (once per machine):

```powershell
winget install Microsoft.winappcli --source winget
# or: pwsh -File scripts/windows/Install-WinAppCli.ps1
```

## Phase 1 — Orchestrated verification

From repo root:

```powershell
pwsh -File scripts/windows/Invoke-AgentE2EVerification.ps1 -InstallWinAppCli
```

This: stops `ChurchPresenter` → Core + App `dotnet test` → packaged `dotnet build` → `winapp run --detach` → writes `.agent-logs/last-run.json` (PID for MCP).

| Flag | Use |
|------|-----|
| `-SkipTests` | Iteration and UI smoke when tests already passed this session, or when failures are clearly pre-existing — note them and proceed |
| `-SkipBuild` | **Relaunch only** — use during iteration when a packaged build already succeeded after your latest edits; still stops app and runs `winapp run` |
| `-SkipLaunch` | Build/test only (no relaunch) — not for UI verification of code changes |
| `-CleanAppData` | `winapp run --clean` — reset package LocalState |
| `-InstallWinAppCli` | Install CLI when missing |

Step-by-step equivalent:

```powershell
pwsh -File scripts/windows/Stop-ChurchPresenter.ps1
dotnet test tests/ChurchPresenter.Core.Tests/ChurchPresenter.Core.Tests.csproj
dotnet test tests/ChurchPresenter.App.Tests/ChurchPresenter.App.Tests.csproj
pwsh -File scripts/windows/Build-PackagedApp.ps1 -SkipStop
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode Detach
```

## Phase 2 — WinApp MCP (only when the task needs it)

Skip Phase 2 entirely for routine UI/design tasks unless the user asks or you are testing **functionality**.

**Forbidden launch paths (MCP or not):**

- `dotnet run --project apps/windows/ChurchPresenter/...`
- `launch_app` on `ChurchPresenter.exe`
- Visual Studio F5 / "Debug Installed App Package"
- Running the raw `.exe` from `bin/...` without `winapp run`

**When MCP is in scope — attach path:**

1. Relaunch first (`Run-PackagedApp.ps1 -Mode Detach` or orchestrator without `-SkipLaunch`)
2. Read `.agent-logs/last-run.json` → `mcpAttachArgs.pid` (or `pid`)
3. `CallMcpTool` → server **`project-0-church-presenter-winapp`** → `attach_to_pid` with `{"pid": <number>}`
4. `get_snapshot` (`maxDepth` 3–4). If `wait_for_input_idle` returns NotImplemented, skip it.
5. Run only the scenarios the task needs from `references/churchpresenter-ui-flows.md`
6. Assert with `read_element`, `find_elements`, `take_screenshot_optimized` as needed

`Run-PackagedApp.ps1` verifies the process survives 2s after detach launch and writes `mcpServer` / `mcpAttachArgs` into `last-run.json`.

Fallback: `attach_to_app` with `ChurchPresenter` only when a single instance is guaranteed.

## Phase 3 — Debug-output capture (use this by default)

`ILogger` → `AddDebug()` → `OutputDebugString`, plus first/second-chance exceptions. **Prefer this build path for almost all runs** — surfacing and fixing the errors the app logs is the point. Capture with:

```powershell
pwsh -File scripts/windows/Stop-ChurchPresenter.ps1
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput
```

- Tee to `.agent-logs/debug-output-*.log`; managed crashes include file:line from PDBs.
- **MCP can attach alongside `--debug-output`.** `winapp --debug-output` is the debug-output listener; the WinApp MCP is a UIA client, not a debugger, so you can launch DebugOutput in the background, attach MCP to the launched PID, and drive the UI to reproduce while the log records everything. Do not attach a *second real debugger* (Visual Studio) at the same time.
- Get the launched PID from the log line `... launched (PID: N)` (DebugOutput mode does not write `last-run.json` until it exits).

### Crash / fail-fast debugging — add `-Symbols`

For a crash, especially a native fail-fast (e.g. `RoFailFast 0xC000027B`, process exit code `3221226107`) that bypasses managed handlers, **always** use the symbols path to resolve native frames:

```powershell
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput -Symbols
```

- First run downloads symbols (can take minutes); the resolved stack (e.g. `Microsoft_UI_Xaml!TransformAndClipStack`) and a `.dmp` are written under `%LocalAppData%\Temp\winapp-dumps\`.
- Native fail-fasts produce no managed exception, so the resolved native stack + dump are the only way to locate the offending closed/cross-thread `CompositionObject`.

See `references/troubleshooting.md` for all failure modes and `references/winapp-mcp-tools.md` for tool-by-tool guidance.

## Phase 4 — Cleanup (optional)

```powershell
pwsh -File scripts/windows/Stop-ChurchPresenter.ps1
pwsh -File scripts/windows/Unregister-PackagedApp.ps1
```

## Script reference

| Script | Purpose |
|--------|---------|
| `Invoke-AgentE2EVerification.ps1` | Full orchestrator |
| `Build-PackagedApp.ps1` | `dotnet build` x64 Debug |
| `Run-PackagedApp.ps1` | `winapp run` with **built** `AppxManifest.xml`; Detach / DebugOutput / Foreground |
| `Stop-ChurchPresenter.ps1` | Close app before build |
| `Install-WinAppCli.ps1` | Install Microsoft winapp CLI |
| `Unregister-PackagedApp.ps1` | Remove dev package registration |

## MCP tool patterns

Most tools need **`appId`** from attach. Prefer **`automationId`** over visible **name**. For a per-tool description and the situations each one fits, read `references/winapp-mcp-tools.md`.

### Ground truth before concluding (don't guess)

- The UIA snapshot, the rendered pixels, and the XAML source are three different views — cross-check them. If something "isn't there" in a snapshot, confirm with `take_screenshot_optimized` and by reading the page's `.xaml` before concluding it is missing or broken. Controls can be in a popup/overlay layer (see dialogs below), virtualized off-screen, or simply not yet realized.
- **The user may be interacting with the same running app concurrently.** State can change between calls (a dialog you opened may have been dismissed, a different item selected). Re-`invalidate_cache` + re-snapshot/screenshot to re-establish ground truth rather than assuming your last action's result.
- When driving a specific control, open its `.xaml` to confirm the real `x:Name`/`AutomationProperties.AutomationId`, control type, and whether it lives inside an `ItemsControl` template, a `Flyout`, or a `ContentDialog`. This is faster and more reliable than inferring structure from the snapshot alone.

### Session lifecycle

- **`attach_to_pid`** — primary for ChurchPresenter after `Run-PackagedApp.ps1`
- **`attach_to_app`** — only when PID unknown and one instance exists
- **`launch_app`** — **not** for packaged ChurchPresenter
- **`list_apps`** — confirm an app is still alive (a crashed app shows "process may have exited"), **`close_app`** — teardown

### Interaction

- **`get_snapshot`** → map tree; note AutomationIds
- **`click_element`** → simulates a click; pair with **`wait_for_element`** for the result
- **`invoke_element`** → fires the element's UIA pattern (Invoke/Toggle, and Select for selection items). Use it for `SelectorBar`/`SelectorBarItem` and other `ListItem` selection controls (selecting a tab), `ContentDialog` buttons, and toggles — cases where a plain click may not change selection/state.
- **`read_element`** → check `IsEnabled` / `IsOffscreen` before acting (e.g. the Editor/Reflow tabs are disabled until a presentation is open)
- **`fill_form`**, **`select_option`** (ComboBox), **`press_key`** / **`press_key_combo`** (e.g. `RETURN` to accept a `ContentDialog` default, `ESCAPE` to cancel)
- **`wait_for_condition`** / **`wait_for_element`** — never fixed sleeps for async UI
- **`invalidate_cache`** after navigation or any non-MCP UI change

### Evidence

- **`take_screenshot_optimized`** (`maxTokens` to cap size)
- **`screenshot_diff`**, **`get_tree_hash`**
- **`start_event_monitor`** → **`get_event_log`** for flaky UI

### WinUI edge cases

| Situation | Tooling |
|-----------|---------|
| Top-level tab switch (`SelectorBar`) | `invoke_element` on the `*SelectorItem` (fires the selection pattern). Check `read_element` `IsEnabled` first for workspace-gated tabs (Editor/Reflow). |
| ContentDialog | Action buttons render in a popup/overlay layer and often are **not** in the main-window `get_snapshot`. `invoke_element` by button name, `press_key RETURN`/`ESCAPE` for default/cancel, or `list_desktop_windows` + `get_snapshot_hwnd`/`click_element_hwnd`. Read the dialog's `.xaml` to confirm button names. |
| Virtualized lists | `scroll_into_view`, `realize_virtualized_item`, `find_item_by_property` |
| Duplicate labels | `find_all_elements` + `index` |
| Separate windows | `get_snapshot_hwnd`, `click_element_hwnd` |

## Stable AutomationIds (shell)

`ShowSelectorItem`, `EditorSelectorItem`, `SettingsSelectorItem`, `MediaPanelToggleButton`, `AudienceTitleToggle`, `StageTitleToggle`

Add `AutomationProperties.AutomationId` in XAML for any new agent-tested control.

## Exit criteria

Work is **not complete** until:

1. Relevant tests pass (unless explicitly skipped with reason)
2. Packaged **build** succeeds after the final code edit
3. For **functional** or **crash** tasks: app **relaunched** and behavior confirmed (MCP and/or `DebugOutput` logs as appropriate)
4. For **UI/design** tasks: build passes; visual review is the user's unless they asked for MCP verification
5. Failures documented with `DebugOutput` logs and/or MCP screenshots when MCP or crash debugging was used

If you made another code edit after verifying, re-run the gates that apply to that task before claiming done.
