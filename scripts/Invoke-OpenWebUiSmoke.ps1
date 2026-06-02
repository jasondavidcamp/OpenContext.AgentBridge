[CmdletBinding()]
param(
    [int]$AgentBridgePort = 5330,
    [int]$OpenWebUiPort = 3100,
    [int]$SimulatorPort = 5331,
    [string]$Image = "ghcr.io/open-webui/open-webui:main",
    [string]$Email = "admin@localhost",
    [string]$Password = "admin",
    [string]$Prompt = "Inspect only README.md. Do not edit files. Return one sentence under 20 words.",
    [switch]$SkipStart,
    [switch]$SkipBuild,
    [switch]$UseSimulator,
    [switch]$UseExistingProviderConfig,
    [switch]$Recreate
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

function ConvertFrom-SseJson {
    param([string]$Content)

    $payloads = $Content -split "\r?\n" |
        Where-Object { $_.StartsWith("data: ", [StringComparison]::Ordinal) } |
        ForEach-Object { $_.Substring(6) }

    foreach ($payload in $payloads) {
        if ($payload -eq "[DONE]") {
            continue
        }

        $payload | ConvertFrom-Json
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$agentBridgeBase = "http://127.0.0.1:$AgentBridgePort"
$openWebUiBase = "http://127.0.0.1:$OpenWebUiPort"

Push-Location $repoRoot
try {
    if (-not $SkipStart) {
        Write-Section "Start Open WebUI Bridge"
        $startArgs = @{
            AgentBridgePort = $AgentBridgePort
            OpenWebUiPort = $OpenWebUiPort
            SimulatorPort = $SimulatorPort
            Image = $Image
        }

        if ($SkipBuild) {
            $startArgs.SkipBuild = $true
        }

        if ($UseSimulator) {
            $startArgs.UseSimulator = $true
        }

        if ($UseExistingProviderConfig) {
            $startArgs.UseExistingProviderConfig = $true
        }

        if ($Recreate) {
            $startArgs.Recreate = $true
        }

        & (Join-Path $PSScriptRoot "Start-OpenWebUiAgentBridge.ps1") @startArgs
    }

    Write-Section "Wait For Services"
    Wait-ForUrl -Url "$agentBridgeBase/v1/models" -Attempts 60
    Wait-ForUrl -Url $openWebUiBase -Attempts 120

    Write-Section "Sign In"
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $signin = Invoke-RestMethod `
        -Uri "$openWebUiBase/api/v1/auths/signin" `
        -Method Post `
        -ContentType "application/json" `
        -WebSession $session `
        -Body (@{
            email = $Email
            password = $Password
        } | ConvertTo-Json)

    if (-not $signin.token) {
        throw "Open WebUI sign-in did not return a token."
    }

    $headers = @{ Authorization = "Bearer $($signin.token)" }

    Write-Section "Open WebUI Models"
    $models = Invoke-RestMethod `
        -Uri "$openWebUiBase/api/models" `
        -Headers $headers `
        -WebSession $session
    $models | ConvertTo-Json -Depth 10

    $modelsJson = $models | ConvertTo-Json -Depth 20
    if (-not ($modelsJson -match '"agentbridge-agent"')) {
        throw "Open WebUI did not expose agentbridge-agent."
    }

    Write-Section "Open WebUI Streaming Chat"
    $streamResponse = Invoke-WebRequest `
        -Uri "$openWebUiBase/api/chat/completions" `
        -Method Post `
        -ContentType "application/json" `
        -Headers $headers `
        -WebSession $session `
        -TimeoutSec 120 `
        -Body (@{
            model = "agentbridge-agent"
            messages = @(
                @{ role = "user"; content = $Prompt }
            )
            stream = $true
            max_tokens = 500
            temperature = 0
        } | ConvertTo-Json -Depth 10)

    if ($streamResponse.StatusCode -ne 200) {
        throw "Open WebUI chat returned HTTP $($streamResponse.StatusCode)."
    }

    if (-not ($streamResponse.Headers["Content-Type"] -match "text/event-stream")) {
        throw "Open WebUI chat did not return a streaming response."
    }

    if (-not ($streamResponse.Content -match "chat\.completion\.chunk")) {
        throw "Open WebUI stream did not include OpenAI-compatible chunk objects."
    }

    if (-not ($streamResponse.Content -match "data: \[DONE\]")) {
        throw "Open WebUI stream did not include the final [DONE] marker."
    }

    $chunks = @(ConvertFrom-SseJson -Content $streamResponse.Content)
    $metadataChunk = $chunks |
        Where-Object { $_.agentbridge -and $_.agentbridge.conversation_id } |
        Select-Object -Last 1
    if (-not $metadataChunk) {
        throw "Open WebUI stream did not include AgentBridge metadata."
    }

    $conversationId = $metadataChunk.agentbridge.conversation_id
    $assistantText = ($chunks |
        ForEach-Object { $_.choices[0].delta.content } |
        Where-Object { $_ }) -join ""

    $streamResponse.Content -split "\r?\n" |
        Where-Object { $_ } |
        Select-Object -First 8

    Write-Section "AgentBridge Conversation Details"
    $conversationDetails = Invoke-RestMethod -Uri "$agentBridgeBase/v1/agentbridge/conversations/$conversationId"
    $conversationDetails | ConvertTo-Json -Depth 10

    if ($conversationDetails.agentbridge.conversation_id -ne $conversationId) {
        throw "Conversation details metadata did not match the streaming metadata."
    }

    Write-Section "Result"
    [pscustomobject]@{
        open_webui_url = $openWebUiBase
        agentbridge_url = "$agentBridgeBase/v1"
        upstream = if ($UseSimulator) { "simulator" } else { "configured" }
        conversation_id = $conversationId
        tool_call_count = $conversationDetails.agentbridge.tool_call_count
        assistant_text = $assistantText
    } | ConvertTo-Json -Depth 10
}
finally {
    Pop-Location
}
