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
        return @{
            pending = $false
            touchedPaths = @()
        }
    }

    try {
        $raw = Get-Content -LiteralPath $path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return @{
                pending = $false
                touchedPaths = @()
            }
        }

        $loaded = $raw | ConvertFrom-Json
        return @{
            pending = [bool]$loaded.pending
            touchedPaths = @($loaded.touchedPaths)
        }
    }
    catch {
        return @{
            pending = $false
            touchedPaths = @()
        }
    }
}

function Save-State([hashtable]$State) {
    $path = Get-StatePath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $payload = @{
        pending = [bool]$State.pending
        touchedPaths = @($State.touchedPaths | Sort-Object -Unique)
        lastUpdatedUtc = [DateTime]::UtcNow.ToString('o')
    }

    $payload | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Get-InputText {
    return [Console]::In.ReadToEnd()
}

function Add-RelevantPath([System.Collections.Generic.HashSet[string]]$Results, [string]$PathText) {
    if ([string]::IsNullOrWhiteSpace($PathText)) {
        return
    }

    $normalized = $PathText.Replace('\', '/')
    if (
        $normalized -like '*apps/windows/*' -or
        $normalized -like '*tests/ChurchPresenter.App.Tests/*' -or
        $normalized -like '*tests/ChurchPresenter.Core.Tests/*'
    ) {
        [void]$Results.Add($normalized)
    }
}

function Get-RelevantPaths($Payload) {
    $results = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if ($Payload.PSObject.Properties.Name -contains 'file_path') {
        Add-RelevantPath $results ([string]$Payload.file_path)
    }

    if ($Payload.PSObject.Properties.Name -contains 'edits' -and $null -ne $Payload.edits) {
        foreach ($edit in @($Payload.edits)) {
            if ($null -eq $edit) {
                continue
            }

            if ($edit.PSObject.Properties.Name -contains 'file_path') {
                Add-RelevantPath $results ([string]$edit.file_path)
            }
        }
    }

    return @($results)
}

try {
    $rawInput = Get-InputText
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        Write-Json @{}
        exit 0
    }

    $payload = $rawInput | ConvertFrom-Json
    $relevantPaths = @(Get-RelevantPaths $payload)
    if ($relevantPaths.Count -eq 0) {
        Write-Json @{}
        exit 0
    }

    $state = Read-State
    $combined = @($state.touchedPaths) + $relevantPaths
    $state.pending = $true
    $state.touchedPaths = @($combined | Sort-Object -Unique)
    Save-State $state

    Write-Json @{}
    exit 0
}
catch {
    Write-Json @{}
    exit 0
}
