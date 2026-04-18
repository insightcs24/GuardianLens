# =============================================================================
# GuardianLens - Full Deploy Script for Windows EC2
# Builds BOTH the .NET API and YARP Proxy, then copies to EC2
#
# RUN FROM YOUR LOCAL WINDOWS MACHINE (not on EC2):
#   .\deploy-all.ps1 -EC2IP "54.123.45.67" -PemKey "C:\keys\my-key.pem"
#
# WHAT IT DOES:
#   1. Builds GuardianLens.API (win-x64 Release)
#   2. Builds GuardianLens.Proxy (win-x64 Release)
#   3. Builds React frontend
#   4. Copies all three to EC2 via SCP (uses PEM key)
#   5. Installs Windows Services on EC2 via PowerShell remoting
# =============================================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$EC2IP,

    [Parameter(Mandatory=$true)]
    [string]$PemKey,

    [string]$EC2User = "Administrator",

    [string]$ViteApiUrl = "",   # Empty = same-origin (YARP serves React + API from same host)

    [switch]$SkipBuild,         # Skip build step if binaries already exist
    [switch]$SkipFrontend       # Skip React build
)

$ErrorActionPreference = "Stop"
# PSScriptRoot = <repo>\deploy\windows\scripts - repo root is three parents up
$ProjectRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  GuardianLens - Full Windows EC2 Deploy" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   EC2:      $EC2User@$EC2IP" -ForegroundColor White
Write-Host "   PEM Key:  $PemKey" -ForegroundColor White
Write-Host ""

