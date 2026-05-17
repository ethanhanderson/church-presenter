Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Json([hashtable]$Payload) {
    $json = $Payload | ConvertTo-Json -Compress -Depth 10
    [Console]::Out.Write($json)
}

function Get-StatePath {
    return Join-Path $PSScriptRoot 'state\windows-app-organization-review.json'
}

function Read-State {
    $path = Get-StatePath
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }

    try {
        $raw = Get-Content -LiteralPath $path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $null
        }

        return $raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Save-State([bool]$Pending, [string[]]$TouchedPaths) {
    $path = Get-StatePath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    @{
        pending = $Pending
        touchedPaths = @($TouchedPaths | Sort-Object -Unique)
        lastUpdatedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
}

try {
    $rawInput = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        Write-Json @{}
        exit 0
    }

    $payload = $rawInput | ConvertFrom-Json
    if ([string]$payload.status -ne 'completed') {
        Write-Json @{}
        exit 0
    }

    if ([int]$payload.loop_count -gt 0) {
        Write-Json @{}
        exit 0
    }

    $state = Read-State
    if ($null -eq $state -or -not [bool]$state.pending) {
        Write-Json @{}
        exit 0
    }

    $touchedPaths = @($state.touchedPaths | Sort-Object -Unique)
    Save-State $false $touchedPaths

    $pathsText = ''
    if ($touchedPaths.Count -gt 0) {
        $pathsText = ($touchedPaths | Select-Object -First 12) -join ', '
    }

    $message = @"
Run the Windows app organization follow-up now.

Read and apply `.cursor/skills/windows-app-organization-maintenance/SKILL.md`. If the changed Windows app work is substantial, use the specialist agent exposed by `.cursor/skills/windows-app-organization-maintenance/agents/openai.yaml`.

Review the recent Windows app edits for:
- splitting oversized or mixed-purpose files into smaller focused files
- moving code to the correct Windows project and folder
- keeping namespaces aligned with folders
- preserving the canonical app workspace/content layout (`Libraries`, `Playlists`, `Presentations`, `Configurations`, `Themes`, `Media`, `Audits`, with machine-local state under `MachineState`)
- removing unused code and stale files left behind by recent changes

Only make safe cleanup changes that fit Microsoft Learn WinUI and .NET organization guidance, then verify and finish.

Touched paths: $pathsText
"@

    Write-Json @{
        followup_message = $message.Trim()
    }
    exit 0
}
catch {
    Write-Json @{}
    exit 0
}
