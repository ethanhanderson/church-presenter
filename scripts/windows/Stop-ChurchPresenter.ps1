# Gracefully stop running ChurchPresenter instances before packaged builds.

[CmdletBinding()]
param(
    [int] $CloseTimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ChurchPresenter.Paths.ps1"

$paths = Get-ChurchPresenterProjectPaths
$processName = $paths.ProcessName

$running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
if (-not $running) {
    Write-Host "No $processName process is running."
    exit 0
}

Write-Host "Stopping $($running.Count) $processName process(es)..."
$running | Select-Object Id, ProcessName, MainWindowTitle, Path | Format-Table -AutoSize | Out-String | Write-Host

foreach ($process in $running) {
    if ($process.MainWindowHandle -ne 0) {
        [void]$process.CloseMainWindow()
    }
}

$ids = @($running.Id)
$stillRunning = @(Get-Process -Name $processName -ErrorAction SilentlyContinue |
    Where-Object { $ids -contains $_.Id })

if ($stillRunning) {
    $stillRunning | Wait-Process -Timeout $CloseTimeoutSeconds -ErrorAction SilentlyContinue
}

$remaining = @(Get-Process -Name $processName -ErrorAction SilentlyContinue |
    Where-Object { $ids -contains $_.Id })

if ($remaining) {
    Write-Warning "Graceful close timed out; stopping remaining process(es)."
    $remaining | Stop-Process -Force
    $remaining | Wait-Process -Timeout $CloseTimeoutSeconds
}

Write-Host "$processName stopped."
