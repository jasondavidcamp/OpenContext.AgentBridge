[CmdletBinding()]
param(
    [int]$Port = 5320,
    [int]$SimulatorPort = 5321,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
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
$serverBase = "http://127.0.0.1:$Port"
$simulatorBase = "http://127.0.0.1:$SimulatorPort"
$simulatorEndpoint = "$simulatorBase/v1"
$logRoot = Join-Path $repoRoot ".agentbridge\server-smoke-logs"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serverOut = Join-Path $logRoot "server-$stamp.out.log"
$serverErr = Join-Path $logRoot "server-$stamp.err.log"
$simulatorOut = Join-Path $logRoot "simulator-$stamp.out.log"
$simulatorErr = Join-Path $logRoot "simulator-$stamp.err.log"
$simulatorApiKey = "local-simulator-key"
$server = $null
$simulator = $null
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
    Wait-ForModels -BaseUrl $simulatorBase -ApiKey $simulatorApiKey

    Write-Section "Start Server"
    $env:AGENTBRIDGE_SERVER_WORKSPACE = $repoRoot
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
            $serverBase
        ) `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr
    Wait-ForModels -BaseUrl $serverBase

    Write-Section "Models"
    Invoke-RestMethod -Uri "$serverBase/v1/models" |
        ConvertTo-Json -Depth 10

    Write-Section "Raw Proxy"
    Invoke-RestMethod `
        -Uri "$serverBase/v1/chat/completions" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{
            model = "simulated-gemini-flash"
            messages = @(
                @{ role = "user"; content = "Return exactly: model test ok" }
            )
            stream = $false
        } | ConvertTo-Json -Depth 10) |
        ConvertTo-Json -Depth 10

    Write-Section "Agent Mode"
    $agentResponse = Invoke-WebRequest `
        -Uri "$serverBase/v1/chat/completions" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{
            model = "agentbridge-agent"
            messages = @(
                @{ role = "user"; content = "Only inspect examples/powershell-sandbox and return a concise summary. Do not edit files." }
            )
            stream = $false
        } | ConvertTo-Json -Depth 10)
    $agentCompletion = $agentResponse.Content | ConvertFrom-Json
    $agentCompletion | ConvertTo-Json -Depth 10

    $conversationId = $agentResponse.Headers["X-AgentBridge-Conversation"]
    if (-not $conversationId) {
        throw "Agent mode response did not include X-AgentBridge-Conversation."
    }

    if ($agentCompletion.agentbridge.conversation_id -ne $conversationId) {
        throw "Agent mode metadata conversation id did not match the response header."
    }

    Write-Section "Agent Conversation Details"
    $conversationDetails = Invoke-RestMethod -Uri "$serverBase/v1/agentbridge/conversations/$conversationId"
    $conversationDetails | ConvertTo-Json -Depth 10

    if ($conversationDetails.agentbridge.conversation_id -ne $conversationId) {
        throw "Conversation details metadata did not match the requested conversation id."
    }

    Write-Section "Agent Mode Streaming"
    $streamResponse = Invoke-WebRequest `
        -Uri "$serverBase/v1/chat/completions" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{
            model = "agentbridge-agent"
            messages = @(
                @{ role = "user"; content = "Only inspect examples/powershell-sandbox and return a concise summary. Do not edit files." }
            )
            stream = $true
        } | ConvertTo-Json -Depth 10)

    if (-not ($streamResponse.Content -match "chat\.completion\.chunk")) {
        throw "Streaming response did not include OpenAI-compatible chunk objects."
    }

    if (-not ($streamResponse.Content -match "data: \[DONE\]")) {
        throw "Streaming response did not include the final [DONE] marker."
    }

    if (-not ($streamResponse.Content -match '"agentbridge"')) {
        throw "Streaming response did not include AgentBridge metadata."
    }

    $streamResponse.Content -split "\r?\n" |
        Where-Object { $_ } |
        Select-Object -First 8

    Write-Section "Logs"
    Write-Host "Server stdout: $serverOut"
    Write-Host "Server stderr: $serverErr"
    Write-Host "Simulator stdout: $simulatorOut"
    Write-Host "Simulator stderr: $simulatorErr"
}
finally {
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        [void]$server.WaitForExit(5000)
    }

    if ($simulator -and -not $simulator.HasExited) {
        Stop-Process -Id $simulator.Id -Force -ErrorAction SilentlyContinue
        [void]$simulator.WaitForExit(5000)
    }

    foreach ($name in $oldEnvironment.Keys) {
        Set-Item -Path "env:$name" -Value $oldEnvironment[$name]
    }

    Pop-Location
}
