[CmdletBinding()]
param(
    [int]$AgentBridgePort = 5320,
    [int]$SimulatorPort = 5321,
    [string]$Workspace,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Output ""
    Write-Output "== $Title =="
}

function Wait-ForModels {
    param(
        [string]$BaseUrl,
        [string]$ApiKey
    )

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        try {
            $headers = if ($ApiKey) {
                @{ Authorization = "Bearer $ApiKey" }
            }
            else {
                @{}
            }

            Invoke-RestMethod -Uri "$BaseUrl/v1/models" -Headers $headers -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $BaseUrl/v1/models."
}

function Stop-PidFileProcess {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $processId = Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $processId) {
        return
    }

    Stop-Process -Id ([int]$processId) -Force -ErrorAction SilentlyContinue
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$workspaceRoot = if ($Workspace) {
    Resolve-Path $Workspace
}
else {
    $repoRoot
}
$agentBridgeBase = "http://127.0.0.1:$AgentBridgePort"
$agentBridgeListenUrl = "http://0.0.0.0:$AgentBridgePort"
$simulatorBase = "http://127.0.0.1:$SimulatorPort"
$simulatorEndpoint = "$simulatorBase/v1"
$simulatorApiKey = "local-simulator-key"
$logRoot = Join-Path $repoRoot ".agentbridge\local-simulator-bridge"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serverOut = Join-Path $logRoot "agentbridge-server-$stamp.out.log"
$serverErr = Join-Path $logRoot "agentbridge-server-$stamp.err.log"
$simulatorOut = Join-Path $logRoot "simulator-$stamp.out.log"
$simulatorErr = Join-Path $logRoot "simulator-$stamp.err.log"
$serverPidPath = Join-Path $logRoot "agentbridge-server.pid"
$simulatorPidPath = Join-Path $logRoot "simulator.pid"
$oldEnvironment = @{
    AGENTBRIDGE_SERVER_WORKSPACE = $env:AGENTBRIDGE_SERVER_WORKSPACE
    AGENTBRIDGE_MODEL_PROVIDER = $env:AGENTBRIDGE_MODEL_PROVIDER
    AGENTBRIDGE_GATEWAY_ENDPOINT = $env:AGENTBRIDGE_GATEWAY_ENDPOINT
    AGENTBRIDGE_GATEWAY_MODEL = $env:AGENTBRIDGE_GATEWAY_MODEL
    AGENTBRIDGE_GATEWAY_API_KEY = $env:AGENTBRIDGE_GATEWAY_API_KEY
    AGENTBRIDGE_LOG_MODEL_TRAFFIC = $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC
}

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build .\OpenContext.AgentBridge.sln
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    Write-Section "Stop Previous Local Bridge"
    Stop-PidFileProcess -Path $serverPidPath
    Stop-PidFileProcess -Path $simulatorPidPath

    Write-Section "Start Simulator"
    $simulator = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--no-build",
            "--project",
            ".\src\OpenContext.AgentBridge.SimulatedGateway",
            "--urls",
            $simulatorBase
        ) `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $simulatorOut `
        -RedirectStandardError $simulatorErr
    Set-Content -LiteralPath $simulatorPidPath -Value $simulator.Id
    Wait-ForModels -BaseUrl $simulatorBase -ApiKey $simulatorApiKey

    Write-Section "Start AgentBridge Server"
    $env:AGENTBRIDGE_SERVER_WORKSPACE = $workspaceRoot
    $env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
    $env:AGENTBRIDGE_GATEWAY_ENDPOINT = $simulatorEndpoint
    $env:AGENTBRIDGE_GATEWAY_MODEL = "simulated-gemini-flash"
    $env:AGENTBRIDGE_GATEWAY_API_KEY = $simulatorApiKey
    $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC = "false"

    $server = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--no-build",
            "--project",
            ".\src\OpenContext.AgentBridge.Server",
            "--urls",
            $agentBridgeListenUrl
        ) `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr
    Set-Content -LiteralPath $serverPidPath -Value $server.Id
    Wait-ForModels -BaseUrl $agentBridgeBase

    Write-Section "Ready"
    Write-Output "AgentBridge API: $agentBridgeBase/v1"
    Write-Output "Simulator API: $simulatorEndpoint"
    Write-Output "Workspace: $workspaceRoot"
    Write-Output "AgentBridge stdout: $serverOut"
    Write-Output "AgentBridge stderr: $serverErr"
    Write-Output "Simulator stdout: $simulatorOut"
    Write-Output "Simulator stderr: $simulatorErr"
    Write-Output ""
    Write-Output "Try it:"
    Write-Output "  Invoke-RestMethod -Uri `"$agentBridgeBase/v1/models`""
    Write-Output "  Invoke-RestMethod -Uri `"$agentBridgeBase/v1/chat/completions`" -Method Post -ContentType `"application/json`" -Body (@{ model = `"agentbridge-agent`"; messages = @(@{ role = `"user`"; content = `"Inspect only README.md. Do not edit files. Return one sentence.`" }); stream = `$false } | ConvertTo-Json -Depth 10)"
    Write-Output ""
    Write-Output "Stop commands:"
    Write-Output "  Stop-Process -Id $($server.Id) -Force"
    Write-Output "  Stop-Process -Id $($simulator.Id) -Force"
}
finally {
    foreach ($name in $oldEnvironment.Keys) {
        if ($null -eq $oldEnvironment[$name]) {
            Remove-Item -Path "env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -Path "env:$name" -Value $oldEnvironment[$name]
        }
    }

    Pop-Location
}
