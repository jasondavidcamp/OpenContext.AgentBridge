[CmdletBinding()]
param(
    [string]$Endpoint = $(if ($env:AGENTBRIDGE_GATEWAY_ENDPOINT) { $env:AGENTBRIDGE_GATEWAY_ENDPOINT } else { "https://generativelanguage.googleapis.com/v1beta/openai/" }),
    [string]$Model = $(if ($env:AGENTBRIDGE_GATEWAY_MODEL) { $env:AGENTBRIDGE_GATEWAY_MODEL } else { "gemini-2.5-flash" }),
    [string]$ApiKey = $env:AGENTBRIDGE_GATEWAY_API_KEY,
    [int]$ProxyPort = 5382,
    [string]$ClineImage = "opencontext-agentbridge-cline:latest",
    [string]$ProxyImage = "opencontext-agentbridge-constrained-proxy:latest",
    [int]$MaxAttempts = 2,
    [switch]$SkipClineImageBuild,
    [switch]$SkipProxyImageBuild,
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

function Get-GeminiApiKey {
    if ($env:AGENTBRIDGE_GEMINI_API_KEY) {
        return $env:AGENTBRIDGE_GEMINI_API_KEY
    }

    return [Environment]::GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY", "User")
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

function New-LfFile {
    param(
        [string]$Path,
        [string]$Content
    )

    [IO.File]::WriteAllText(
        $Path,
        $Content.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    $Model = Read-Host "Gateway model id"
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    throw "A gateway model id is required."
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = Get-GeminiApiKey
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
$scratchRoot = Join-Path $repoRoot ".agentbridge\cline-split-canary-workspaces"
$workspaceRoot = Join-Path $scratchRoot "workspace-$stamp"
$notesRoot = Join-Path $workspaceRoot "notes"
$networkName = "agentbridge-cline-$stamp"
$proxyName = "agentbridge-proxy-$stamp"
$hostProxyBase = "http://127.0.0.1:$ProxyPort"
$containerProxyBase = "http://$proxyName`:8080/v1"
$proxyStarted = $false
$networkCreated = $false

Push-Location $repoRoot
try {
    Write-Section "Preflight"
    Wait-ForDocker

    docker image inspect $ClineImage 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        if ($SkipClineImageBuild) {
            throw "Docker image not found: $ClineImage. Build docker\cline.Dockerfile or remove -SkipClineImageBuild."
        }

        Write-Section "Build Cline Image"
        docker build -f .\docker\cline.Dockerfile -t $ClineImage .
        if ($LASTEXITCODE -ne 0) {
            throw "Cline image build failed with exit code $LASTEXITCODE."
        }
    }

    docker image inspect $ProxyImage 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        if ($SkipProxyImageBuild) {
            throw "Docker image not found: $ProxyImage. Build docker\constrained-proxy.Dockerfile or remove -SkipProxyImageBuild."
        }

        Write-Section "Build Proxy Image"
        docker build -f .\docker\constrained-proxy.Dockerfile -t $ProxyImage .
        if ($LASTEXITCODE -ne 0) {
            throw "Proxy image build failed with exit code $LASTEXITCODE."
        }
    }

    Write-Section "Prepare Scratch Workspace"
    New-Item -ItemType Directory -Force -Path $scratchRoot, $workspaceRoot, $notesRoot | Out-Null
    New-LfFile -Path (Join-Path $workspaceRoot "README.md") -Content @"
# Split Proxy Canary

This scratch workspace verifies that a Cline-only client container can talk to a separate constrained proxy container.
"@
    New-LfFile -Path (Join-Path $notesRoot "message.txt") -Content "Hello, AgentBridge!`n"
    git -C $workspaceRoot init | Out-Null
    git -C $workspaceRoot config user.name "Jason Camp"
    git -C $workspaceRoot config user.email "Jason.Camp@gmail.com"
    git -C $workspaceRoot config core.autocrlf false
    git -C $workspaceRoot add README.md notes
    git -C $workspaceRoot commit -m Baseline | Out-Null

    Write-Section "Start Proxy Container"
    docker network create $networkName | Out-Null
    $networkCreated = $true

    docker run `
        -d `
        --rm `
        --name $proxyName `
        --network $networkName `
        -p "127.0.0.1:$ProxyPort`:8080" `
        -e "AGENTBRIDGE_SERVER_WORKSPACE=/workspace" `
        -e "AGENTBRIDGE_MODEL_PROVIDER=gateway" `
        -e "AGENTBRIDGE_GATEWAY_ENDPOINT=$($Endpoint.TrimEnd("/"))" `
        -e "AGENTBRIDGE_GATEWAY_MODEL=$Model" `
        -e "AGENTBRIDGE_GATEWAY_API_KEY=$ApiKey" `
        -e "AGENTBRIDGE_LOG_MODEL_TRAFFIC=false" `
        -v "$workspaceRoot`:/workspace" `
        $ProxyImage | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Proxy container failed to start with exit code $LASTEXITCODE."
    }
    $proxyStarted = $true
    Wait-ForModels -BaseUrl $hostProxyBase

    $validationText = ""
    $lastClineError = $null
    $expectedText = "Hello, AgentBridge from AgentBridge!"
    $validationCommand = "node -e `"const fs=require('fs'); const text=fs.readFileSync('notes/message.txt','utf8').trim(); if (text !== '$expectedText') { console.error(text); process.exit(1); } console.log(text);`""
    $message = "You must use tools for this task. First read README.md and notes/message.txt. Then modify only notes/message.txt so it contains exactly: $expectedText Then run this validation command: $validationCommand"

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if ($attempt -gt 1) {
            git -C $workspaceRoot checkout -- notes/message.txt
        }

        Write-Section "Cline Client Container (Attempt $attempt of $MaxAttempts)"
        try {
            & (Join-Path $PSScriptRoot "Start-ClineDocker.ps1") `
                -Image $ClineImage `
                -Network $networkName `
                -Workspace $workspaceRoot `
                -Model $Model `
                -OpenAiApiBase $containerProxyBase `
                -ApiKey "agentbridge-local" `
                -TimeoutSeconds 420 `
                -Thinking low `
                -Message $message
        }
        catch {
            $lastClineError = $_
            Write-Warning "Cline attempt $attempt failed: $($_.Exception.Message)"
            continue
        }

        $validation = Get-Content -LiteralPath (Join-Path $notesRoot "message.txt") -Raw
        $validationText = $validation.Trim()
        if ($validationText -eq $expectedText) {
            break
        }

        Write-Warning "Cline attempt $attempt completed without the expected edit. Validation was: $validationText"
    }

    Write-Section "Validate Result"
    Write-Host $validationText
    if ($validationText -ne $expectedText) {
        if ($lastClineError) {
            Write-Warning "Last Cline error: $($lastClineError.Exception.Message)"
        }

        throw "Cline split proxy canary produced unexpected validation output."
    }

    $status = git -C $workspaceRoot status --short
    $diff = git -C $workspaceRoot diff -- notes/message.txt
    $diffText = ($diff | Out-String)
    Write-Host ($status | Out-String).Trim()
    Write-Host $diffText.Trim()
    if (-not $diffText.Contains("from AgentBridge")) {
        throw "Cline split proxy canary diff did not include the expected message change."
    }

    Write-Section "Result"
    [pscustomobject]@{
        status = "passed"
        workspace = $workspaceRoot
        cline_image = $ClineImage
        proxy_image = $ProxyImage
        endpoint = $Endpoint.TrimEnd("/")
        model = $Model
        host_proxy_url = "$hostProxyBase/v1"
        cline_proxy_url = $containerProxyBase
        validation = $validationText
    } | ConvertTo-Json -Depth 10
}
finally {
    if ($proxyStarted) {
        docker rm -f $proxyName 2>$null | Out-Null
    }

    if ($networkCreated) {
        docker network rm $networkName 2>$null | Out-Null
    }

    if (-not $KeepWorkspace -and (Test-Path -LiteralPath $workspaceRoot)) {
        if (-not (Test-PathInside -ParentPath $scratchRoot -ChildPath $workspaceRoot)) {
            throw "Refusing to remove scratch workspace outside scratch root: $workspaceRoot"
        }

        Remove-Item -LiteralPath $workspaceRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Pop-Location
}
