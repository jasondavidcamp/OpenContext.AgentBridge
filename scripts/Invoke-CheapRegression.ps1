[CmdletBinding()]
param(
    [int]$AgentBridgePort = 5330,
    [int]$OpenWebUiPort = 3100,
    [int]$SimulatorPort = 5331,
    [switch]$SkipEditCanary,
    [switch]$SkipOpenWebUi,
    [switch]$SkipFormat,
    [switch]$KeepServices
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Stop-LocalDotNetServices {
    $processes = Get-Process OpenContext.AgentBridge.Server, OpenContext.AgentBridge.SimulatedGateway -ErrorAction SilentlyContinue
    if (-not $processes) {
        return
    }

    $processes | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        try {
            [void]$process.WaitForExit(5000)
        }
        catch {
        }
    }
}

function Invoke-ReservedTermScan {
    $firstTerm = -join ([char[]](115, 116, 97, 114, 107))
    $secondTerm = -join ([char[]](100, 108, 97))
    $pattern = "$firstTerm|$secondTerm"
    $paths = @("README.md", "docs", "scripts", "src", "tests", ".github") |
        Where-Object { Test-Path -LiteralPath $_ }

    if (-not $paths) {
        throw "Reserved term scan did not find any paths to scan."
    }

    if (Get-Command rg -ErrorAction SilentlyContinue) {
        $matches = & rg -n -i $pattern @paths 2>$null
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            $matches
            throw "Reserved term scan found matches."
        }

        if ($exitCode -ne 1) {
            throw "Reserved term scan failed with exit code $exitCode."
        }

        Write-Host "Reserved term scan clean."
        return
    }

    $files = foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Get-Item -LiteralPath $path
            continue
        }

        Get-ChildItem -LiteralPath $path -Recurse -File |
            Where-Object {
                $relativePath = Resolve-Path -LiteralPath $_.FullName -Relative
                -not ($relativePath -match '(^|[\\/])(bin|obj)([\\/]|$)')
            }
    }

    $fallbackMatches = $files |
        Select-String -Pattern $pattern -ErrorAction SilentlyContinue
    if ($fallbackMatches) {
        $fallbackMatches |
            ForEach-Object { "$($_.Path):$($_.LineNumber):$($_.Line.Trim())" }
        throw "Reserved term scan found matches."
    }

    Write-Host "Reserved term scan clean."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Push-Location $repoRoot
try {
    if (-not $KeepServices) {
        Write-Section "Stop Local .NET Services"
        Stop-LocalDotNetServices
    }

    Write-Section "Build"
    Invoke-Native "dotnet" @("build", ".\OpenContext.AgentBridge.sln")

    Write-Section "Local Server Smoke"
    & (Join-Path $PSScriptRoot "Invoke-LocalServerSmoke.ps1") -SkipBuild

    if (-not $SkipEditCanary) {
        Write-Section "Local Server Edit Canary"
        & (Join-Path $PSScriptRoot "Invoke-LocalServerEditCanary.ps1") -SkipBuild
    }

    if (-not $SkipOpenWebUi) {
        Write-Section "Open WebUI Simulator Smoke"
        & (Join-Path $PSScriptRoot "Invoke-OpenWebUiSmoke.ps1") `
            -UseSimulator `
            -SkipBuild `
            -AgentBridgePort $AgentBridgePort `
            -OpenWebUiPort $OpenWebUiPort `
            -SimulatorPort $SimulatorPort

        if (-not $KeepServices) {
            Stop-LocalDotNetServices
        }
    }

    Write-Section "Tests"
    Invoke-Native "dotnet" @("test", ".\OpenContext.AgentBridge.sln")

    if (-not $SkipFormat) {
        Write-Section "Format"
        Invoke-Native "dotnet" @("format", ".\OpenContext.AgentBridge.sln", "--verify-no-changes")
    }

    Write-Section "Reserved Term Scan"
    Invoke-ReservedTermScan

    Write-Section "Result"
    Write-Host "Cheap regression passed."
}
finally {
    if (-not $KeepServices) {
        Stop-LocalDotNetServices
    }

    Pop-Location
}
