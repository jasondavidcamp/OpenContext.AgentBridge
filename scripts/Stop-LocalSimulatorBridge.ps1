[CmdletBinding()]
param(
    [switch]$All
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)

    Write-Output ""
    Write-Output "== $Title =="
}

function Stop-BridgeProcess {
    param(
        [int]$ProcessId,
        [string[]]$ExpectedProcessNames
    )

    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
    if (-not $processInfo) {
        Write-Output "Process $ProcessId is not running."
        return
    }

    if (-not (Test-IsBridgeProcess -ProcessInfo $processInfo -ExpectedProcessNames $ExpectedProcessNames)) {
        Write-Output "Skipping process $ProcessId because it is $($processInfo.Name), not an AgentBridge local bridge process."
        return
    }

    Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue |
        Where-Object { Test-IsBridgeProcess -ProcessInfo $_ -ExpectedProcessNames $ExpectedProcessNames } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            Write-Output "Stopped $($_.Name) ($($_.ProcessId))."
        }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    try {
        $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($process) {
            [void]$process.WaitForExit(5000)
        }
    }
    catch {
    }

    Write-Output "Stopped $($processInfo.Name) ($ProcessId)."
}

function Test-IsBridgeProcess {
    param(
        [object]$ProcessInfo,
        [string[]]$ExpectedProcessNames
    )

    $processName = [System.IO.Path]::GetFileNameWithoutExtension($ProcessInfo.Name)
    if ($ExpectedProcessNames -contains $processName) {
        return $true
    }

    if ($processName -ne "dotnet") {
        return $false
    }

    foreach ($expected in $ExpectedProcessNames) {
        if ($ProcessInfo.CommandLine -and $ProcessInfo.CommandLine.IndexOf($expected, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Stop-PidFileProcess {
    param(
        [string]$Path,
        [string[]]$ExpectedProcessNames
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Output "No pid file: $Path"
        return
    }

    $processIdText = Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue | Select-Object -First 1
    $processId = 0
    if (-not $processIdText -or -not [int]::TryParse([string]$processIdText, [ref]$processId)) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        Write-Output "Removed invalid pid file: $Path"
        return
    }

    Stop-BridgeProcess -ProcessId $processId -ExpectedProcessNames $ExpectedProcessNames
    Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$logRoot = Join-Path $repoRoot ".agentbridge\local-simulator-bridge"
$serverPidPath = Join-Path $logRoot "agentbridge-server.pid"
$simulatorPidPath = Join-Path $logRoot "simulator.pid"
$expectedNames = @(
    "OpenContext.AgentBridge.Server",
    "OpenContext.AgentBridge.SimulatedGateway"
)

Write-Section "Stop Local Simulator Bridge"
Stop-PidFileProcess -Path $serverPidPath -ExpectedProcessNames $expectedNames
Stop-PidFileProcess -Path $simulatorPidPath -ExpectedProcessNames $expectedNames

if ($All) {
    Write-Section "Stop Any Remaining Matching Processes"
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { Test-IsBridgeProcess -ProcessInfo $_ -ExpectedProcessNames $expectedNames } |
        Sort-Object ParentProcessId -Descending |
        ForEach-Object {
            Stop-BridgeProcess -ProcessId $_.ProcessId -ExpectedProcessNames $expectedNames
        }
}

Write-Section "Result"
Write-Output "Local simulator bridge stopped."
