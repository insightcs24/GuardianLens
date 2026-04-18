#Requires -RunAsAdministrator
# =============================================================================
# GuardianLens - Local-path deploy on this Windows machine (e.g. EC2) + HTTPS
#
# Prerequisites:
#   - Public DNS A/AAAA record for -Domain pointing to THIS machine's public IP
#   - AWS Security Group: inbound TCP 80 and 443 (and 3389 for RDP if needed)
#   - Port 80 must reach GuardianLens.Proxy (Let's Encrypt validates over HTTP)
#
# Run (Administrator PowerShell), from repo or any folder:
#   & "C:\path\to\GuardianLens\deploy\windows\scripts\deploy-local-with-https.ps1" `
#        -Domain "app.example.com" `
#        -Email "you@example.com"
#
# What it does:
#   1. dotnet publish API + Proxy, npm build UI to C:\guardianlens\app\...
#   2. Ensures ACME webroot and optional win-acme download
#   3. Starts services (HTTP + ACME); runs win-acme (Let's Encrypt) filesystem HTTP-01
#   4. Writes PFX + service env (Proxy__CertPath / Proxy__CertPassword); restarts proxy for HTTPS :443
# =============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$Domain,

    [Parameter(Mandatory = $true)]
    [string]$Email,

    [string]$ProjectRoot = "",

    [switch]$SkipBuild,

    [switch]$SkipCert,

    [switch]$VerboseWacs
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

$buildPublishRoot = Join-Path $ProjectRoot "build-publish"
$apiPublishOut = Join-Path $buildPublishRoot "api"
$proxyPublishOut = Join-Path $buildPublishRoot "proxy"
$uiDir = Join-Path $ProjectRoot "guardians-ui"
$syncScript = Join-Path $PSScriptRoot "remote-sync-windows-services.ps1"
$reloadScriptSrc = Join-Path $PSScriptRoot "reload-proxy-after-cert.ps1"

if (-not (Test-Path $syncScript)) { throw "Missing: $syncScript" }

# --- Layout on disk (matches appsettings) ---
$dirs = @(
    "C:\guardianlens\app\api",
    "C:\guardianlens\app\proxy",
    "C:\guardianlens\app\frontend",
    "C:\guardianlens\data",
    "C:\guardianlens\ssl\acme-webroot\.well-known\acme-challenge",
    "C:\guardianlens\config",
    "C:\guardianlens\tools\wacs",
    "C:\guardianlens\tools"
)
foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

# Firewall 443 (80 may already exist from setup-windows-ec2.ps1)
$fw443 = Get-NetFirewallRule -DisplayName "GuardianLens HTTPS" -ErrorAction SilentlyContinue
if (-not $fw443) {
    New-NetFirewallRule -DisplayName "GuardianLens HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow | Out-Null
    Write-Host "Created firewall rule: GuardianLens HTTPS (443)" -ForegroundColor Green
}

Copy-Item -Force $reloadScriptSrc "C:\guardianlens\tools\reload-proxy-after-cert.ps1"

Write-Host "> Stopping GuardianLens services (releases DLL locks)..." -ForegroundColor Yellow
Stop-Service -Name "GuardianLensProxy" -Force -ErrorAction SilentlyContinue
Stop-Service -Name "GuardianLensAPI" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

function Invoke-RobocopyMirror {
    param([string]$Src, [string]$Dst)
    if (-not (Test-Path $Src)) { throw "Missing path: $Src" }
    $r = robocopy $Src $Dst /MIR /R:2 /W:5 /NFL /NDL /NJH /NJS
    if ($r -ge 8) { throw "robocopy failed with exit code $r ($Src -> $Dst)" }
}

# --- Build ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet CLI not found on PATH. Elevated Administrator shells often miss user PATH entries. Add 'C:\Program Files\dotnet' to the machine PATH, then open a new Administrator window."
}

if (-not $SkipBuild) {
    New-Item -ItemType Directory -Path $apiPublishOut -Force | Out-Null
    New-Item -ItemType Directory -Path $proxyPublishOut -Force | Out-Null

    Write-Host "> Building GuardianLens.API (output: $apiPublishOut)..." -ForegroundColor Yellow
    & dotnet publish (Join-Path $ProjectRoot "GuardianLens.API\GuardianLens.API.csproj") `
        -c Release -r win-x64 --self-contained false -o $apiPublishOut /p:UseAppHost=true
    if (-not $?) { throw "API publish failed (exit: $LASTEXITCODE). See dotnet output above." }

    Write-Host "> Building GuardianLens.Proxy (output: $proxyPublishOut)..." -ForegroundColor Yellow
    & dotnet publish (Join-Path $ProjectRoot "GuardianLens.Proxy\GuardianLens.Proxy.csproj") `
        -c Release -r win-x64 --self-contained false -o $proxyPublishOut /p:UseAppHost=true
    if (-not $?) { throw "Proxy publish failed (exit: $LASTEXITCODE). See dotnet output above." }

    Write-Host "> Building guardians-ui..." -ForegroundColor Yellow
    Push-Location $uiDir
    try {
        if (-not (Test-Path "node_modules")) {
            & npm install
            if (-not $?) { throw "npm install failed (exit: $LASTEXITCODE)" }
        }
        $env:VITE_API_URL = ""
        & npm run build
        if (-not $?) { throw "npm build failed (exit: $LASTEXITCODE)" }
    }
    finally {
        Pop-Location
        Remove-Item Env:\VITE_API_URL -ErrorAction SilentlyContinue
    }
}

