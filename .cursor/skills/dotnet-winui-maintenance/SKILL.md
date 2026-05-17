---
name: dotnet-winui-maintenance
description: Runs maintenance-day workflows for .NET and WinUI 3 — dotnet format, Roslyn code-style and quality analyzers, NuGet outdated/vulnerable package reports, optional duplication scans, structural triage, then safe fixes. Use when paying down C# or WinUI tech debt, periodic cleanup, or applying a dotnet equivalent of the peth-eth/refactor maintenance skill to apps/windows or other .NET solutions.
---

# .NET / WinUI maintenance (refactor-style)

Acts as a codebase maintenance specialist for **C#, WinUI 3, and .NET desktop** projects: run CLI tools, triage output, apply safe fixes, and flag judgment calls. Behavior mirrors [peth-eth/refactor `SKILL.md`](https://github.com/peth-eth/refactor/blob/main/SKILL.md) (phases, batching, summary table) with **.NET-first tooling**.

**First-party grounding:** Roslyn analyzers, `dotnet format`, and `dotnet package list` are documented on [Microsoft Learn — .NET code analysis overview](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview) and [dotnet format](https://learn.microsoft.com/dotnet/core/tools/dotnet-format).

## Tool mapping (JS/Python refactor skill → .NET)

| Upstream role | Typical JS/Python tool | .NET / WinUI equivalent |
|---------------|------------------------|-------------------------|
| Dead exports / unused imports | knip | `dotnet format style` (e.g. **IDE0005** unused usings), build with **EnforceCodeStyleInBuild** + EditorConfig severities; **CA/IDE** quality rules via SDK **NetAnalyzers**; optional **SonarAnalyzer.CSharp** for broader smells |
| Duplication | jscpd | Optional **`npx jscpd`** on `*.cs` / `*.xaml` trees if Node is available; else **SonarCloud/SonarQube** duplication metrics, or commercial **JetBrains dupFinder** / **inspectcode** where licensed |
| Lint / format | ESLint / Biome / ruff | **`dotnet format`** (whitespace, style, analyzers subcommands); optional **StyleCop.Analyzers** |
| Outdated deps | npm outdated / pip | **`dotnet package list --outdated`** (restore first on SDKs before .NET 10 — see [dotnet package list](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list)); optional **`dotnet package list --vulnerable`** (SDK 9.0.300+); optional global **dotnet-outdated** for extra reporting |

**Note:** `dotnet format` ships with the **.NET 6+ SDK** as the `dotnet format` command — prefer the SDK command over legacy global-tool workflows unless the repo standardizes otherwise.

## Before you start

Two modes (same idea as upstream):

1. **Quick** — Phase 0 + Phase 1 + Phase 4 only. Skip structural and WinUI pattern passes.
2. **Full** — All phases.

**Batching:** Keep each phase under **5 files** changed before a commit; split sub-phases if needed (upstream batch rule).

## Phase 0: Delete dead weight (always first)

Remove noise before refactors:

1. **Unused usings / fixable style** — `dotnet format style --diagnostics IDE0005 --severity info` on the target project(s), then review diff.
2. **Debug noise** — Search for `Debug.WriteLine`, `Console.WriteLine`, or `#if DEBUG` blocks that should not ship; remove or gate.
3. **Commented-out code** — Delete large stale blocks (verify with git history if unsure).
4. **Commit separately:** `chore: remove dead code before maintenance pass`

**Solution shape:** This repo may use **`.slnx`**; `dotnet format` might not load `.slnx`. Prefer formatting **per `.csproj`** (Core, Application, WinUI app, tests) — align with repo `AGENTS.md` / `Directory.Build.props`.

## Phase 1: Automated scans

Detect context, then run scans (adjust paths to the solution root, e.g. `apps/windows/ChurchPresenter/`).

### Always (.NET)

From repo root or project directory (PowerShell):

```powershell
dotnet build -c Debug --no-restore 2>$null; dotnet restore
dotnet format style --verify-no-changes
dotnet format whitespace --verify-no-changes
dotnet build /p:EnforceCodeStyleInBuild=true /p:TreatWarningsAsErrors=false
dotnet package list --outdated
```

If SDK supports it:

```powershell
dotnet package list --vulnerable
```

### Duplication (optional)

When Node is available:

```powershell
npx --yes jscpd apps/windows --min-lines 10 --reporters "consoleFull" --ignore "**/bin/**,**/obj/**,**/.vs/**"
```

Summarize findings as a numbered list with severity **FIX / CONSIDER / INFO**. Fix FIX-level items in Phase 4; ask before CONSIDER that deletes public API or changes behavior.

**Quick mode:** go to Phase 4 after Phase 1.

## Phase 2: Structural health (full mode)

1. **Oversized files** — Flag `*.cs` or `*.xaml` over ~300 lines; note extraction points (partial classes, user controls, helpers).
2. **Directory bloat** — Directories with 20+ loose files → suggest subfolders.
3. **WinUI composition** — Huge `MainWindow.xaml` / page XAML → `UserControl`, `DataTemplate`, or separate views.
4. **Tests** — Slow tests, empty tests, skipped tests (`[Fact(Skip = ...)]`), stale paths.

## Phase 3: WinUI / desktop patterns (full mode)

Flag behavior-preserving cleanups common in WinUI 3:

1. **Binding** — Prefer `x:Bind` with correct **default mode** for static POCOs; avoid patterns that fight change notification (see repo notes on WMC1506 / `OneTime` vs `OneWay`).
2. **ItemsRepeater** — Do not assume `DataContext` on items; prefer `Tag` + compiled bind patterns per project conventions.
3. **Platform guards** — Watch **CA1416** and `SupportedOSPlatform` usage for Windows-only APIs ([platform compatibility analyzer](https://learn.microsoft.com/dotnet/standard/analyzers/platform-compat-analyzer)).
4. **Lifecycle / theme** — Avoid late `Application.RequestedTheme` changes that throw; set on window/root early.
5. **Code-behind** — Large event handlers → commands / MVVM helpers (e.g. CommunityToolkit.Mvvm) when it matches existing architecture.

## Phase 4: Fix

1. **Safe auto-fixes** — `dotnet format` (without `--verify-no-changes`), targeted `dotnet format style --diagnostics ...`, analyzer fixes that do not alter semantics; remove confirmed dead code.
2. **Refactors** — Extract types from oversized files; deduplicate helpers flagged by jscpd/Sonar; consolidate trivial duplicates.
3. **Flag for user** — Major package upgrades, unclear “unused but public” API, broad moves that churn imports.

**Verify:** `dotnet build` (and `dotnet test` if tests exist) on affected configurations; for WinUI, follow repo rules (e.g. `-p:Platform=x64`).

## Output format

End with:

| Category | Found | Fixed | Flagged |
|----------|-------|-------|---------|
| Duplication | | | |
| Dead code / usings | | | |
| Analyzer / style | | | |
| Oversized files | | | |
| WinUI / desktop patterns | | | |
| Slow / weak tests | | | |
| Outdated / vulnerable packages | | | |

## Rules

- Prefer **behavior-preserving** edits; treat behavior changes as out of scope unless explicitly requested.
- Respect **EditorConfig**, **Directory.Build.props**, and repo analyzer settings.
- Prefer **small commits** per batch; never mix mass deletion and large refactors in one commit.
- When unsure if code is truly dead (reflection, codegen, XAML `x:FieldModifier`, etc.), **flag** instead of deleting.

## Feedback protocol

**Session start:** If `feedback.log` exists beside this `SKILL.md`, read it and apply logged preferences.

**During session:** Append user corrections that should persist:

```text
[YYYY-MM-DD] preference or correction here
```

## Additional resources

- [Overview of .NET source code analysis](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview)
- [dotnet format](https://learn.microsoft.com/dotnet/core/tools/dotnet-format)
- [dotnet package list](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list)
- Project WinUI patterns: `.agents/skills/dotnet-winui/SKILL.md` (repo); desktop UI automation: `.cursor/skills/winapp-mcp-winui/SKILL.md`
