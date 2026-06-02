[CmdletBinding()]
param(
    [int]$Port = 5323,
    [string]$Model = "gemini-2.5-flash",
    [string]$Workspace,
    [switch]$SkipBuild,
    [switch]$IncludeAgentMode
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function Wait-ForModels {
    param([string]$BaseUrl)

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        try {
            Invoke-RestMethod -Uri "$BaseUrl/v1/models" -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $BaseUrl/v1/models."
}

function Get-GeminiApiKey {
    if ($env:AGENTBRIDGE_GEMINI_API_KEY) {
        return $env:AGENTBRIDGE_GEMINI_API_KEY
    }

    return [Environment]::GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY", "User")
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$workspaceRoot = if ($Workspace) {
    Resolve-Path $Workspace
}
else {
    $repoRoot
}

$serverBase = "http://127.0.0.1:$Port"
$logRoot = Join-Path $repoRoot ".agentbridge\server-gemini-canary-logs"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serverOut = Join-Path $logRoot "server-gemini-canary-$stamp.out.log"
$serverErr = Join-Path $logRoot "server-gemini-canary-$stamp.err.log"
$server = $null
$oldEnvironment = @{
    AGENTBRIDGE_SERVER_WORKSPACE = $env:AGENTBRIDGE_SERVER_WORKSPACE
    AGENTBRIDGE_MODEL_PROVIDER = $env:AGENTBRIDGE_MODEL_PROVIDER
    AGENTBRIDGE_OPENAI_ENDPOINT = $env:AGENTBRIDGE_OPENAI_ENDPOINT
    AGENTBRIDGE_OPENAI_MODEL = $env:AGENTBRIDGE_OPENAI_MODEL
    AGENTBRIDGE_OPENAI_API_KEY = $env:AGENTBRIDGE_OPENAI_API_KEY
    AGENTBRIDGE_LOG_MODEL_TRAFFIC = $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC
    AGENTBRIDGE_MAX_ITERATIONS = $env:AGENTBRIDGE_MAX_ITERATIONS
}

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

    $geminiApiKey = Get-GeminiApiKey
    if (-not $geminiApiKey) {
        throw "AGENTBRIDGE_GEMINI_API_KEY was not found in the process or user environment."
    }

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build .\OpenContext.AgentBridge.sln
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    Write-Section "Start Server"
    $env:AGENTBRIDGE_SERVER_WORKSPACE = $workspaceRoot
    $env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
    $env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
    $env:AGENTBRIDGE_OPENAI_MODEL = $Model
    $env:AGENTBRIDGE_OPENAI_API_KEY = $geminiApiKey
    $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC = "false"
    $env:AGENTBRIDGE_MAX_ITERATIONS = "4"

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

    Write-Section "Raw Proxy Canary"
    $expected = "server proxy ok"
    $rawResponse = Invoke-RestMethod `
        -Uri "$serverBase/v1/chat/completions" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{
            model = $Model
            messages = @(
                @{ role = "user"; content = "Return exactly: $expected" }
            )
            stream = $false
            max_tokens = 64
            temperature = 0
        } | ConvertTo-Json -Depth 10)

    $rawContent = [string]$rawResponse.choices[0].message.content
    Write-Host "Raw proxy content: $rawContent"
    if ($rawContent.Trim() -ne $expected) {
        $rawResponse | ConvertTo-Json -Depth 10
        throw "Raw proxy canary returned unexpected content."
    }

    if ($IncludeAgentMode) {
        Write-Section "Agent Mode Canary"
        $expectedAgentPath = "examples/powershell-sandbox/Get-Greeting.ps1"
        $agentResponse = Invoke-RestMethod `
            -Uri "$serverBase/v1/chat/completions" `
            -Method Post `
            -ContentType "application/json" `
            -Body (@{
                model = "agentbridge-agent"
                messages = @(
                    @{ role = "user"; content = "Use tools to read only $expectedAgentPath. Do not edit files. Return a concise final summary under 25 words." }
                )
                stream = $false
                max_tokens = 700
                temperature = 0
            } | ConvertTo-Json -Depth 10)

        $agentResponse | ConvertTo-Json -Depth 10
        if (-not $agentResponse.agentbridge.conversation_id) {
            throw "Agent mode canary did not include AgentBridge metadata."
        }

        if ($agentResponse.agentbridge.successful_tool_call_count -lt 1) {
            throw "Agent mode canary did not complete at least one successful tool call."
        }

        $conversationId = [string]$agentResponse.agentbridge.conversation_id
        $conversationDetails = Invoke-RestMethod -Uri "$serverBase/v1/agentbridge/conversations/$conversationId"
        $conversationDetails | ConvertTo-Json -Depth 10

        $expectedRead = @($conversationDetails.tool_calls) |
            Where-Object {
                $_.tool_name -eq "read_file" -and
                $_.is_success -eq $true -and
                ([string]$_.arguments_json).Contains($expectedAgentPath)
            } |
            Select-Object -First 1

        if (-not $expectedRead) {
            throw "Agent mode canary did not record a successful read_file call for $expectedAgentPath."
        }
    }

    Write-Section "Logs"
    Write-Host "Server stdout: $serverOut"
    Write-Host "Server stderr: $serverErr"

    Write-Section "Result"
    Write-Host "Gemini server canary passed."
}
finally {
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        [void]$server.WaitForExit(5000)
    }

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
