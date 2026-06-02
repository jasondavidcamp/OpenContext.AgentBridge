[CmdletBinding()]
param(
    [string]$Endpoint = $env:AGENTBRIDGE_GATEWAY_ENDPOINT,
    [string]$Model = $(if ($env:AGENTBRIDGE_GATEWAY_MODEL) { $env:AGENTBRIDGE_GATEWAY_MODEL } else { "gemini-2.5-flash" }),
    [string]$ApiKey = $env:AGENTBRIDGE_GATEWAY_API_KEY,
    [int]$AgentBridgePort = 5330,
    [int]$OpenWebUiPort = 3100,
    [string]$Image = "ghcr.io/open-webui/open-webui:main",
    [string]$Prompt = "Inspect only README.md. Do not edit files. Return one sentence under 20 words.",
    [switch]$SkipBuild,
    [switch]$Recreate
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function ConvertTo-PlainText {
    param([securestring]$SecureString)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    $Endpoint = Read-Host "Gateway endpoint ending in /v1"
}

if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    throw "A gateway endpoint is required."
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    $Model = Read-Host "Gateway model id"
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    throw "A gateway model id is required."
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $secureKey = Read-Host "Gateway API key" -AsSecureString
    $ApiKey = ConvertTo-PlainText $secureKey
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "A gateway API key is required."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$oldEnvironment = @{
    AGENTBRIDGE_MODEL_PROVIDER = $env:AGENTBRIDGE_MODEL_PROVIDER
    AGENTBRIDGE_GATEWAY_ENDPOINT = $env:AGENTBRIDGE_GATEWAY_ENDPOINT
    AGENTBRIDGE_GATEWAY_MODEL = $env:AGENTBRIDGE_GATEWAY_MODEL
    AGENTBRIDGE_GATEWAY_API_KEY = $env:AGENTBRIDGE_GATEWAY_API_KEY
    AGENTBRIDGE_LOG_MODEL_TRAFFIC = $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC
}

Push-Location $repoRoot
try {
    Write-Section "Configure Gateway Provider"
    $env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
    $env:AGENTBRIDGE_GATEWAY_ENDPOINT = $Endpoint.TrimEnd("/")
    $env:AGENTBRIDGE_GATEWAY_MODEL = $Model
    $env:AGENTBRIDGE_GATEWAY_API_KEY = $ApiKey
    $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC = "false"

    Write-Host "Endpoint: $($env:AGENTBRIDGE_GATEWAY_ENDPOINT)"
    Write-Host "Model: $($env:AGENTBRIDGE_GATEWAY_MODEL)"
    Write-Host "API key: configured, not displayed"
    Write-Host "Open WebUI image: $Image"

    Write-Section "Open WebUI Gateway Smoke"
    $smokeArgs = @{
        AgentBridgePort = $AgentBridgePort
        OpenWebUiPort = $OpenWebUiPort
        Image = $Image
        UseExistingProviderConfig = $true
        Prompt = $Prompt
    }

    if ($SkipBuild) {
        $smokeArgs.SkipBuild = $true
    }

    if ($Recreate) {
        $smokeArgs.Recreate = $true
    }

    & (Join-Path $PSScriptRoot "Invoke-OpenWebUiSmoke.ps1") @smokeArgs

    Write-Section "Result"
    Write-Host "Open WebUI gateway smoke passed."
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
