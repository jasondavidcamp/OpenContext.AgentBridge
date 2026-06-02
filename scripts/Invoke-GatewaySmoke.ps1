[CmdletBinding()]
param(
    [string]$Endpoint = $env:AGENTBRIDGE_GATEWAY_ENDPOINT,
    [string]$Model = $(if ($env:AGENTBRIDGE_GATEWAY_MODEL) { $env:AGENTBRIDGE_GATEWAY_MODEL } else { "gemini-2.5-flash" }),
    [int]$ModelTimeoutSeconds = 300,
    [int]$MaxTokens = 1500,
    [int]$NoEditMaxIterations = 8,
    [int]$EditMaxIterations = 12,
    [switch]$SkipBuild,
    [switch]$ConnectivityOnly,
    [switch]$KeepChanges,
    [switch]$KeepLocalState,
    [string]$OutputDirectory,
    [switch]$ZipDiagnostics
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function ConvertTo-SafeFileName {
    param([string]$Value)

    $safe = $Value -replace '[^A-Za-z0-9._-]+', '-'
    $safe = $safe.Trim('-')

    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "step"
    }

    return $safe.ToLowerInvariant()
}

function Initialize-Diagnostics {
    param(
        [string]$Directory,
        [string]$Repository,
        [string]$Endpoint,
        [string]$Model,
        [string]$PowerShellPath
    )

    if ([string]::IsNullOrWhiteSpace($Directory)) {
        return
    }

    $resolvedDirectory = if ([IO.Path]::IsPathRooted($Directory)) {
        $Directory
    }
    else {
        Join-Path $Repository $Directory
    }

    $script:DiagnosticsDirectory = [IO.Path]::GetFullPath($resolvedDirectory)
    $script:DiagnosticsLogsDirectory = Join-Path $script:DiagnosticsDirectory "logs"
    New-Item -ItemType Directory -Force -Path $script:DiagnosticsLogsDirectory | Out-Null

    $script:Diagnostics = [ordered]@{
        schemaVersion = 1
        startedAt = (Get-Date).ToUniversalTime().ToString("o")
        completedAt = $null
        status = "running"
        failure = $null
        environment = [ordered]@{
            repository = "$Repository"
            endpoint = $Endpoint
            model = $Model
            modelTimeoutSeconds = $ModelTimeoutSeconds
            maxTokens = $MaxTokens
            connectivityOnly = [bool]$ConnectivityOnly
            keepChanges = [bool]$KeepChanges
            keepLocalState = [bool]$KeepLocalState
            powerShellHost = $PowerShellPath
            apiKey = "configured, not displayed"
        }
        steps = @()
    }

    $script:Diagnostics.environment |
        ConvertTo-Json -Depth 10 |
        Set-Content -Path (Join-Path $script:DiagnosticsDirectory "environment.json") -Encoding UTF8
}

