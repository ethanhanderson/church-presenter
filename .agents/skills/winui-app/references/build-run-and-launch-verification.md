---
title: Build, Run, and Launch Verification
priority: CRITICAL
tags: build, run, launch, verification, packaged, unpackaged, debugging
sources:
  - https://learn.microsoft.com/windows/apps/get-started/start-here
  - https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-packaged-apps
  - https://learn.microsoft.com/windows/apps/dev-tools/winapp-cli/debugging
---

## What This Reference Is For

Use this file when the task involves building, running, launch failures, startup crashes, or final verification that a WinUI app actually opens on the current machine.

## ChurchPresenter (this repository)

**Only workflow:** `.cursor/skills/winapp-mcp-winui/SKILL.md` + `scripts/windows/Invoke-AgentE2EVerification.ps1`.

Agents must run it for big refactors, new WinUI features, user-requested testing, or before completing substantial `apps/windows/**` work.

**Iteration:** Stop + **relaunch** before MCP or runtime verification; rebuild when needed (`-SkipTests -SkipBuild` for relaunch-only). **MCP:** task-by-task — crash repro, functional testing, agent-driven flows only; skip for UI/design unless the user asks.

**Forbidden:** Visual Studio F5, `dotnet run` on `ChurchPresenter.csproj`, raw `.exe` launch, WinApp MCP `launch_app` on `ChurchPresenter.exe`, attaching MCP for every styling change, driving MCP against a stale PID.

## Required Workflow (generic WinUI — other repos)

1. Identify build target: project, configuration, platform, packaged vs unpackaged.
2. Verify after meaningful edits; avoid repeated packaged builds while the app is open.
3. Run non-packaged tests first when useful.
4. Preflight-close the app process before packaged builds that copy into `AppX`.
5. Launch using the path that matches the deployment model.
6. Confirm a real top-level window (title, responsiveness, no immediate crash).

## Packaged vs Unpackaged Rules

- Packaged apps use package identity and package-backed storage.
- Unpackaged apps must not assume package identity.
- Do not mix packaged-only APIs into unpackaged startup paths.

## Debugging Startup Failures (ChurchPresenter)

```powershell
pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput
```

Read `.agent-logs/debug-output-*.log`. Troubleshooting: `.cursor/skills/winapp-mcp-winui/references/troubleshooting.md`.

**Launch manifest:** `Run-PackagedApp.ps1` passes the **build-generated** `bin/.../win-x64/AppxManifest.xml` (includes `Microsoft.WindowsAppRuntime.1.8` package dependency). Using source `Package.appxmanifest` causes immediate `REGDB_E_CLASSNOTREG` bootstrap failure.

**MCP attach:** server id `project-0-church-presenter-winapp`; tool `attach_to_pid` with `pid` from `.agent-logs/last-run.json`.

## Exit Criteria

- Build succeeds.
- App launches through the correct workflow for this repo.
- UI verification complete when the change warrants it.
