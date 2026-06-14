# Install Microsoft winapp CLI when missing (required for packaged agent launch/logs).

[CmdletBinding()]
param(
    [switch] $SkipIfPresent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$existing = Get-Command winapp -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "winapp CLI already available: $($existing.Source)"
    exit 0
}

if ($SkipIfPresent) {
    exit 1
}

Write-Host "Installing Microsoft winapp CLI via winget..."
winget install Microsoft.winappcli --source winget --accept-package-agreements --accept-source-agreements

$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
    [System.Environment]::GetEnvironmentVariable('Path', 'User')

$installed = Get-Command winapp -ErrorAction SilentlyContinue
if (-not $installed) {
    throw "winapp still not on PATH after install. Open a new terminal or add the install directory to PATH."
}

Write-Host "winapp CLI installed: $($installed.Source)"