function Add-DiagnosticsStep {
    param(
        [string]$Name,
        [string]$Command,
        [int]$ExitCode,
        [bool]$Succeeded,
        [double]$DurationSeconds,
        [string]$LogPath
    )

    if (-not $script:Diagnostics) {
        return
    }

    $relativeLogPath = if ([string]::IsNullOrWhiteSpace($LogPath)) {
        $null
    }
    else {
        [IO.Path]::GetRelativePath($script:DiagnosticsDirectory, $LogPath).Replace('\', '/')
    }

    $script:Diagnostics.steps += [ordered]@{
        name = $Name
        command = $Command
        exitCode = $ExitCode
        succeeded = $Succeeded
        durationSeconds = [Math]::Round($DurationSeconds, 3)
        logPath = $relativeLogPath
    }
}

function Write-DiagnosticsResult {
    param(
        [string]$Status,
        [string]$Failure
    )

    if (-not $script:Diagnostics) {
        return
    }

    $script:Diagnostics.completedAt = (Get-Date).ToUniversalTime().ToString("o")
    $script:Diagnostics.status = $Status
    $script:Diagnostics.failure = if ([string]::IsNullOrWhiteSpace($Failure)) { $null } else { $Failure }

    $resultPath = Join-Path $script:DiagnosticsDirectory "smoke-result.json"
    $script:Diagnostics |
        ConvertTo-Json -Depth 20 |
        Set-Content -Path $resultPath -Encoding UTF8

    Write-Host ""
    Write-Host "Diagnostics: $script:DiagnosticsDirectory"
    Write-Host "Result: $resultPath"

    if ($ZipDiagnostics) {
        $zipPath = "$script:DiagnosticsDirectory.zip"
        if (Test-Path $zipPath) {
            Remove-Item -Force $zipPath
        }

        Compress-Archive -Path (Join-Path $script:DiagnosticsDirectory "*") -DestinationPath $zipPath -Force
        Write-Host "Diagnostics zip: $zipPath"
    }
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

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList,

        [string]$StepName = $FilePath
    )

    Write-Host ""
    Write-Host "> $FilePath $($ArgumentList -join ' ')"
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $lines = & $FilePath @ArgumentList 2>&1 | ForEach-Object {
        $line = $_.ToString()
        Write-Host $line
        $line
    }
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    $logPath = $null
    if ($script:DiagnosticsLogsDirectory) {
        $index = ($script:Diagnostics.steps.Count + 1).ToString("000")
        $logPath = Join-Path $script:DiagnosticsLogsDirectory "$index-$(ConvertTo-SafeFileName $StepName).log"
        @(
            "> $FilePath $($ArgumentList -join ' ')"
            ""
            $lines
            ""
            "Exit code: $exitCode"
            "Duration: $($stopwatch.Elapsed)"
        ) | Set-Content -Path $logPath -Encoding UTF8
    }

    Add-DiagnosticsStep `
        -Name $StepName `
        -Command "$FilePath $($ArgumentList -join ' ')" `
        -ExitCode $exitCode `
        -Succeeded ($exitCode -eq 0) `
        -DurationSeconds $stopwatch.Elapsed.TotalSeconds `
        -LogPath $logPath

    if ($exitCode -ne 0) {
        throw "$FilePath failed with exit code $exitCode."
    }
}

function Reset-SmokeState {
    param(
        [string]$Path,
        [switch]$RemoveLocalState,
        [string]$Reason = "cleanup"
    )

    if (Test-Path $Path) {
        git checkout -- $Path | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "git checkout failed while resetting $Path during $Reason."
        }

        Write-Host "Reset sample file during ${Reason}: $Path"
    }

    if ($RemoveLocalState -and (Test-Path ".\.agentbridge")) {
        Remove-Item -Recurse -Force ".\.agentbridge" -ErrorAction SilentlyContinue
        Write-Host "Removed local AgentBridge state during ${Reason}: .agentbridge"
    }
}

function Assert-CleanSample {
    param([string]$Path)

    $status = git status --short -- $Path
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed while checking no-edit smoke result."
    }

    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw "No-edit smoke changed $Path. Status: $status"
    }

    Write-Host "No-edit smoke left sample file clean."
}

