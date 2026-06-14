# End-to-end agent verification: tests, packaged build, packaged launch for WinApp MCP.
# Iteration: -SkipTests (common while editing); -SkipBuild when rebuild already succeeded after latest edits (still stops + relaunches).

[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $SkipBuild,
    [switch] $SkipLaunch,
    [switch] $CleanAppData,
    [switch] $InstallWinAppCli
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ChurchPresenter.Paths.ps1"

$repoRoot = Get-RepoRoot
$paths = Get-ChurchPresenterProjectPaths
$summary = [ordered]@{
    repoRoot = $repoRoot
    steps    = @()
    success  = $true
}

function Add-Step {
    param([string] $Name, [bool] $Ok, [string] $Detail = '')
    $summary.steps += [ordered]@{
        name   = $Name
        ok     = $Ok
        detail = $Detail
    }
    if (-not $Ok) { $summary.success = $false }
}

Write-Host "=== ChurchPresenter agent E2E verification ===" -ForegroundColor Cyan

if ($InstallWinAppCli) {
    try {
        & "$PSScriptRoot/Install-WinAppCli.ps1"
        Add-Step 'install-winapp-cli' $true
    }
    catch {
        Add-Step 'install-winapp-cli' $false $_.Exception.Message
        throw
    }
}

if (-not $SkipLaunch) {
    try {
        Assert-WinAppCli | Out-Null
        Add-Step 'assert-winapp-cli' $true
    }
    catch {
        Add-Step 'assert-winapp-cli' $false $_.Exception.Message
        Write-Host $_.Exception.Message
        Write-Host "Re-run with -InstallWinAppCli or install manually: winget install Microsoft.winappcli --source winget"
        exit 1
    }
}

& "$PSScriptRoot/Stop-ChurchPresenter.ps1" | Out-Null
Add-Step 'stop-app' $true

if (-not $SkipTests) {
    Write-Host "`n--- dotnet test (Core) ---" -ForegroundColor Yellow
    dotnet test (Join-Path $repoRoot 'tests/ChurchPresenter.Core.Tests/ChurchPresenter.Core.Tests.csproj') --nologo
    $coreOk = $LASTEXITCODE -eq 0
    Add-Step 'test-core' $coreOk "exit=$LASTEXITCODE"

    Write-Host "`n--- dotnet test (App) ---" -ForegroundColor Yellow
    dotnet test (Join-Path $repoRoot 'tests/ChurchPresenter.App.Tests/ChurchPresenter.App.Tests.csproj') --nologo
    $appOk = $LASTEXITCODE -eq 0
    Add-Step 'test-app' $appOk "exit=$LASTEXITCODE"

    if (-not ($coreOk -and $appOk)) {
        $summary | ConvertTo-Json -Depth 6 | Write-Host
        throw 'Unit tests failed. Fix tests before UI verification (see skill for the pre-existing-failure -SkipTests path).'
    }
}

if (-not $SkipBuild) {
    Write-Host "`n--- packaged build ---" -ForegroundColor Yellow
    & "$PSScriptRoot/Build-PackagedApp.ps1" -SkipStop
    Add-Step 'build-packaged' $true (Get-ChurchPresenterAppXPath)
}

if ($SkipLaunch) {
    Add-Step 'launch-packaged' $true 'skipped'
}
else {
    Write-Host "`n--- packaged launch (detach) ---" -ForegroundColor Yellow
    $launchArgs = @{
        Mode = 'Detach'
    }
    if ($CleanAppData) { $launchArgs.Clean = $true }

    & "$PSScriptRoot/Run-PackagedApp.ps1" @launchArgs
    $launchOk = $LASTEXITCODE -eq 0
    $lastRun = Join-Path $paths.AgentLogs 'last-run.json'
    $detail = if (Test-Path $lastRun) { Get-Content $lastRun -Raw } else { 'no last-run.json' }
    Add-Step 'launch-packaged' $launchOk $detail
    if (-not $launchOk) {
        $summary | ConvertTo-Json -Depth 6 | Write-Host
        exit 1
    }
}

$summary.next = [ordered]@{
    skill          = '.cursor/skills/winapp-mcp-winui/SKILL.md'
    mcpServer      = (Get-WinAppMcpServerName)
    mcpServerAlias = 'winapp'
    attach         = 'Read .agent-logs/last-run.json then CallMcpTool attach_to_pid with mcpAttachArgs.pid'
    uiFlows        = '.cursor/skills/winapp-mcp-winui/references/churchpresenter-ui-flows.md'
    tools          = '.cursor/skills/winapp-mcp-winui/references/winapp-mcp-tools.md'
    onFailure      = 'Run-PackagedApp.ps1 -Mode DebugOutput (add -Symbols for crashes); see skill.'
}

$summaryPath = Write-AgentRunManifest @{
    phase   = 'e2e-ready'
    summary = $summary
}

Write-Host ""
Write-Host "=== E2E verification ready for WinApp MCP ===" -ForegroundColor Green
Write-Host "Summary: $summaryPath"
$summary | ConvertTo-Json -Depth 8 | Write-Host
