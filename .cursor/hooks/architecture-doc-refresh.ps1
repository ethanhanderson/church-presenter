param(
    [ValidateSet("track", "stop")]
    [string]$Mode = "stop"
)

$ErrorActionPreference = "Stop"

function Write-EmptyHookResult {
    [Console]::Out.WriteLine("{}")
}

function Write-HookResult {
    param([hashtable]$Result)

    $json = $Result | ConvertTo-Json -Depth 8 -Compress
    [Console]::Out.WriteLine($json)
}

function Get-RepoRoot {
    try {
        $root = (& git rev-parse --show-toplevel 2>$null).Trim()
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            return $root
        }
    }
    catch {
        # Hooks should fail open if git is unavailable.
    }

    return (Get-Location).Path
}

$repoRoot = Get-RepoRoot
$normalizedRepoRoot = ($repoRoot -replace "\\", "/").TrimEnd("/")
$stateDirectory = Join-Path $repoRoot ".cursor/hooks/state"
$statePath = Join-Path $stateDirectory "architecture-doc-refresh-session.json"

function Convert-ToRepoPath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $path = $PathValue.Trim().Trim('"') -replace "\\", "/"
    if ($path.StartsWith($normalizedRepoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $path = $path.Substring($normalizedRepoRoot.Length).TrimStart("/")
    }

    if ($path.StartsWith("./", [StringComparison]::Ordinal)) {
        $path = $path.Substring(2)
    }

    return $path
}

function Get-StringValues {
    param($Value)

    if ($null -eq $Value) {
        return
    }

    if ($Value -is [string]) {
        $Value
        return
    }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            Get-StringValues $Value[$key]
        }
        return
    }

    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        foreach ($item in $Value) {
            Get-StringValues $item
        }
        return
    }

    foreach ($property in $Value.PSObject.Properties) {
        Get-StringValues $property.Value
    }
}

function Test-AppImplementationPath {
    param([string]$RepoPath)

    if ([string]::IsNullOrWhiteSpace($RepoPath)) {
        return $false
    }

    if ($RepoPath -notmatch "^apps/(windows|macos)/") {
        return $false
    }

    if ($RepoPath -match "/(bin|obj|artifacts)/") {
        return $false
    }

    if ($RepoPath -match "\.(md|mdc)$") {
        return $false
    }

    return $true
}

function Test-ArchitectureDocPath {
    param([string]$RepoPath)

    return -not [string]::IsNullOrWhiteSpace($RepoPath) -and $RepoPath -match "^docs/architecture/"
}

function Read-State {
    if (-not (Test-Path $statePath)) {
        return [pscustomobject]@{
            appPaths = @()
            architectureDocsTouched = $false
            prompted = $false
            updatedAt = $null
            lastPromptedAt = $null
        }
    }

    try {
        return Get-Content -Raw -Path $statePath | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{
            appPaths = @()
            architectureDocsTouched = $false
            prompted = $false
            updatedAt = $null
            lastPromptedAt = $null
        }
    }
}

function Write-State {
    param($State)

    if (-not (Test-Path $stateDirectory)) {
        New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    }

    $State | ConvertTo-Json -Depth 8 | Set-Content -Path $statePath -Encoding utf8
}

function Clear-State {
    if (Test-Path $statePath) {
        Remove-Item -Path $statePath -Force
    }
}

function Get-ChangedRepoPaths {
    try {
        $lines = & git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>$null
    }
    catch {
        return @()
    }

    $paths = foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) {
            continue
        }

        $path = $line.Substring(3)
        if ($path -match " -> ") {
            $path = ($path -split " -> ")[-1]
        }

        Convert-ToRepoPath $path
    }

    return @($paths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
}

