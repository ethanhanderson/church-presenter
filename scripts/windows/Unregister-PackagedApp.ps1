# Remove sideloaded ChurchPresenter development package registration.

[CmdletBinding()]
param(
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ChurchPresenter.Paths.ps1"

Assert-WinAppCli | Out-Null

$paths = Get-ChurchPresenterProjectPaths
$args = @('unregister', '--manifest', $paths.Manifest)
if ($Force) { $args += '--force' }

& winapp @args
exit $LASTEXITCODE
