[CmdletBinding()]
param(
    [int]$Port = 5198,
    [string]$Model = "simulated-gemini-flash",
    [switch]$ConnectivityOnly,
    [switch]$KeepChanges,
    [switch]$KeepLocalState
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function Wait-ForSimulator {
    param(
        [string]$Endpoint,
        [System.Diagnostics.Process]$Process,
        [string]$StandardErrorPath
    )

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        if ($Process.HasExited) {
            $errorText = if (Test-Path $StandardErrorPath) {
                Get-Content -Raw $StandardErrorPath
            }
            else {
                ""
            }

            throw "Simulator exited before it was ready. Exit code: $($Process.ExitCode). $errorText"
        }

        try {
            Invoke-RestMethod `
                -Uri "$Endpoint/models" `
                -Headers @{ Authorization = "Bearer local-simulator-key" } `
                -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Simulator did not become ready at $Endpoint."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$endpoint = "http://127.0.0.1:$Port/v1"
$baseUrl = "http://127.0.0.1:$Port"
$logRoot = Join-Path $repoRoot ".agentbridge\simulator-logs"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stdoutPath = Join-Path $logRoot "simulator-$stamp.out.log"
$stderrPath = Join-Path $logRoot "simulator-$stamp.err.log"
$oldApiKey = $env:AGENTBRIDGE_STARK_API_KEY
$process = $null

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

    Write-Section "Build"
    dotnet build .\OpenContext.AgentBridge.sln
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    Write-Section "Start Simulator"
    Write-Host "Endpoint: $endpoint"
    Write-Host "Logs:"
    Write-Host "  $stdoutPath"
    Write-Host "  $stderrPath"

    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--no-build",
            "--project",
            ".\src\OpenContext.AgentBridge.SimulatedStark",
            "--urls",
            $baseUrl
        ) `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    $env:AGENTBRIDGE_STARK_API_KEY = "local-simulator-key"
    Wait-ForSimulator -Endpoint $endpoint -Process $process -StandardErrorPath $stderrPath

    Write-Section "Smoke"
    & .\scripts\Invoke-StarkSmoke.ps1 `
        -Endpoint $endpoint `
        -Model $Model `
        -SkipBuild `
        -ConnectivityOnly:$ConnectivityOnly `
        -KeepChanges:$KeepChanges `
        -KeepLocalState:$KeepLocalState
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Section "Stop Simulator"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
    }

    $env:AGENTBRIDGE_STARK_API_KEY = $oldApiKey
    Pop-Location
}
