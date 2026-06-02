param(
    [string]$Workspace = (Get-Location).Path,
    [string]$Image = "paulgauthier/aider",
    [string]$Model,
    [string]$OpenAiApiBase,
    [string]$ApiKey,
    [string]$ApiKeyEnvironmentVariable,
    [string]$Message,
    [string[]]$File = @(),
    [string[]]$Read = @(),
    [string]$TestCommand,
    [switch]$AutoTest,
    [switch]$AutoCommits,
    [switch]$DryRun,
    [switch]$Pull,
    [switch]$ShowVersion,
    [switch]$NoTty,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AiderArgs = @()
)

$ErrorActionPreference = "Stop"

function Get-FirstEnvironmentValue {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name, "Process")
        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = [Environment]::GetEnvironmentVariable($name, "User")
        }
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $null
}

function Resolve-DefaultModel {
    if (-not [string]::IsNullOrWhiteSpace($Model)) {
        return $Model
    }

    $configuredModel = Get-FirstEnvironmentValue @(
        "AIDER_MODEL",
        "AGENTBRIDGE_OPENAI_MODEL",
        "AGENTBRIDGE_STARK_MODEL"
    )
    if (-not [string]::IsNullOrWhiteSpace($configuredModel)) {
        return $configuredModel
    }

    return "gemini-2.5-flash"
}

function Resolve-DefaultEndpoint {
    if (-not [string]::IsNullOrWhiteSpace($OpenAiApiBase)) {
        return $OpenAiApiBase
    }

    $configuredEndpoint = Get-FirstEnvironmentValue @(
        "AIDER_OPENAI_API_BASE",
        "AGENTBRIDGE_OPENAI_ENDPOINT",
        "AGENTBRIDGE_STARK_ENDPOINT"
    )
    if (-not [string]::IsNullOrWhiteSpace($configuredEndpoint)) {
        return $configuredEndpoint
    }

    return "https://generativelanguage.googleapis.com/v1beta/openai/"
}

function Resolve-ApiKey {
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
        return $ApiKey
    }

    if (-not [string]::IsNullOrWhiteSpace($ApiKeyEnvironmentVariable)) {
        return Get-FirstEnvironmentValue @($ApiKeyEnvironmentVariable)
    }

    return Get-FirstEnvironmentValue @(
        "AIDER_OPENAI_API_KEY",
        "AGENTBRIDGE_OPENAI_API_KEY",
        "AGENTBRIDGE_STARK_API_KEY",
        "AGENTBRIDGE_GEMINI_API_KEY"
    )
}

$resolvedWorkspace = Resolve-Path -LiteralPath $Workspace
if (-not (Test-Path -LiteralPath $resolvedWorkspace -PathType Container)) {
    throw "Workspace is not a directory: $Workspace"
}

if ($Pull) {
    docker pull $Image
}

if ($ShowVersion) {
    docker run --rm $Image --version
    exit $LASTEXITCODE
}

$resolvedModel = Resolve-DefaultModel
if ($resolvedModel -notmatch "^[^/]+/") {
    $resolvedModel = "openai/$resolvedModel"
}

$resolvedEndpoint = Resolve-DefaultEndpoint
$resolvedApiKey = Resolve-ApiKey
if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) {
    throw "No API key found. Set AIDER_OPENAI_API_KEY, AGENTBRIDGE_OPENAI_API_KEY, AGENTBRIDGE_STARK_API_KEY, AGENTBRIDGE_GEMINI_API_KEY, or pass -ApiKeyEnvironmentVariable."
}

$oldAiderKey = $env:AIDER_OPENAI_API_KEY
$oldAiderBase = $env:AIDER_OPENAI_API_BASE
try {
    $env:AIDER_OPENAI_API_KEY = $resolvedApiKey
    $env:AIDER_OPENAI_API_BASE = $resolvedEndpoint

    $dockerArgs = @("run", "--rm")
    if ([string]::IsNullOrWhiteSpace($Message) -and -not $NoTty) {
        $dockerArgs += "-it"
    }

    $dockerArgs += @(
        "-e", "AIDER_OPENAI_API_KEY",
        "-e", "AIDER_OPENAI_API_BASE",
        "-v", "$($resolvedWorkspace.Path):/app",
        "-w", "/app",
        $Image
    )

    $aiderCommand = @(
        "--model", $resolvedModel,
        "--no-analytics",
        "--no-check-update",
        "--no-show-model-warnings",
        "--no-check-model-accepts-settings",
        "--no-gitignore",
        "--timeout", "300"
    )

    if (-not $AutoCommits) {
        $aiderCommand += "--no-auto-commits"
    }
    if ($DryRun) {
        $aiderCommand += "--dry-run"
    }
    if ($AutoTest) {
        $aiderCommand += "--auto-test"
    }
    if (-not [string]::IsNullOrWhiteSpace($TestCommand)) {
        $aiderCommand += @("--test-cmd", $TestCommand)
    }

    foreach ($path in $File) {
        $aiderCommand += @("--file", $path)
    }
    foreach ($path in $Read) {
        $aiderCommand += @("--read", $path)
    }
    if (-not [string]::IsNullOrWhiteSpace($Message)) {
        $aiderCommand += @("--message", $Message)
    }
    if ($AiderArgs.Count -gt 0) {
        $aiderCommand += $AiderArgs
    }

    $allArgs = @()
    $allArgs += $dockerArgs
    $allArgs += $aiderCommand

    docker @allArgs
    exit $LASTEXITCODE
}
finally {
    $env:AIDER_OPENAI_API_KEY = $oldAiderKey
    $env:AIDER_OPENAI_API_BASE = $oldAiderBase
}
