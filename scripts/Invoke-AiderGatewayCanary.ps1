[CmdletBinding()]
param(
    [string]$Endpoint = $env:AGENTBRIDGE_GATEWAY_ENDPOINT,
    [string]$Model = $(if ($env:AGENTBRIDGE_GATEWAY_MODEL) { $env:AGENTBRIDGE_GATEWAY_MODEL } else { "gemini-2.5-flash" }),
    [string]$ApiKey = $env:AGENTBRIDGE_GATEWAY_API_KEY,
    [int]$AgentBridgePort = 5360,
    [string]$Image = "opencontext-agentbridge-aider-dotnet:latest",
    [switch]$SkipBuild,
    [switch]$KeepWorkspace
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

function Wait-ForDocker {
    for ($attempt = 1; $attempt -le 90; $attempt++) {
        try {
            docker version --format "{{.Server.Version}}" 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 2
    }

    throw "Docker daemon is not available. Start Docker Desktop and retry."
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

function Set-LfTextFile {
    param([string]$Path)

    $content = Get-Content -LiteralPath $Path -Raw
    $content = $content.Replace("`r`n", "`n")
    [IO.File]::WriteAllText(
        $Path,
        $content,
        [Text.UTF8Encoding]::new($false))
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
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$scratchRoot = Join-Path $repoRoot ".agentbridge\aider-canary-workspaces"
$workspaceRoot = Join-Path $scratchRoot "workspace-$stamp"
$sampleRoot = Join-Path $workspaceRoot "examples\sandbox-project"
$sampleAppRoot = Join-Path $sampleRoot "SandboxApp"
$logRoot = Join-Path $repoRoot ".agentbridge\aider-canary-logs"
$serverOut = Join-Path $logRoot "server-$stamp.out.log"
$serverErr = Join-Path $logRoot "server-$stamp.err.log"
$serverBase = "http://127.0.0.1:$AgentBridgePort"
$containerBase = "http://host.docker.internal:$AgentBridgePort/v1"
$programPath = "examples/sandbox-project/SandboxApp/Program.cs"
$server = $null
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
    New-Item -ItemType Directory -Force -Path $scratchRoot, $workspaceRoot, $sampleRoot, $sampleAppRoot, $logRoot | Out-Null

    Write-Section "Preflight"
    Wait-ForDocker
    docker image inspect $Image 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker image not found: $Image. Build docker\aider-dotnet.Dockerfile or pass -Image with an available Aider image."
    }

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build .\OpenContext.AgentBridge.sln
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    Write-Section "Prepare Scratch Workspace"
    Copy-Item -LiteralPath (Join-Path $repoRoot "examples\sandbox-project\README.md") -Destination $sampleRoot -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "examples\sandbox-project\SandboxApp\Program.cs") -Destination $sampleAppRoot -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "examples\sandbox-project\SandboxApp\SandboxApp.csproj") -Destination $sampleAppRoot -Force
    Set-LfTextFile -Path (Join-Path $sampleRoot "README.md")
    Set-LfTextFile -Path (Join-Path $sampleAppRoot "Program.cs")
    Set-LfTextFile -Path (Join-Path $sampleAppRoot "SandboxApp.csproj")
    git -C $workspaceRoot init | Out-Null
    git -C $workspaceRoot config user.name "AgentBridge Aider Canary"
    git -C $workspaceRoot config user.email "agentbridge-aider-canary@example.invalid"
    git -C $workspaceRoot config core.autocrlf false
    git -C $workspaceRoot add examples
    git -C $workspaceRoot commit -m Baseline | Out-Null

    Write-Section "Start AgentBridge Raw Proxy"
    $env:AGENTBRIDGE_SERVER_WORKSPACE = $workspaceRoot
    $env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
    $env:AGENTBRIDGE_GATEWAY_ENDPOINT = $Endpoint.TrimEnd("/")
    $env:AGENTBRIDGE_GATEWAY_MODEL = $Model
    $env:AGENTBRIDGE_GATEWAY_API_KEY = $ApiKey
    $env:AGENTBRIDGE_LOG_MODEL_TRAFFIC = "false"

    $server = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--no-build",
            "--project",
            ".\src\OpenContext.AgentBridge.Server",
            "--urls",
            "http://0.0.0.0:$AgentBridgePort"
        ) `
        -WorkingDirectory $repoRoot `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr
    Wait-ForModels -BaseUrl $serverBase

    Write-Section "Aider Docker Edit Canary"
    & (Join-Path $PSScriptRoot "Start-AiderDocker.ps1") `
        -Image $Image `
        -Workspace $workspaceRoot `
        -Model $Model `
        -OpenAiApiBase $containerBase `
        -ApiKey "agentbridge-local" `
        -NoTty `
        -File @($programPath) `
        -Read @("examples/sandbox-project/README.md", "examples/sandbox-project/SandboxApp/SandboxApp.csproj") `
        -TestCommand "dotnet run --project examples/sandbox-project/SandboxApp -- AgentBridge" `
        -AutoTest `
        -Message "Modify examples/sandbox-project/SandboxApp/Program.cs so Greeter.CreateGreeting includes the phrase 'from AgentBridge' while preserving the supplied name. Do not edit any other files." `
        -- --map-tokens 0 --no-stream --no-pretty --yes-always

    if ($LASTEXITCODE -ne 0) {
        throw "Aider Docker canary failed with exit code $LASTEXITCODE."
    }

    Write-Section "Validate Result"
    $validation = dotnet run --project (Join-Path $workspaceRoot "examples\sandbox-project\SandboxApp") -- AgentBridge
    $validationText = ($validation | Out-String).Trim()
    Write-Host $validationText
    if ($validationText -ne "Hello, AgentBridge from AgentBridge!") {
        throw "Aider Docker canary produced unexpected validation output."
    }

    Remove-Item -LiteralPath (Join-Path $workspaceRoot ".aider.chat.history.md") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $workspaceRoot ".aider.input.history") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $sampleAppRoot "bin") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $sampleAppRoot "obj") -Recurse -Force -ErrorAction SilentlyContinue

    $status = git -C $workspaceRoot status --short
    $diff = git -C $workspaceRoot diff -- $programPath
    $diffText = ($diff | Out-String)
    Write-Host ($status | Out-String).Trim()
    Write-Host $diffText.Trim()
    if (-not $diffText.Contains("from AgentBridge")) {
        throw "Aider Docker canary diff did not include the expected greeting change."
    }

    Write-Section "Result"
    [pscustomobject]@{
        status = "passed"
        workspace = $workspaceRoot
        image = $Image
        endpoint = $Endpoint.TrimEnd("/")
        model = $Model
        agentbridge_url = "$serverBase/v1"
        aider_url_from_container = $containerBase
        validation = $validationText
        server_stdout = $serverOut
        server_stderr = $serverErr
    } | ConvertTo-Json -Depth 10
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

    if (-not $KeepWorkspace -and (Test-Path -LiteralPath $workspaceRoot)) {
        if (-not (Test-PathInside -ParentPath $scratchRoot -ChildPath $workspaceRoot)) {
            throw "Refusing to remove scratch workspace outside scratch root: $workspaceRoot"
        }

        Remove-Item -LiteralPath $workspaceRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Pop-Location
}
