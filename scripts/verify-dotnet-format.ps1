#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projects = @(
    'apps/windows/ChurchPresenter.Core/ChurchPresenter.Core.csproj',
    'apps/windows/ChurchPresenter.Application/ChurchPresenter.Application.csproj',
    'apps/windows/ChurchPresenter/ChurchPresenter.csproj',
    'tests/ChurchPresenter.Core.Tests/ChurchPresenter.Core.Tests.csproj',
    'tests/ChurchPresenter.App.Tests/ChurchPresenter.App.Tests.csproj'
)
foreach ($rel in $projects) {
    $csproj = Join-Path $repoRoot $rel
    if (-not (Test-Path $csproj)) {
        throw "Missing project: $csproj"
    }
    Write-Host "dotnet format --verify-no-changes $rel"
    dotnet format $csproj --verify-no-changes
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host 'All projects passed format verification.'
