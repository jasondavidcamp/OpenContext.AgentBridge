[CmdletBinding()]
param(
    [int]$Port = 5324,
    [int]$SimulatorPort = 5325,
    [switch]$SkipBuild,
    [switch]$KeepWorkspace
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

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Get-PowerShellHost {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return [pscustomobject]@{
            FilePath = $pwsh.Source
            Prefix = @("-NoProfile")
        }
    }

    $windowsPowerShell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($windowsPowerShell) {
        return [pscustomobject]@{
            FilePath = $windowsPowerShell.Source
            Prefix = @("-NoProfile", "-ExecutionPolicy", "Bypass")
        }
    }

    throw "Neither pwsh nor Windows PowerShell was found on PATH."
}

function Test-PathInside {
    param(
        [string]$ParentPath,
        [string]$ChildPath
    )

    if ([string]::IsNullOrWhiteSpace($ParentPath) -or [string]::IsNullOrWhiteSpace($ChildPath)) {
        return $false
    }

    $parent = [IO.Path]::GetFullPath($ParentPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $child = [IO.Path]::GetFullPath($ChildPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

    return [string]::Equals($parent, $child, [StringComparison]::OrdinalIgnoreCase) `
        -or $child.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) `
        -or $child.StartsWith($parent + [IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$serverBase = "http://127.0.0.1:$Port"
$simulatorBase = "http://127.0.0.1:$SimulatorPort"
$simulatorEndpoint = "$simulatorBase/v1"
$simulatorApiKey = "local-simulator-key"
$logRoot = Join-Path $repoRoot ".agentbridge\server-edit-canary-logs"
$scratchRoot = Join-Path $repoRoot ".agentbridge\edit-canary-workspaces"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$workspaceRoot = Join-Path $scratchRoot "workspace-$stamp"
$serverOut = Join-Path $logRoot "server-edit-canary-$stamp.out.log"
$serverErr = Join-Path $logRoot "server-edit-canary-$stamp.err.log"
$simulatorOut = Join-Path $logRoot "simulator-edit-canary-$stamp.out.log"
$simulatorErr = Join-Path $logRoot "simulator-edit-canary-$stamp.err.log"
$samplePath = "examples/powershell-sandbox/Get-Greeting.ps1"
$sampleSource = Join-Path $repoRoot "examples\powershell-sandbox"
$sampleTarget = Join-Path $workspaceRoot "examples\powershell-sandbox"
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
    New-Item -ItemType Directory -Force -Path $logRoot, $scratchRoot, $sampleTarget | Out-Null

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build .\OpenContext.AgentBridge.sln
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    Write-Section "Prepare Scratch Workspace"
    Copy-Item -LiteralPath (Join-Path $sampleSource "README.md") -Destination $sampleTarget -Force
    Copy-Item -LiteralPath (Join-Path $sampleSource "Get-Greeting.ps1") -Destination $sampleTarget -Force
    Invoke-Native "git" @("init") $workspaceRoot
    Invoke-Native "git" @("config", "user.name", "AgentBridge Canary") $workspaceRoot
    Invoke-Native "git" @("config", "user.email", "agentbridge-canary@example.invalid") $workspaceRoot
    Invoke-Native "git" @("config", "core.autocrlf", "false") $workspaceRoot
    Invoke-Native "git" @("add", "examples") $workspaceRoot
    Invoke-Native "git" @("commit", "-m", "Baseline") $workspaceRoot

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
            $serverBase
        ) `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr
    Wait-ForModels -BaseUrl $serverBase

    Write-Section "Agent Edit Canary"
    $agentResponse = Invoke-RestMethod `
        -Uri "$serverBase/v1/chat/completions" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{
            model = "agentbridge-agent"
            messages = @(
                @{ role = "user"; content = "Only work in examples/powershell-sandbox. Improve the PowerShell script help text in Get-Greeting.ps1 without changing runtime behavior." }
            )
            stream = $false
        } | ConvertTo-Json -Depth 10)

    $agentResponse | ConvertTo-Json -Depth 10
    if (-not $agentResponse.agentbridge.conversation_id) {
        throw "Agent edit canary did not include AgentBridge metadata."
    }

    if ($agentResponse.agentbridge.successful_tool_call_count -lt 4) {
        throw "Agent edit canary did not complete the expected edit tool chain."
    }

    Write-Section "Conversation Details"
    $conversationId = [string]$agentResponse.agentbridge.conversation_id
    $conversationDetails = Invoke-RestMethod -Uri "$serverBase/v1/agentbridge/conversations/$conversationId"
    $conversationDetails | ConvertTo-Json -Depth 10

    $expectedTools = @("read_file", "replace_text", "run_command", "git_diff")
    foreach ($toolName in $expectedTools) {
        $toolCall = @($conversationDetails.tool_calls) |
            Where-Object { $_.tool_name -eq $toolName -and $_.is_success -eq $true } |
            Select-Object -First 1
        if (-not $toolCall) {
            throw "Agent edit canary did not record a successful $toolName call."
        }
    }

    Write-Section "Validate Edited Script"
    $scriptContent = Get-Content -LiteralPath (Join-Path $workspaceRoot $samplePath) -Raw
    if (-not $scriptContent.Contains(".SYNOPSIS")) {
        throw "Edited script does not contain the expected help text marker."
    }

    $shell = Get-PowerShellHost
    Push-Location $workspaceRoot
    try {
        $output = & $shell.FilePath @($shell.Prefix + @("-File", ".\$samplePath", "-Name", "AgentBridge")) 2>&1
        if ($LASTEXITCODE -ne 0) {
            $output
            throw "Edited script validation command failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    $outputText = ($output | Out-String).Trim()
    Write-Host $outputText
    if ($outputText -ne "Hello, AgentBridge!") {
        throw "Edited script changed runtime behavior. Expected: Hello, AgentBridge!"
    }

    Write-Section "Diff"
    $diff = git -C $workspaceRoot diff -- $samplePath
    if ($LASTEXITCODE -ne 0) {
        throw "git diff failed for edit canary workspace."
    }

    $diffText = ($diff | Out-String).Trim()
    Write-Host $diffText
    if ([string]::IsNullOrWhiteSpace($diffText) -or -not $diffText.Contains(".SYNOPSIS")) {
        throw "Edit canary did not produce the expected non-empty diff."
    }

    Write-Section "Logs"
    Write-Host "Scratch workspace: $workspaceRoot"
    Write-Host "Server stdout: $serverOut"
    Write-Host "Server stderr: $serverErr"
    Write-Host "Simulator stdout: $simulatorOut"
    Write-Host "Simulator stderr: $simulatorErr"

    Write-Section "Result"
    Write-Host "Local server edit canary passed."
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
        if ($null -eq $oldEnvironment[$name]) {
            Remove-Item -Path "env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -Path "env:$name" -Value $oldEnvironment[$name]
        }
    }

    if (-not $KeepWorkspace -and (Test-Path -LiteralPath $workspaceRoot)) {
        if (-not (Test-PathInside -ParentPath $scratchRoot -ChildPath $workspaceRoot)) {
            throw "Refusing to remove scratch workspace outside scratch root: $workspaceRoot"
        }

        Remove-Item -LiteralPath $workspaceRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Pop-Location
}
