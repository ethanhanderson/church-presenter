# Packaged verification troubleshooting

## winapp CLI not found

```powershell
winget install Microsoft.winappcli --source winget
# or
pwsh -File scripts/windows/Install-WinAppCli.ps1
```

Restart terminal. Verify: `winapp --help`.

## AppX file locked (MSB3026 / MSB3027)

1. `pwsh -File scripts/windows/Stop-ChurchPresenter.ps1`
2. Retry build once.
3. If lock lists **Microsoft Visual Studio** and no `ChurchPresenter` process: ask user to stop debugging in VS — do not kill `devenv`.

## REGDB_E_CLASSNOTREG / app exits within seconds of launch

**Symptom:** `TypeInitializationException` / `COMException REGDB_E_CLASSNOTREG` from `WindowsAppRuntime.DeploymentManagerCS.AutoInitialize`, or `Run-PackagedApp.ps1` reports the process died within 2s.

**Common causes:**

| Cause | Fix |
|-------|-----|
| Wrong launch path (`dotnet run`, raw `.exe`, `launch_app`) | Use `Run-PackagedApp.ps1 -Mode Detach` only |
| `winapp run` used source `Package.appxmanifest` | Scripts use **built** `AppxManifest.xml` under `bin/.../win-x64` (includes `Microsoft.WindowsAppRuntime.1.8` `PackageDependency`). Rebuild if missing. |
| Stale dev registration | `Unregister-PackagedApp.ps1` then relaunch with `-Clean` |

Capture full stack:

```powershell
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput
```

Read `.agent-logs/debug-output-*.log`.

## winapp run fails — manifest / package

1. Build first: `Build-PackagedApp.ps1`
2. Confirm built manifest exists: `bin/x64/Debug/.../win-x64/AppxManifest.xml` with `PackageDependency Name="Microsoft.WindowsAppRuntime.1.8"`
3. Source manifest `Package.appxmanifest` is **not** passed to winapp — only the build output manifest
4. Stale registration: `Unregister-PackagedApp.ps1`

## MCP server not found (`server: winapp`)

Cursor exposes WinApp MCP as **`project-0-church-presenter-winapp`** (see `mcps/project-0-church-presenter-winapp/`). The display name in `.cursor/mcp.json` may still be `winapp`.

```text
CallMcpTool server=project-0-church-presenter-winapp toolName=attach_to_pid arguments={"pid": <from last-run.json>}
```

`.agent-logs/last-run.json` includes `mcpServer` and `mcpAttachArgs`.

## MCP attach finds no process

- Read `.agent-logs/last-run.json` — confirm `pid` and `phase: running`
- If launch script failed the 2s survival check, use `DebugOutput` (see REGDB section)
- Use `attach_to_pid` with parameter **`pid`** (not `processId`)
- Prefer PID attach over `attach_to_app` when multiple instances are possible

## `wait_for_input_idle` returns NotImplemented

Known WinApp MCP limitation on some WinUI 3 hosts. Fallback:

1. `get_snapshot` with `maxDepth` 3–4
2. `find_elements` with `idContains` for the target control
3. Short `Start-Sleep` only when snapshot already shows the expected tree

## Switching tabs / navigation looks stale

- Top-level tabs are `SelectorBar` → `SelectorBarItem` (UIA `ListItem`). Selecting a tab uses the **selection pattern**, so use **`invoke_element`** on the `*SelectorItem` (it fires Select). A plain `click_element` may not change the selected tab.
- Workspace-gated tabs (`EditorSelectorItem`, `ReflowSelectorItem`) are **disabled** until a presentation is open — `read_element` → `IsEnabled` first; open/create a presentation to enable them.
- `invalidate_cache` then re-snapshot after navigation; confirm with `take_screenshot_optimized`.
- Frame-hosted pages may need deeper `maxDepth` or `nameContains` filters.

## A control "isn't showing up" / an action seems to do nothing

Before concluding a control is missing or a fix failed, get ground truth from all three views — they differ:

1. `take_screenshot_optimized` (what actually rendered),
2. the page's `.xaml` source (real names, control types, whether it's inside an `ItemsControl` template / `Flyout` / `ContentDialog`),
3. `get_snapshot` / `find_elements`.

`ContentDialog` action buttons render in a popup/overlay layer and frequently are **not** in the main-window snapshot — that does not mean they are absent. Use `invoke_element` by button name, `press_key RETURN`/`ESCAPE` (default/cancel), or `list_desktop_windows` + `get_snapshot_hwnd`/`click_element_hwnd`. Also remember the **user may be interacting with the same app concurrently**, so re-snapshot to re-establish state.

## Capturing logs (default) and crash stacks (symbols)

Use the debug-output path for essentially every run so logged errors surface and get fixed:

```powershell
pwsh -File scripts/windows/Stop-ChurchPresenter.ps1
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput
```

The WinApp MCP is a UIA client, **not** a debugger, so you can attach MCP to the launched PID and drive the UI to reproduce *while* `--debug-output` records the log. Do not attach a second real debugger (Visual Studio) at the same time. Read `.agent-logs/debug-output-*.log`.

For a crash, especially a native fail-fast (process exit code `3221226107` / `0xC000027B`) that bypasses managed handlers, **always add `-Symbols`** to resolve native frames:

```powershell
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput -Symbols
```

First run downloads symbols (minutes). The resolved stack and a `.dmp` land in `%LocalAppData%\Temp\winapp-dumps\`.

## RoFailFast 0xC000027B (composition)

Native fail-fast from a closed/cross-thread `CompositionObject` — it produces no managed exception, so capture with `-Mode DebugOutput -Symbols` and read the resolved native stack (e.g. `Microsoft_UI_Xaml!TransformAndClipStack` points at a disposed `CompositionClip`/visual still in the render tree) plus the `.dmp`. See AGENTS.md WinUI gotchas for the safe composition-teardown sequence. Known sensitive path: re-attaching the Show preview while a slide is live on output.

## Clean state

```powershell
pwsh -File scripts/windows/Invoke-AgentE2EVerification.ps1 -CleanAppData
```