function Track-EditedPaths {
    $inputJson = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($inputJson)) {
        Write-EmptyHookResult
        return
    }

    try {
        $payload = $inputJson | ConvertFrom-Json
    }
    catch {
        Write-EmptyHookResult
        return
    }

    $paths = @(Get-StringValues $payload |
        ForEach-Object { Convert-ToRepoPath $_ } |
        Where-Object {
            Test-AppImplementationPath $_ -or Test-ArchitectureDocPath $_
        } |
        Sort-Object -Unique)

    if ($paths.Count -eq 0) {
        Write-EmptyHookResult
        return
    }

    $state = Read-State
    $existingAppPaths = @($state.appPaths)
    $appPaths = @($paths | Where-Object { Test-AppImplementationPath $_ })
    $architectureDocsTouched = [bool]$state.architectureDocsTouched -or
        [bool](@($paths | Where-Object { Test-ArchitectureDocPath $_ }).Count -gt 0)

    if ($appPaths.Count -gt 0) {
        $state.appPaths = @($existingAppPaths + $appPaths | Sort-Object -Unique)
        $state.prompted = $false
    }

    $state.architectureDocsTouched = $architectureDocsTouched
    $state.updatedAt = (Get-Date).ToString("o")
    Write-State $state
    Write-EmptyHookResult
}

function Invoke-StopCheck {
    $state = Read-State
    $trackedAppPaths = @($state.appPaths | Where-Object { Test-AppImplementationPath $_ } | Sort-Object -Unique)

    if ($trackedAppPaths.Count -eq 0 -or [bool]$state.prompted) {
        Write-EmptyHookResult
        return
    }

    $changedPaths = Get-ChangedRepoPaths
    if ([bool]$state.architectureDocsTouched) {
        Clear-State
        Write-EmptyHookResult
        return
    }

    if ($changedPaths.Count -gt 0) {
        $changedSet = @{}
        foreach ($path in $changedPaths) {
            $changedSet[$path] = $true
        }

        $trackedAppPaths = @($trackedAppPaths | Where-Object { $changedSet.ContainsKey($_) })
        if ($trackedAppPaths.Count -eq 0) {
            Clear-State
            Write-EmptyHookResult
            return
        }
    }

    $platforms = @()
    if (@($trackedAppPaths | Where-Object { $_ -match "^apps/windows/" }).Count -gt 0) {
        $platforms += "Windows"
    }
    if (@($trackedAppPaths | Where-Object { $_ -match "^apps/macos/" }).Count -gt 0) {
        $platforms += "macOS"
    }
    if ($platforms.Count -eq 0) {
        $platforms = @("native app")
    }

    $pathLines = @($trackedAppPaths | Select-Object -First 80 | ForEach-Object { "- $_" })
    if ($trackedAppPaths.Count -gt 80) {
        $pathLines += "- ... $($trackedAppPaths.Count - 80) more tracked app paths"
    }

    $promptLines = @(
        "A native app feature implementation appears to have completed in this session. Before finishing, run the project architecture doc refresh workflow using the continual-learning pattern.",
        "",
        "Detected app area: $($platforms -join ', ')",
        "",
        "Changed app files detected by the hook:",
        $pathLines,
        "",
        "Workflow:",
        "- Read `.cursor/skills/architecture-doc-refresh/SKILL.md`.",
        "- Follow the skill's continual-learning-style orchestration: keep this parent flow light, launch exactly one focused subagent for the documentation pass, and return the subagent result.",
        "- Use the Architecture Docs Maintainer project agent metadata at `.cursor/skills/architecture-doc-refresh/agents/openai.yaml` when available; otherwise launch one general-purpose subagent with the skill instructions and the changed paths above.",
        "",
        "Non-negotiables:",
        "- Treat `docs/reference/propresenter/**` as read-only behavior reference.",
        "- Update only relevant `docs/architecture/**` files when the docs need durable architecture changes.",
        "- Use Microsoft Learn MCP or Apple/Sosumi MCP tools as needed for platform documentation.",
        "- If no architecture update is needed, make no edits and report that the docs are already aligned."
    )

    $state.prompted = $true
    $state.lastPromptedAt = (Get-Date).ToString("o")
    Write-State $state

    Write-HookResult @{
        followup_message = ($promptLines -join "`n")
    }
}

try {
    if ($Mode -eq "track") {
        Track-EditedPaths
    }
    else {
        Invoke-StopCheck
    }
}
catch {
    Write-EmptyHookResult
}
