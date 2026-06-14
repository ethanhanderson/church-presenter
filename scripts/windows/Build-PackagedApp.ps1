# Build the packaged ChurchPresenter WinUI app (x64 Debug).

[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $Platform = 'x64',
    [switch] $SkipStop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ChurchPresenter.Paths.ps1"

$paths = Get-ChurchPresenterProjectPaths

if (-not $SkipStop) {
    & "$PSScriptRoot/Stop-ChurchPresenter.ps1"
}

Write-Host "Building ChurchPresenter ($Configuration|$Platform)..."
dotnet build $paths.ProjectFile -c $Configuration -p:Platform=$Platform --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$appXPath = Get-ChurchPresenterAppXPath -Configuration $Configuration -Platform $Platform
Write-Host "Build succeeded. AppX layout: $appXPath"

$manifestPath = Write-AgentRunManifest @{
    phase      = 'built'
    appXPath   = $appXPath
    manifest   = $paths.Manifest
    configuration = $Configuration
    platform   = $Platform
}

Write-Host "Wrote $manifestPath"
