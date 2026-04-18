#Requires -RunAsAdministrator
# =============================================================================
# GuardianLens - Local deploy (C:\guardianlens) + ngrok-friendly settings
#
# ngrok terminates HTTPS and forwards HTTP to this machine (usually 127.0.0.1:80).
# The proxy enables forwarded headers from loopback only so X-Forwarded-Proto works.
#
# Prerequisites:
#   - ngrok installed, `ngrok config add-authtoken` done, reserved name on your plan
#   - GuardianLens.Proxy listening on port 80 (Windows Services after this script)
#
# Run (Administrator PowerShell):
#   & .\deploy\windows\scripts\deploy-windows-ngrok.ps1
#
# Then in another window (any user):
#   ngrok http http://127.0.0.1:80 --domain nonautonomous-ariel-overexertedly.ngrok-free.dev
# =============================================================================

param(
    [string]$ProjectRoot = "",

    [string]$NgrokDomain = "nonautonomous-ariel-overexertedly.ngrok-free.dev",

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run as Administrator."
}

if (-not $ProjectRoot) {
    # PSScriptRoot = <repo>\deploy\windows\scripts ; repo root is three levels up
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

# Publish outside GuardianLens.*\publish so a running `dotnet` / dev host cannot lock output DLLs.
$buildPublishRoot = Join-Path $ProjectRoot "build-publish"
$apiPublishOut = Join-Path $buildPublishRoot "api"
$proxyPublishOut = Join-Path $buildPublishRoot "proxy"
$uiDir = Join-Path $ProjectRoot "guardians-ui"
$syncScript = Join-Path $PSScriptRoot "remote-sync-windows-services.ps1"

if (-not (Test-Path $syncScript)) { throw "Missing: $syncScript" }

$dirs = @(
    "C:\guardianlens\app\api",
    "C:\guardianlens\app\proxy",
    "C:\guardianlens\app\frontend",
    "C:\guardianlens\data",
    "C:\guardianlens\ssl\acme-webroot\.well-known\acme-challenge",
    "C:\guardianlens\config"
)
foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

Write-Host "> Stopping GuardianLens services (releases DLL locks on C:\guardianlens and avoids deploy races)..." -ForegroundColor Yellow
Stop-Service -Name "GuardianLensProxy" -Force -ErrorAction SilentlyContinue
Stop-Service -Name "GuardianLensAPI" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

function Invoke-RobocopyMirror {
    param([string]$Src, [string]$Dst)
    if (-not (Test-Path $Src)) { throw "Missing path: $Src" }
    $r = robocopy $Src $Dst /MIR /R:2 /W:5 /NFL /NDL /NJH /NJS
    if ($r -ge 8) { throw "robocopy failed with exit code $r ($Src -> $Dst)" }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet CLI not found on PATH. Elevated Administrator shells often miss user PATH entries. Add 'C:\Program Files\dotnet' to the machine PATH, then open a new Administrator window."
}

if (-not $SkipBuild) {
    New-Item -ItemType Directory -Path $apiPublishOut -Force | Out-Null
    New-Item -ItemType Directory -Path $proxyPublishOut -Force | Out-Null

    Write-Host "> Building GuardianLens.API (output: $apiPublishOut)..." -ForegroundColor Yellow
    & dotnet publish (Join-Path $ProjectRoot "GuardianLens.API\GuardianLens.API.csproj") `
        -c Release -r win-x64 --self-contained false -o $apiPublishOut /p:UseAppHost=true
    # Do not use $LASTEXITCODE alone: it can be $null after success; ($null -ne 0) is true in PowerShell.
    if (-not $?) { throw "API publish failed (exit: $LASTEXITCODE). See dotnet output above." }

    Write-Host "> Building GuardianLens.Proxy (output: $proxyPublishOut)..." -ForegroundColor Yellow
    & dotnet publish (Join-Path $ProjectRoot "GuardianLens.Proxy\GuardianLens.Proxy.csproj") `
        -c Release -r win-x64 --self-contained false -o $proxyPublishOut /p:UseAppHost=true
    if (-not $?) { throw "Proxy publish failed (exit: $LASTEXITCODE). See dotnet output above." }

    Write-Host "> Building guardians-ui (VITE_API_URL from .env.production)..." -ForegroundColor Yellow
    Push-Location $uiDir
    try {
        if (-not (Test-Path "node_modules")) {
            & npm install
            if (-not $?) { throw "npm install failed (exit: $LASTEXITCODE)" }
        }
        & npm run build
        if (-not $?) { throw "npm run build failed (exit: $LASTEXITCODE)" }
    }
    finally { Pop-Location }
}

Write-Host "> Copying to C:\guardianlens\app\..." -ForegroundColor Yellow
Invoke-RobocopyMirror -Src $apiPublishOut -Dst "C:\guardianlens\app\api"
Invoke-RobocopyMirror -Src $proxyPublishOut -Dst "C:\guardianlens\app\proxy"
Invoke-RobocopyMirror -Src (Join-Path $uiDir "dist") -Dst "C:\guardianlens\app\frontend"

# No TLS on Kestrel when using ngrok - overwrite extras so old Let's Encrypt vars are not left behind.
$acmeRoot = "C:\guardianlens\ssl\acme-webroot"
$extraEnvPath = "C:\guardianlens\config\proxy-extra-env.txt"
@"
# Used by remote-sync-windows-services.ps1 for GuardianLensProxy
Proxy__FrontendStaticPath=C:\guardianlens\app\frontend
Proxy__AcmeWebRoot=$acmeRoot
"@ | Set-Content -Path $extraEnvPath -Encoding ASCII
icacls $extraEnvPath /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" | Out-Null

Write-Host "> Syncing Windows services..." -ForegroundColor Yellow
& $syncScript

Write-Host ""
Write-Host "Deploy complete. Start ngrok in a second terminal (leave it running):" -ForegroundColor Green
Write-Host "  ngrok http http://127.0.0.1:80 --domain $NgrokDomain" -ForegroundColor Cyan
Write-Host ""
Write-Host "Then open: https://$NgrokDomain/" -ForegroundColor Green
Write-Host ""
