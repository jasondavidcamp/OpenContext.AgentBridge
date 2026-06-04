[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Workspace = (Get-Location).Path,
    [string]$Image = "opencontext-agentbridge-cline:latest",
    [string]$Network,
    [string]$Model,
    [string]$OpenAiApiBase,
    [string]$ApiKey = "agentbridge-local",
    [string]$Message,
    [int]$TimeoutSeconds = 420,
    [ValidateSet("none", "low", "medium", "high", "xhigh")]
    [string]$Thinking = "low",
    [switch]$NoJson,
    [switch]$Pull,
    [switch]$ShowVersion
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

if ($Pull) {
    docker pull $Image
}

if ($ShowVersion) {
    docker run --rm --entrypoint cline $Image --version
    if ($LASTEXITCODE -ne 0) {
        throw "Cline Docker version check failed with exit code $LASTEXITCODE."
    }

    return
}

$resolvedWorkspace = Resolve-Path -LiteralPath $Workspace
if (-not (Test-Path -LiteralPath $resolvedWorkspace -PathType Container)) {
    throw "Workspace is not a directory: $Workspace"
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    $Model = Get-FirstEnvironmentValue @(
        "CLINE_MODEL",
        "AGENTBRIDGE_OPENAI_MODEL",
        "AGENTBRIDGE_GATEWAY_MODEL"
    )
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    $Model = "gemini-2.5-flash"
}

if ([string]::IsNullOrWhiteSpace($OpenAiApiBase)) {
    $OpenAiApiBase = Get-FirstEnvironmentValue @(
        "CLINE_OPENAI_API_BASE",
        "AGENTBRIDGE_OPENAI_ENDPOINT",
        "AGENTBRIDGE_GATEWAY_ENDPOINT"
    )
}

if ([string]::IsNullOrWhiteSpace($OpenAiApiBase)) {
    throw "No OpenAI-compatible base URL was provided."
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = Get-FirstEnvironmentValue @(
        "CLINE_OPENAI_API_KEY",
        "AGENTBRIDGE_SERVER_API_KEY",
        "AGENTBRIDGE_OPENAI_API_KEY",
        "AGENTBRIDGE_GATEWAY_API_KEY"
    )
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = "agentbridge-local"
}

if ([string]::IsNullOrWhiteSpace($Message)) {
    throw "A Cline message is required for non-interactive Docker runs."
}

$oldEnvironment = @{
    CLINE_OPENAI_API_BASE = $env:CLINE_OPENAI_API_BASE
    CLINE_OPENAI_API_KEY = $env:CLINE_OPENAI_API_KEY
    CLINE_MODEL = $env:CLINE_MODEL
    CLINE_MESSAGE = $env:CLINE_MESSAGE
    CLINE_TIMEOUT_SECONDS = $env:CLINE_TIMEOUT_SECONDS
    CLINE_THINKING = $env:CLINE_THINKING
    CLINE_JSON_FLAG = $env:CLINE_JSON_FLAG
}

try {
    $env:CLINE_OPENAI_API_BASE = $OpenAiApiBase.TrimEnd("/")
    $env:CLINE_OPENAI_API_KEY = $ApiKey
    $env:CLINE_MODEL = $Model
    $env:CLINE_MESSAGE = $Message
    $env:CLINE_TIMEOUT_SECONDS = $TimeoutSeconds.ToString()
    $env:CLINE_THINKING = $Thinking
    $env:CLINE_JSON_FLAG = if ($NoJson) { "" } else { "--json" }

    $script = @'
set -euo pipefail
DATA_DIR=/tmp/agentbridge-cline-data
cline auth \
  -p openai \
  -k "$CLINE_OPENAI_API_KEY" \
  -m "$CLINE_MODEL" \
  -b "$CLINE_OPENAI_API_BASE" \
  --data-dir "$DATA_DIR" >/tmp/agentbridge-cline-auth.log

exec cline \
  --data-dir "$DATA_DIR" \
  --cwd /workspace \
  --provider openai \
  --key "$CLINE_OPENAI_API_KEY" \
  --model "$CLINE_MODEL" \
  --timeout "$CLINE_TIMEOUT_SECONDS" \
  --auto-approve true \
  --thinking "$CLINE_THINKING" \
  $CLINE_JSON_FLAG \
  "$CLINE_MESSAGE"
'@

    $dockerArgs = @(
        "run",
        "--rm"
    )
    if (-not [string]::IsNullOrWhiteSpace($Network)) {
        $dockerArgs += @("--network", $Network)
    }

    $dockerArgs += @(
        "-e", "CLINE_NO_OPEN_BROWSER=1",
        "-e", "CLINE_OPENAI_API_BASE",
        "-e", "CLINE_OPENAI_API_KEY",
        "-e", "CLINE_MODEL",
        "-e", "CLINE_MESSAGE",
        "-e", "CLINE_TIMEOUT_SECONDS",
        "-e", "CLINE_THINKING",
        "-e", "CLINE_JSON_FLAG",
        "-e", "HOME=/tmp/agentbridge-home",
        "-e", "XDG_DATA_HOME=/tmp/agentbridge-xdg",
        "-v", "$($resolvedWorkspace.Path):/workspace",
        "-w", "/workspace",
        "--entrypoint", "bash",
        $Image,
        "-lc", $script
    )

    docker @dockerArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Cline Docker run failed with exit code $LASTEXITCODE."
    }
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
}