# --- Helper: run SSH command on EC2 ---
function Invoke-EC2Command {
    param([string]$Command, [string]$Description = "")
    if ($Description) { Write-Host "   > $Description" -ForegroundColor White }
    & ssh -i $PemKey -o StrictHostKeyChecking=no `
        "$EC2User@$EC2IP" `
        "powershell -NonInteractive -Command `"$Command`""
    if ($LASTEXITCODE -ne 0) {
        throw "EC2 command failed: $Description"
    }
}

# --- Helper: SCP file to EC2 ---
function Copy-ToEC2 {
    param([string]$LocalPath, [string]$RemotePath)
    Write-Host "   Uploading $LocalPath to $RemotePath" -ForegroundColor White
    & scp -i $PemKey -o StrictHostKeyChecking=no -r $LocalPath "$EC2User@${EC2IP}:$RemotePath"
    if ($LASTEXITCODE -ne 0) { throw "SCP failed for $LocalPath" }
}

# ============================================================================
# STEP 1: BUILD .NET API
# ============================================================================
if (-not $SkipBuild) {
    Write-Host "> [1/5] Building GuardianLens.API..." -ForegroundColor Yellow

    $apiProject = Join-Path $ProjectRoot "GuardianLens.API\GuardianLens.API.csproj"
    $apiOutput  = Join-Path $ProjectRoot "build-publish\api"
    New-Item -ItemType Directory -Path $apiOutput -Force | Out-Null

    & dotnet publish $apiProject `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -o $apiOutput `
        /p:UseAppHost=true

    if (-not $?) { throw "API build failed (exit: $LASTEXITCODE)" }
    Write-Host "   API build successful: $apiOutput" -ForegroundColor Green
} else {
    Write-Host "> [1/5] Skipping API build (--SkipBuild)" -ForegroundColor Gray
}

# ============================================================================
# STEP 2: BUILD YARP PROXY
# ============================================================================
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "> [2/5] Building GuardianLens.Proxy (YARP)..." -ForegroundColor Yellow

    $proxyProject = Join-Path $ProjectRoot "GuardianLens.Proxy\GuardianLens.Proxy.csproj"
    $proxyOutput  = Join-Path $ProjectRoot "build-publish\proxy"
    New-Item -ItemType Directory -Path $proxyOutput -Force | Out-Null

    & dotnet publish $proxyProject `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -o $proxyOutput `
        /p:UseAppHost=true

    if (-not $?) { throw "Proxy build failed (exit: $LASTEXITCODE)" }
    Write-Host "   Proxy build successful: $proxyOutput" -ForegroundColor Green
} else {
    Write-Host "> [2/5] Skipping Proxy build (--SkipBuild)" -ForegroundColor Gray
}

# ============================================================================
# STEP 3: BUILD REACT FRONTEND
# ============================================================================
if (-not $SkipFrontend) {
    Write-Host ""
    Write-Host "> [3/5] Building React frontend..." -ForegroundColor Yellow

    $frontendDir = Join-Path $ProjectRoot "guardians-ui"

    Push-Location $frontendDir
    try {
        # Set the API URL - empty means same-origin (YARP serves both)
        $env:VITE_API_URL = $ViteApiUrl
        & npm run build
        if (-not $?) { throw "React build failed (exit: $LASTEXITCODE)" }
        Write-Host "   React build successful: dist/" -ForegroundColor Green
    } finally {
        Pop-Location
        $env:VITE_API_URL = $null
    }
} else {
    Write-Host "> [3/5] Skipping frontend build (--SkipFrontend)" -ForegroundColor Gray
}

# ============================================================================
# STEP 4: COPY TO EC2
# ============================================================================
Write-Host ""
Write-Host "> [4/5] Copying files to EC2 ($EC2IP)..." -ForegroundColor Yellow

# Stop services before overwriting (ignore errors if not running yet)
Invoke-EC2Command `
    "Stop-Service -Name 'GuardianLensProxy' -Force -ErrorAction SilentlyContinue; Stop-Service -Name 'GuardianLensAPI' -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2" `
    "Stopping services"

# Copy API
Invoke-EC2Command "Remove-Item 'C:\guardianlens\app\api\*' -Recurse -Force -ErrorAction SilentlyContinue" "Clearing old API files"
Copy-ToEC2 (Join-Path $ProjectRoot "build-publish\api") "C:\guardianlens\app\api_new"
Invoke-EC2Command "Copy-Item 'C:\guardianlens\app\api_new\*' 'C:\guardianlens\app\api\' -Recurse -Force; Remove-Item 'C:\guardianlens\app\api_new' -Recurse -Force" "Installing API files"

# Copy Proxy
Invoke-EC2Command "Remove-Item 'C:\guardianlens\app\proxy\*' -Recurse -Force -ErrorAction SilentlyContinue" "Clearing old Proxy files"
Copy-ToEC2 (Join-Path $ProjectRoot "build-publish\proxy") "C:\guardianlens\app\proxy_new"
Invoke-EC2Command "Copy-Item 'C:\guardianlens\app\proxy_new\*' 'C:\guardianlens\app\proxy\' -Recurse -Force; Remove-Item 'C:\guardianlens\app\proxy_new' -Recurse -Force" "Installing Proxy files"

# Copy React build
$frontendDist = Join-Path $ProjectRoot "guardians-ui\dist"
Invoke-EC2Command "Remove-Item 'C:\guardianlens\app\frontend\*' -Recurse -Force -ErrorAction SilentlyContinue" "Clearing old frontend files"
Copy-ToEC2 $frontendDist "C:\guardianlens\app\frontend_new"
Invoke-EC2Command "Copy-Item 'C:\guardianlens\app\frontend_new\*' 'C:\guardianlens\app\frontend\' -Recurse -Force; Remove-Item 'C:\guardianlens\app\frontend_new' -Recurse -Force" "Installing frontend files"

Write-Host "   All files copied" -ForegroundColor Green

# ============================================================================
# STEP 5: INSTALL AND START WINDOWS SERVICES (same as install-services.ps1)
# ============================================================================
Write-Host ""
Write-Host "> [5/5] Installing and starting Windows Services..." -ForegroundColor Yellow

$syncLocal = Join-Path $PSScriptRoot "remote-sync-windows-services.ps1"
if (-not (Test-Path $syncLocal)) { throw "Missing: $syncLocal" }

Copy-ToEC2 $syncLocal "C:\guardianlens\remote-sync-windows-services.ps1"
Invoke-EC2Command "powershell -ExecutionPolicy Bypass -File C:\guardianlens\remote-sync-windows-services.ps1" "Sync Windows services (GuardianLensAPI / GuardianLensProxy)"

Invoke-EC2Command "Get-Service -Name 'GuardianLensAPI','GuardianLensProxy' | Select-Object Name, Status, StartType | Format-Table -AutoSize" "Service status"

Write-Host ""
Write-Host "==============================================================" -ForegroundColor Green
Write-Host "  Deployment complete!" -ForegroundColor Green
Write-Host "  App:  http://$EC2IP/" -ForegroundColor Green
Write-Host "  API:  http://$EC2IP/swagger" -ForegroundColor Green
Write-Host "==============================================================" -ForegroundColor Green
Write-Host ""
