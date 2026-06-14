# Windows apps

## Church Presenter (WinUI 3)

- **Project**: [ChurchPresenter/ChurchPresenter.csproj](ChurchPresenter/ChurchPresenter.csproj)
- **Application layer** (models + non-UI services shared with tests): [ChurchPresenter.Application/ChurchPresenter.Application.csproj](ChurchPresenter.Application/ChurchPresenter.Application.csproj)
- **Core library** (portable `.cpres` ZIP/JSON): [ChurchPresenter.Core/ChurchPresenter.Core.csproj](ChurchPresenter.Core/ChurchPresenter.Core.csproj)

### Build

**Toolchain:** .NET SDK **10.0.301** (`global.json`), Windows App SDK **2.2.0**, TFMs `net10.0` / `net10.0-windows10.0.26100.0`. Repo config: `global.json` + `.editorconfig` only.

From repo root:

```powershell
dotnet build apps/windows/ChurchPresenter/ChurchPresenter.csproj -c Debug -p:Platform=x64
```

Or:

```powershell
pwsh -File scripts/windows/Build-PackagedApp.ps1
```

### Run and verify (agents and packaged testing)

The **only** supported packaged run workflow uses Microsoft **winapp CLI** scripts. **WinApp MCP** is optional — use only for crash repro, functional testing, or when the user asks:

```powershell
pwsh -File scripts/windows/Invoke-AgentE2EVerification.ps1
```

While iterating: **build** to verify compile; **relaunch** when runtime behavior matters or before WinApp MCP. **MCP** only for crash repro, functional testing, or when the user asks — not for routine UI/design. Relaunch-only: `-SkipTests -SkipBuild`.

See `.cursor/skills/winapp-mcp-winui/SKILL.md` and `.cursor/rules/windows-build-verification.mdc`.

Prerequisite (once): `winget install Microsoft.winappcli`

**`scripts/windows/`** (packaged build/run only):

| Script | Purpose |
| --- | --- |
| `Build-PackagedApp.ps1` | `dotnet build` x64 Debug |
| `Run-PackagedApp.ps1` | `winapp run` (Detach / DebugOutput) |
| `Stop-ChurchPresenter.ps1` | Stop running app before rebuild |
| `Invoke-AgentE2EVerification.ps1` | Tests → build → launch orchestration |
| `Install-WinAppCli.ps1` | One-time winapp CLI install helper |
| `Unregister-PackagedApp.ps1` | Remove stale debug package registration |
| `ChurchPresenter.Paths.ps1` | Shared path helpers (dot-sourced by scripts above) |

After launch, read `.agent-logs/last-run.json` and attach WinApp MCP server **`project-0-church-presenter-winapp`** with `attach_to_pid` and `pid`. Scripts pass the **built** `bin/.../win-x64/AppxManifest.xml` to `winapp run` (required for Windows App Runtime package dependency).

Do **not** use `dotnet run` on the WinUI project or launch `ChurchPresenter.exe` directly for packaged verification.

### Code style (`dotnet format`)

`dotnet format` does not load `ChurchPresenter.slnx` yet; format (or verify) each project file. From repo root:

```powershell
dotnet format apps/windows/ChurchPresenter.Core/ChurchPresenter.Core.csproj
dotnet format apps/windows/ChurchPresenter.Application/ChurchPresenter.Application.csproj
dotnet format apps/windows/ChurchPresenter/ChurchPresenter.csproj
```

### Tests

From repo root:

```bash
dotnet test ../../tests/ChurchPresenter.Core.Tests/ChurchPresenter.Core.Tests.csproj
dotnet test ../../tests/ChurchPresenter.App.Tests/ChurchPresenter.App.Tests.csproj
```
