[CmdletBinding()]
param(
    [int]$AgentBridgePort = 5330,
    [int]$OpenWebUiPort = 3100,
    [string]$Workspace,
    [string]$Model = "gemini-2.5-flash",
    [string]$ContainerName = "agentbridge-openwebui",
    [string]$VolumeName = "agentbridge-openwebui",
    [string]$Image = "ghcr.io/open-webui/open-webui:main",
    [switch]$UseSimulator,
    [int]$SimulatorPort = 5331,
    [switch]$Recreate,
    [switch]$SkipBuild,
    [switch]$UseExistingProviderConfig
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function Wait-ForUrl {
    param(
        [string]$Url,
        [int]$Attempts = 90
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -TimeoutSec 2 -UseBasicParsing | Out-Null
            return
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    throw "Timed out waiting for $Url."
}

function Wait-ForDocker {
    for ($attempt = 1; $attempt -le 90; $attempt++) {
        try {
            docker version --format "{{.Server.Version}}" 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 2
    }

    throw "Docker daemon is not available. Start Docker Desktop and retry."
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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$workspaceRoot = if ($Workspace) {
    Resolve-Path $Workspace
}
else {
    $repoRoot
}
$agentBridgeBase = "http://127.0.0.1:$AgentBridgePort"
$agentBridgeListenUrl = "http://0.0.0.0:$AgentBridgePort"
$openWebUiBase = "http://127.0.0.1:$OpenWebUiPort"
$containerAgentBridgeBase = "http://host.docker.internal:$AgentBridgePort/v1"
$simulatorBase = "http://127.0.0.1:$SimulatorPort"
$simulatorEndpoint = "$simulatorBase/v1"
$logRoot = Join-Path $repoRoot ".agentbridge\openwebui-smoke-logs"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serverOut = Join-Path $logRoot "agentbridge-server-$stamp.out.log"
$serverErr = Join-Path $logRoot "agentbridge-server-$stamp.err.log"
$simulatorOut = Join-Path $logRoot "simulator-$stamp.out.log"
$simulatorErr = Join-Path $logRoot "simulator-$stamp.err.log"
$pidPath = Join-Path $logRoot "agentbridge-server.pid"
$simulatorPidPath = Join-Path $logRoot "simulator.pid"
$simulatorApiKey = "local-simulator-key"

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

    if ($UseSimulator -and $UseExistingProviderConfig) {
        throw "Use either -UseSimulator or -UseExistingProviderConfig, not both."
    }

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build .\OpenContext.AgentBridge.sln
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    if ($UseSimulator) {
        Write-Section "Start Simulator"
        if (Test-Path -LiteralPath $simulatorPidPath) {
            $oldSimulatorPid = Get-Content -LiteralPath $simulatorPidPath -ErrorAction SilentlyContinue
            if ($oldSimulatorPid) {
                Stop-Process -Id ([int]$oldSimulatorPid) -Force -ErrorAction SilentlyContinue
            }
        }

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
    }

    Write-Section "Start AgentBridge Server"
    if (Test-Path -LiteralPath $pidPath) {
        $oldPid = Get-Content -LiteralPath $pidPath -ErrorAction SilentlyContinue
        if ($oldPid) {
            Stop-Process -Id ([int]$oldPid) -Force -ErrorAction SilentlyContinue
        }
    }

    if ($UseSimulator) {
        $env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
        $env:AGENTBRIDGE_GATEWAY_ENDPOINT = $simulatorEndpoint
        $env:AGENTBRIDGE_GATEWAY_MODEL = "simulated-gemini-flash"
        $env:AGENTBRIDGE_GATEWAY_API_KEY = $simulatorApiKey
    }
    elseif (-not $UseExistingProviderConfig) {
        if (-not $env:AGENTBRIDGE_GEMINI_API_KEY) {
            $env:AGENTBRIDGE_GEMINI_API_KEY = [Environment]::GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY", "User")
        }

        if (-not $env:AGENTBRIDGE_GEMINI_API_KEY) {
            throw "AGENTBRIDGE_GEMINI_API_KEY was not found. Set it or pass -UseExistingProviderConfig after configuring AgentBridge provider environment variables."
        }

        $env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
        $env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
        $env:AGENTBRIDGE_OPENAI_MODEL = $Model
        $env:AGENTBRIDGE_OPENAI_API_KEY = $env:AGENTBRIDGE_GEMINI_API_KEY
    }

    $env:AGENTBRIDGE_SERVER_WORKSPACE = $workspaceRoot
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
    Set-Content -LiteralPath $pidPath -Value $server.Id
    Wait-ForUrl -Url "$agentBridgeBase/v1/models" -Attempts 60

    Write-Section "Start Open WebUI"
    Wait-ForDocker
    if ($Recreate) {
        docker rm -f $ContainerName 2>$null | Out-Null
        docker volume rm $VolumeName 2>$null | Out-Null
    }
    else {
        docker rm -f $ContainerName 2>$null | Out-Null
    }

    docker run -d `
        --name $ContainerName `
        -p "$($OpenWebUiPort):8080" `
        -e WEBUI_AUTH=False `
        -e ENABLE_PERSISTENT_CONFIG=False `
        -e ENABLE_OLLAMA_API=False `
        -e OPENAI_API_BASE_URL=$containerAgentBridgeBase `
        -e OPENAI_API_KEY=agentbridge-local `
        -e DEFAULT_MODELS=agentbridge-agent `
        -v "$($VolumeName):/app/backend/data" `
        $Image | Out-Null
    Wait-ForUrl -Url $openWebUiBase -Attempts 120

    Write-Section "Ready"
    Write-Host "Open WebUI: $openWebUiBase"
    Write-Host "AgentBridge API from host: $agentBridgeBase/v1"
    Write-Host "Open WebUI upstream URL: $containerAgentBridgeBase"
    Write-Host "Model: agentbridge-agent"
    if ($UseSimulator) {
        Write-Host "Simulator API from host: $simulatorEndpoint"
        Write-Host "Simulator stdout: $simulatorOut"
        Write-Host "Simulator stderr: $simulatorErr"
    }
    Write-Host "Disposable sign-in: admin@localhost / admin"
    Write-Host "AgentBridge stdout: $serverOut"
    Write-Host "AgentBridge stderr: $serverErr"
    Write-Host ""
    Write-Host "Stop commands:"
    Write-Host "  docker rm -f $ContainerName"
    Write-Host "  Stop-Process -Id $($server.Id) -Force"
    if ($UseSimulator) {
        Write-Host "  Stop-Process -Id $($simulator.Id) -Force"
    }
}
finally {
    Pop-Location
}
