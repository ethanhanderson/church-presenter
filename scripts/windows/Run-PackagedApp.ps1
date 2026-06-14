# Launch ChurchPresenter with package identity via Microsoft winapp CLI.

[CmdletBinding()]
param(
    [ValidateSet('Detach', 'DebugOutput', 'Foreground')]
    [string] $Mode = 'Detach',

    [string] $Configuration = 'Debug',
    [string] $Platform = 'x64',
    [switch] $Clean,
    [switch] $Symbols,
    [switch] $UnregisterOnExit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ChurchPresenter.Paths.ps1"

Assert-WinAppCli | Out-Null

$paths = Get-ChurchPresenterProjectPaths
$runFolder = Get-ChurchPresenterRunFolder -Configuration $Configuration -Platform $Platform
$builtManifestPath = Get-ChurchPresenterBuiltManifest -Configuration $Configuration -Platform $Platform
$logsDir = Ensure-AgentLogsDirectory

$layoutDir = Get-WinAppLayoutDirectory

$winappArgs = @(
    'run',
    $runFolder,
    '--manifest', $builtManifestPath,
    '--output-appx-directory', $layoutDir,
    '--executable', 'ChurchPresenter.exe'
)

if ($Clean) { $winappArgs += '--clean' }

switch ($Mode) {
    'Detach' {
        $winappArgs += @('--detach', '--json')
    }
    'DebugOutput' {
        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $logFile = Join-Path $logsDir "debug-output-$timestamp.log"
        Write-Host "Capturing OutputDebugString + exceptions to console and $logFile"
        $winappArgs += '--debug-output'
        if ($Symbols) {
            Write-Host "Resolving native crash symbols (first run may take minutes)..."
            $winappArgs += '--symbols'
        }

        & winapp @winappArgs 2>&1 | Tee-Object -FilePath $logFile
        $exitCode = $LASTEXITCODE

        Write-AgentRunManifest @{
            phase        = 'debug-output-complete'
            mode         = $Mode
            runFolder    = $runFolder
            appXPath     = $runFolder
            logFile      = $logFile
            exitCode     = $exitCode
        } | Out-Null

        exit $exitCode
    }
    'Foreground' {
        if ($UnregisterOnExit) { $winappArgs += '--unregister-on-exit' }
    }
}

Write-Host "Launching packaged app from: $runFolder"
Write-Host "winapp $($winappArgs -join ' ')"

if ($Mode -eq 'Detach') {
    $json = & winapp @winappArgs
    if ($LASTEXITCODE -ne 0) {
        throw "winapp run failed: $json"
    }

    $launch = $null
    try {
        $launch = $json | ConvertFrom-Json
    }
    catch {
        throw "Could not parse winapp --json output: $json"
    }

    $pidValue = $null
    foreach ($name in @('ProcessId', 'pid', 'processId')) {
        $prop = $launch.PSObject.Properties[$name]
        if ($prop -and $prop.Value) {
            $pidValue = [int]$prop.Value
            break
        }
    }
    if (-not $pidValue) {
        throw "winapp --json did not return a PID: $json"
    }

    $aumidProp = $launch.PSObject.Properties['AUMID']
    $aumid = if ($aumidProp) { [string]$aumidProp.Value } else { $null }

    Start-Sleep -Seconds 2
    $alive = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if (-not $alive) {
        throw @"
ChurchPresenter exited within 2s of launch (PID $pidValue).
Re-run: pwsh -File scripts/windows/Run-PackagedApp.ps1 -Mode DebugOutput
Common cause: winapp used Package.appxmanifest without Windows App Runtime PackageDependency — scripts now use built AppxManifest.xml.
"@
    }

    $runManifestPath = Write-AgentRunManifest @{
        phase           = 'running'
        mode            = $Mode
        pid             = [int]$pidValue
        aumid           = $aumid
        runFolder       = $runFolder
        appXPath        = $runFolder
        builtManifest   = $builtManifestPath
        processName     = $paths.ProcessName
        mcpServer       = (Get-WinAppMcpServerName)
        mcpServerAlias  = 'winapp'
        mcpTool         = 'attach_to_pid'
        mcpAttachArgs   = @{ pid = [int]$pidValue }
        mainWindowTitle = $alive.MainWindowTitle
    }

    Write-Host ""
    Write-Host "Packaged app launched."
    Write-Host "  PID:            $pidValue"
    Write-Host "  Main window:    $($alive.MainWindowTitle)"
    Write-Host "  MCP server:     $(Get-WinAppMcpServerName)"
    Write-Host "  MCP attach:     attach_to_pid(pid=$pidValue)"
    Write-Host "  Run manifest:   $runManifestPath"
    Write-Host ""
    exit 0
}

& winapp @winappArgs
exit $LASTEXITCODE