Write-Host "> Copying artifacts to C:\guardianlens\app\..." -ForegroundColor Yellow
Invoke-RobocopyMirror -Src $apiPublishOut -Dst "C:\guardianlens\app\api"
Invoke-RobocopyMirror -Src $proxyPublishOut -Dst "C:\guardianlens\app\proxy"
Invoke-RobocopyMirror -Src (Join-Path $uiDir "dist") -Dst "C:\guardianlens\app\frontend"

# Service env overrides (merged by remote-sync-windows-services.ps1)
$acmeRoot = "C:\guardianlens\ssl\acme-webroot"
$pfxPath = "C:\guardianlens\ssl\guardianlens.pfx"
$extraEnvPath = "C:\guardianlens\config\proxy-extra-env.txt"
@"
# Merged into GuardianLensProxy service Environment (see remote-sync-windows-services.ps1)
Proxy__FrontendStaticPath=C:\guardianlens\app\frontend
Proxy__AcmeWebRoot=$acmeRoot
"@ | Set-Content -Path $extraEnvPath -Encoding ASCII
icacls $extraEnvPath /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" | Out-Null

Write-Host "> Syncing Windows services (HTTP first, ACME webroot live)..." -ForegroundColor Yellow
& $syncScript

if ($SkipCert) {
    Write-Host "SkipCert: done (HTTP only). Remove -SkipCert to obtain Let's Encrypt certificate." -ForegroundColor Yellow
    return
}

# --- win-acme (Let's Encrypt) ---
$wacsDir = "C:\guardianlens\tools\wacs"
$wacsExe = Join-Path $wacsDir "wacs.exe"
if (-not (Test-Path $wacsExe)) {
    Write-Host "> Downloading win-acme..." -ForegroundColor Yellow
    $zipUrl = "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.pluggable.zip"
    $zipPath = "$env:TEMP\win-acme.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $wacsDir -Force
    Remove-Item $zipPath -Force
}

if (-not (Test-Path $wacsExe)) { throw "wacs.exe not found under $wacsDir" }

# Strong PFX password (also written for disaster recovery; restrict ACL)
$pfxPasswordPlain = -join ((48..57 + 65..90 + 97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
$pfxPassFile = "C:\guardianlens\ssl\pfx-password.txt"
$pfxPasswordPlain | Set-Content -Path $pfxPassFile -Encoding ASCII
icacls $pfxPassFile /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" | Out-Null

Write-Host ""
Write-Host "Requesting Let's Encrypt certificate for: $Domain" -ForegroundColor Cyan
Write-Host "  DNS for this name must point to this server's public IP." -ForegroundColor White
Write-Host "  HTTP-01 validation uses: http://${Domain}/.well-known/acme-challenge/ ..." -ForegroundColor White
Write-Host ""

# --installation none: we restart services after writing Proxy__Cert* to proxy-extra-env.txt
$wacsArgs = @(
    "--accepttos",
    "--emailaddress", $Email,
    "--source", "manual",
    "--host", $Domain,
    "--validation", "filesystem",
    "--validationmode", "http-01",
    "--webroot", $acmeRoot,
    "--store", "pfxfile",
    "--pfxfilepath", $pfxPath,
    "--pfxpassword", $pfxPasswordPlain,
    "--installation", "none"
)
if ($VerboseWacs) { $wacsArgs = @("--verbose") + $wacsArgs }

$p = Start-Process -FilePath $wacsExe -ArgumentList $wacsArgs -WorkingDirectory $wacsDir `
    -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) {
    throw "win-acme (wacs.exe) failed with exit code $($p.ExitCode). Ensure DNS points here and port 80 is reachable from the internet."
}

if (-not (Test-Path $pfxPath)) {
    throw "PFX not found at $pfxPath after win-acme - check wacs logs under $wacsDir"
}

# Append cert env for Kestrel HTTPS listener
Add-Content -Path $extraEnvPath -Value "Proxy__CertPath=$pfxPath" -Encoding ASCII
Add-Content -Path $extraEnvPath -Value "Proxy__CertPassword=$pfxPasswordPlain" -Encoding ASCII
icacls $extraEnvPath /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" | Out-Null

Write-Host "> Re-syncing Windows services (HTTPS + env)..." -ForegroundColor Yellow
& $syncScript

Write-Host ""
Write-Host "Done. HTTPS:" -ForegroundColor Green
Write-Host "  https://$Domain/" -ForegroundColor White
Write-Host "  https://$Domain/swagger" -ForegroundColor White
Write-Host "PFX password backup (restrictive ACL): $pfxPassFile" -ForegroundColor DarkGray
Write-Host ""
