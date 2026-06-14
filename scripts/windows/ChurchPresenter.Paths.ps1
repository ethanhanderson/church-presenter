# Shared path and tooling helpers for ChurchPresenter agent E2E scripts.

Set-StrictMode -Version Latest

function Get-RepoRoot {
    $root = Resolve-Path (Join-Path $PSScriptRoot '../..')
    return $root.Path
}

function Get-WinAppMcpServerName {
    # Cursor MCP tool server id (display name in .cursor/mcp.json may still be "winapp").
    return 'project-0-church-presenter-winapp'
}

function Get-ChurchPresenterProjectPaths {
    $repoRoot = Get-RepoRoot
    [ordered]@{
        RepoRoot    = $repoRoot
        ProjectFile = Join-Path $repoRoot 'apps/windows/ChurchPresenter/ChurchPresenter.csproj'
        Manifest    = Join-Path $repoRoot 'apps/windows/ChurchPresenter/Package.appxmanifest'
        AgentLogs   = Join-Path $repoRoot '.agent-logs'
        ProcessName = 'ChurchPresenter'
        Configuration = 'Debug'
        Platform    = 'x64'
    }
}

function Get-ChurchPresenterRunFolder {
    param(
        [string] $Configuration = 'Debug',
        [string] $Platform = 'x64'
    )

    $repoRoot = Get-RepoRoot
    $searchRoot = Join-Path $repoRoot "apps/windows/ChurchPresenter/bin/$Platform/$Configuration"
    if (-not (Test-Path $searchRoot)) {
        throw "Build output not found at '$searchRoot'. Run Build-PackagedApp.ps1 first."
    }

    $exeFiles = @(Get-ChildItem -Path $searchRoot -Recurse -Filter ChurchPresenter.exe -File -ErrorAction SilentlyContinue)
    if (-not $exeFiles) {
        throw "ChurchPresenter.exe not found under '$searchRoot'. Run Build-PackagedApp.ps1 first."
    }

    # Prefer the newest build output; when multiple TFMs exist after an upgrade, avoid launching a stale folder.
    $chosen = $exeFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1

    return $chosen.Directory.FullName
}

function Get-ChurchPresenterAppXPath {
    param(
        [string] $Configuration = 'Debug',
        [string] $Platform = 'x64'
    )

    return Get-ChurchPresenterRunFolder -Configuration $Configuration -Platform $Platform
}

function Get-ChurchPresenterBuiltManifest {
    param(
        [string] $Configuration = 'Debug',
        [string] $Platform = 'x64'
    )

    $runFolder = Get-ChurchPresenterRunFolder -Configuration $Configuration -Platform $Platform
    $builtManifest = Join-Path $runFolder 'AppxManifest.xml'
    if (Test-Path $builtManifest) {
        return $builtManifest
    }

    $paths = Get-ChurchPresenterProjectPaths
    return $paths.Manifest
}

function Get-WinAppLayoutDirectory {
    $logs = Ensure-AgentLogsDirectory
    $layout = Join-Path $logs 'winapp-layout'
    if (-not (Test-Path $layout)) {
        New-Item -ItemType Directory -Path $layout -Force | Out-Null
    }
    return $layout
}

function Ensure-AgentLogsDirectory {
    $logs = (Get-ChurchPresenterProjectPaths).AgentLogs
    if (-not (Test-Path $logs)) {
        New-Item -ItemType Directory -Path $logs -Force | Out-Null
    }
    return $logs
}

function Assert-WinAppCli {
    $winapp = Get-Command winapp -ErrorAction SilentlyContinue
    if ($winapp) {
        return $winapp.Source
    }

    throw @"
Microsoft winapp CLI is required but not on PATH.
Install (once): winget install Microsoft.winappcli --source winget
Then restart the terminal and re-run.
Docs: https://learn.microsoft.com/windows/apps/dev-tools/winapp-cli/
"@
}

function Write-AgentRunManifest {
    param(
        [hashtable] $Data
    )

    $logs = Ensure-AgentLogsDirectory
    $manifestPath = Join-Path $logs 'last-run.json'
    $payload = [ordered]@{
        timestampUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    foreach ($key in $Data.Keys) {
        $payload[$key] = $Data[$key]
    }

    $payload | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    return $manifestPath
}