function Assert-NonEmptyDiff {
    param([string]$Path)

    $diff = git diff -- $Path
    if ($LASTEXITCODE -ne 0) {
        throw "git diff failed while checking edit smoke result."
    }

    if ([string]::IsNullOrWhiteSpace(($diff -join [Environment]::NewLine))) {
        throw "Edit smoke produced no diff for $Path."
    }

    Write-Host "Edit smoke produced a non-empty diff for $Path."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$powerShellSamplePath = "examples/powershell-sandbox/Get-Greeting.ps1"
$dotNetSamplePath = "examples/sandbox-project/SandboxApp/Program.cs"
$completed = $false
$failureMessage = $null
$script:Diagnostics = $null
$script:DiagnosticsDirectory = $null
$script:DiagnosticsLogsDirectory = $null

Push-Location $repoRoot
try {
    Reset-SmokeState -Path $powerShellSamplePath -RemoveLocalState:(-not $KeepLocalState) -Reason "preflight"
    Reset-SmokeState -Path $dotNetSamplePath -Reason "preflight"

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        $Endpoint = Read-Host "Gateway endpoint ending in /v1"
    }

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        throw "A gateway endpoint is required."
    }

    if ([string]::IsNullOrWhiteSpace($env:AGENTBRIDGE_GATEWAY_API_KEY)) {
        $secureKey = Read-Host "Paste Gateway API key for this session" -AsSecureString
        $env:AGENTBRIDGE_GATEWAY_API_KEY = ConvertTo-PlainText $secureKey
    }

    if ([string]::IsNullOrWhiteSpace($env:AGENTBRIDGE_GATEWAY_API_KEY)) {
        throw "A gateway API key is required."
    }

    $env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
    $env:AGENTBRIDGE_GATEWAY_ENDPOINT = $Endpoint.TrimEnd("/")
    $env:AGENTBRIDGE_GATEWAY_MODEL = $Model
    $env:AGENTBRIDGE_GATEWAY_TIMEOUT_SECONDS = "$ModelTimeoutSeconds"
    $env:AGENTBRIDGE_GATEWAY_MAX_TOKENS = "$MaxTokens"

    $shell = Get-PowerShellHost
    Initialize-Diagnostics `
        -Directory $OutputDirectory `
        -Repository $repoRoot `
        -Endpoint $env:AGENTBRIDGE_GATEWAY_ENDPOINT `
        -Model $env:AGENTBRIDGE_GATEWAY_MODEL `
        -PowerShellPath $shell.FilePath

    Write-Section "Environment"
    Write-Host "Repository: $repoRoot"
    Write-Host "Endpoint: $($env:AGENTBRIDGE_GATEWAY_ENDPOINT)"
    Write-Host "Model: $($env:AGENTBRIDGE_GATEWAY_MODEL)"
    Write-Host "Model timeout seconds: $($env:AGENTBRIDGE_GATEWAY_TIMEOUT_SECONDS)"
    Write-Host "Max tokens: $($env:AGENTBRIDGE_GATEWAY_MAX_TOKENS)"
    Write-Host "PowerShell host: $($shell.FilePath)"
    Write-Host "API key: configured, not displayed"

    if (-not $SkipBuild) {
        Write-Section "Build"
        Invoke-NativeCommand "dotnet" @("build", ".\OpenContext.AgentBridge.sln") -StepName "build"
    }

    Write-Section "Gateway Models"
    Invoke-NativeCommand "dotnet" @(
        "run",
        "--project",
        ".\src\OpenContext.AgentBridge.Cli",
        "--",
        "models",
        "list",
        ".",
        "--provider",
        "gateway"
    ) -StepName "gateway-models-list"

    Write-Section "Gateway Model Test"
    Invoke-NativeCommand "dotnet" @(
        "run",
        "--project",
        ".\src\OpenContext.AgentBridge.Cli",
        "--",
        "models",
        "test",
        ".",
        "--provider",
        "gateway",
        "--model",
        $env:AGENTBRIDGE_GATEWAY_MODEL
    ) -StepName "gateway-model-test"

    Write-Section "PowerShell Sandbox Baseline"
    Invoke-NativeCommand $shell.FilePath ($shell.Prefix + @("-File", ".\$powerShellSamplePath", "-Name", "AgentBridge")) -StepName "powershell-sandbox-baseline"

    Write-Section ".NET Sandbox Baseline"
    Invoke-NativeCommand "dotnet" @("run", "--project", ".\examples\sandbox-project\SandboxApp", "--", "AgentBridge") -StepName "dotnet-sandbox-baseline"

    if (-not $ConnectivityOnly) {
        Write-Section "No-Edit Agent Smoke"
        Invoke-NativeCommand "dotnet" @(
            "run",
            "--project",
            ".\src\OpenContext.AgentBridge.Cli",
            "--",
            "ask",
            ".",
            "--new",
            "--skill",
            "powershell",
            "--require-tool-calls",
            "2",
            "--max-iterations",
            "$NoEditMaxIterations",
            "Only inspect examples/powershell-sandbox. Read the README.md and Get-Greeting.ps1, then return a final summary in no more than three plain sentences. Do not edit files."
        ) -StepName "agent-no-edit-smoke"

        Write-Section "Status After No-Edit Smoke"
        Invoke-NativeCommand "git" @("status", "--short") -StepName "status-after-no-edit"
        Assert-CleanSample -Path $powerShellSamplePath

        Write-Section "Edit Agent Smoke"
        Invoke-NativeCommand "dotnet" @(
            "run",
            "--project",
            ".\src\OpenContext.AgentBridge.Cli",
            "--",
            "ask",
            ".",
            "--new",
            "--skill",
            "powershell",
            "--require-tool-calls",
            "4",
            "--max-iterations",
            "$EditMaxIterations",
            "Only work in examples/powershell-sandbox. Improve the PowerShell script help text in Get-Greeting.ps1 without changing runtime behavior. Preserve the Name parameter and greeting output. Validate by running: pwsh -NoProfile -File .\examples\powershell-sandbox\Get-Greeting.ps1 -Name AgentBridge. Then show the git diff and return a final summary in no more than three plain sentences."
        ) -StepName "agent-powershell-edit-smoke"

        Write-Section "Final Validation"
        Invoke-NativeCommand $shell.FilePath ($shell.Prefix + @("-File", ".\$powerShellSamplePath", "-Name", "AgentBridge")) -StepName "powershell-final-validation"
        Assert-NonEmptyDiff -Path $powerShellSamplePath

        Write-Section "Final Status"
        Invoke-NativeCommand "git" @("status", "--short") -StepName "powershell-final-status"

        Write-Section "PowerShell Final Diff"
        Invoke-NativeCommand "git" @("diff", "--", $powerShellSamplePath) -StepName "powershell-final-diff"

        Write-Section "Symbol-Aware Agent Smoke"
        Invoke-NativeCommand "dotnet" @(
            "run",
            "--project",
            ".\src\OpenContext.AgentBridge.Cli",
            "--",
            "ask",
            ".",
            "--new",
            "--require-tool-calls",
            "4",
            "--max-iterations",
            "$EditMaxIterations",
            "Use the workspace map to find the C# greeting implementation in the sandbox project. Do not ask for or assume a path from the user. Modify it so the generated greeting includes the phrase 'from AgentBridge' while preserving the supplied name. Validate by running: dotnet run --project .\examples\sandbox-project\SandboxApp -- AgentBridge. Then show the git diff and return a final summary in no more than three plain sentences."
        ) -StepName "agent-symbol-aware-edit-smoke"

        Write-Section ".NET Final Validation"
        Invoke-NativeCommand "dotnet" @("run", "--project", ".\examples\sandbox-project\SandboxApp", "--", "AgentBridge") -StepName "dotnet-final-validation"
        Assert-NonEmptyDiff -Path $dotNetSamplePath

        Write-Section ".NET Final Status"
        Invoke-NativeCommand "git" @("status", "--short") -StepName "dotnet-final-status"

        Write-Section ".NET Final Diff"
        Invoke-NativeCommand "git" @("diff", "--", $dotNetSamplePath) -StepName "dotnet-final-diff"
    }

    $completed = $true
}
catch {
    $failureMessage = $_.Exception.Message
    throw
}
finally {
    Write-DiagnosticsResult `
        -Status $(if ($completed) { "passed" } else { "failed" }) `
        -Failure $failureMessage

    if ($completed) {
        if (-not $KeepChanges) {
            Write-Host ""
            Reset-SmokeState -Path $powerShellSamplePath -Reason "completion"
            Reset-SmokeState -Path $dotNetSamplePath -Reason "completion"
        }

        if (-not $KeepLocalState -and (Test-Path ".\.agentbridge")) {
            $localStatePath = Join-Path $repoRoot ".agentbridge"
            if ($script:DiagnosticsDirectory -and (Test-PathInside -ParentPath $localStatePath -ChildPath $script:DiagnosticsDirectory)) {
                Write-Host "Preserved local AgentBridge state because diagnostics output is inside .agentbridge"
            }
            else {
                Remove-Item -Recurse -Force ".\.agentbridge" -ErrorAction SilentlyContinue
                Write-Host "Removed local AgentBridge state: .agentbridge"
            }
        }
    }

    Pop-Location
}
