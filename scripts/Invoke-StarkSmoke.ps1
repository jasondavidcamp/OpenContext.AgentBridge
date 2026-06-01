[CmdletBinding()]
param(
    [string]$Endpoint = $env:AGENTBRIDGE_STARK_ENDPOINT,
    [string]$Model = $(if ($env:AGENTBRIDGE_STARK_MODEL) { $env:AGENTBRIDGE_STARK_MODEL } else { "gemini-2.5-flash" }),
    [int]$ModelTimeoutSeconds = 300,
    [int]$MaxTokens = 1500,
    [int]$NoEditMaxIterations = 8,
    [int]$EditMaxIterations = 12,
    [switch]$SkipBuild,
    [switch]$ConnectivityOnly,
    [switch]$KeepChanges,
    [switch]$KeepLocalState
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
        [string[]]$ArgumentList
    )

    Write-Host ""
    Write-Host "> $FilePath $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
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
$samplePath = "examples/powershell-sandbox/Get-Greeting.ps1"
$completed = $false

Push-Location $repoRoot
try {
    Reset-SmokeState -Path $samplePath -RemoveLocalState:(-not $KeepLocalState) -Reason "preflight"

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        $Endpoint = Read-Host "STARK endpoint ending in /v1"
    }

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        throw "A STARK endpoint is required."
    }

    if ([string]::IsNullOrWhiteSpace($env:AGENTBRIDGE_STARK_API_KEY)) {
        $secureKey = Read-Host "Paste STARK API key for this session" -AsSecureString
        $env:AGENTBRIDGE_STARK_API_KEY = ConvertTo-PlainText $secureKey
    }

    if ([string]::IsNullOrWhiteSpace($env:AGENTBRIDGE_STARK_API_KEY)) {
        throw "A STARK API key is required."
    }

    $env:AGENTBRIDGE_MODEL_PROVIDER = "stark"
    $env:AGENTBRIDGE_STARK_ENDPOINT = $Endpoint.TrimEnd("/")
    $env:AGENTBRIDGE_STARK_MODEL = $Model
    $env:AGENTBRIDGE_STARK_TIMEOUT_SECONDS = "$ModelTimeoutSeconds"
    $env:AGENTBRIDGE_STARK_MAX_TOKENS = "$MaxTokens"

    $shell = Get-PowerShellHost

    Write-Section "Environment"
    Write-Host "Repository: $repoRoot"
    Write-Host "Endpoint: $($env:AGENTBRIDGE_STARK_ENDPOINT)"
    Write-Host "Model: $($env:AGENTBRIDGE_STARK_MODEL)"
    Write-Host "Model timeout seconds: $($env:AGENTBRIDGE_STARK_TIMEOUT_SECONDS)"
    Write-Host "Max tokens: $($env:AGENTBRIDGE_STARK_MAX_TOKENS)"
    Write-Host "PowerShell host: $($shell.FilePath)"
    Write-Host "API key: configured, not displayed"

    if (-not $SkipBuild) {
        Write-Section "Build"
        Invoke-NativeCommand "dotnet" @("build", ".\OpenContext.AgentBridge.sln")
    }

    Write-Section "STARK Models"
    Invoke-NativeCommand "dotnet" @(
        "run",
        "--project",
        ".\src\OpenContext.AgentBridge.Cli",
        "--",
        "models",
        "list",
        ".",
        "--provider",
        "stark"
    )

    Write-Section "STARK Model Test"
    Invoke-NativeCommand "dotnet" @(
        "run",
        "--project",
        ".\src\OpenContext.AgentBridge.Cli",
        "--",
        "models",
        "test",
        ".",
        "--provider",
        "stark",
        "--model",
        $env:AGENTBRIDGE_STARK_MODEL
    )

    Write-Section "PowerShell Sandbox Baseline"
    Invoke-NativeCommand $shell.FilePath ($shell.Prefix + @("-File", ".\$samplePath", "-Name", "AgentBridge"))

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
        )

        Write-Section "Status After No-Edit Smoke"
        Invoke-NativeCommand "git" @("status", "--short")
        Assert-CleanSample -Path $samplePath

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
        )

        Write-Section "Final Validation"
        Invoke-NativeCommand $shell.FilePath ($shell.Prefix + @("-File", ".\$samplePath", "-Name", "AgentBridge"))
        Assert-NonEmptyDiff -Path $samplePath

        Write-Section "Final Status"
        Invoke-NativeCommand "git" @("status", "--short")

        Write-Section "Final Diff"
        Invoke-NativeCommand "git" @("diff", "--", $samplePath)
    }

    $completed = $true
}
finally {
    if ($completed) {
        if (-not $KeepChanges) {
            Write-Host ""
            Reset-SmokeState -Path $samplePath -Reason "completion"
        }

        if (-not $KeepLocalState -and (Test-Path ".\.agentbridge")) {
            Remove-Item -Recurse -Force ".\.agentbridge" -ErrorAction SilentlyContinue
            Write-Host "Removed local AgentBridge state: .agentbridge"
        }
    }

    Pop-Location
}
