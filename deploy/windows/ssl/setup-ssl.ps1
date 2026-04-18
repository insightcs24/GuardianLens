# =============================================================================
# GuardianLens — Free SSL Certificate with win-acme (Let's Encrypt)
# Run ON your EC2 instance after you have a domain pointing to your EC2 IP
#
# Prerequisites:
#   1. You have a domain name (e.g. guardianlens.duckdns.org)
#   2. Domain's A record points to your EC2 public IP
#   3. Port 80 is open in your AWS Security Group
#   4. Services are running (so win-acme can verify domain ownership)
#
# Usage (run as Administrator on EC2):
#   .\setup-ssl.ps1 -Domain "yourdomain.com" -Email "you@email.com"
# =============================================================================

#Requires -RunAsAdministrator

param(
    [Parameter(Mandatory=$true)]
    [string]$Domain,

    [Parameter(Mandatory=$true)]
    [string]$Email
)

$ErrorActionPreference = "Stop"
$WinAcmePath = "C:\guardianlens\ssl\win-acme"
$CertOutput  = "C:\guardianlens\ssl\certs"

Write-Host ""
Write-Host "GuardianLens — SSL Certificate Setup" -ForegroundColor Cyan
Write-Host "Domain: $Domain" -ForegroundColor White
Write-Host ""

# ── Download win-acme ────────────────────────────────────────────────────────
Write-Host "→ Downloading win-acme..." -ForegroundColor Yellow

if (-not (Test-Path $WinAcmePath)) {
    New-Item -ItemType Directory -Path $WinAcmePath -Force | Out-Null
}

$winAcmeZip = "$env:TEMP\win-acme.zip"
Invoke-WebRequest `
    -Uri "https://github.com/win-acme/win-acme/releases/latest/download/win-acme.v2.2.9.1701.x64.pluggable.zip" `
    -OutFile $winAcmeZip
Expand-Archive -Path $winAcmeZip -DestinationPath $WinAcmePath -Force
Remove-Item $winAcmeZip

New-Item -ItemType Directory -Path $CertOutput -Force | Out-Null

# ── Run win-acme to get certificate ──────────────────────────────────────────
Write-Host ""
Write-Host "→ Requesting certificate from Let's Encrypt..." -ForegroundColor Yellow
Write-Host "   This requires port 80 to be accessible from the internet." -ForegroundColor White

$wacs = Join-Path $WinAcmePath "wacs.exe"

# Request certificate silently
& $wacs `
    --source manual `
    --host $Domain `
    --validation selfhosting `
    --validationport 80 `
    --store pemfiles `
    --pemfilespath $CertOutput `
    --emailaddress $Email `
    --accepttos `
    --notaskscheduler

# ── Check if certificate was created ─────────────────────────────────────────
$certFile = Join-Path $CertOutput "$Domain-crt.pem"
$keyFile  = Join-Path $CertOutput "$Domain-key.pem"

if ((Test-Path $certFile) -and (Test-Path $keyFile)) {
    Write-Host ""
    Write-Host "Certificate files created:" -ForegroundColor Green
    Write-Host "   Certificate: $certFile" -ForegroundColor White
    Write-Host "   Private Key: $keyFile"  -ForegroundColor White

    # Convert PEM to PFX for .NET Kestrel
    Write-Host ""
    Write-Host "→ Converting to PFX format for .NET..." -ForegroundColor Yellow
    $pfxPath     = "C:\guardianlens\ssl\guardianlens.pfx"
    $pfxPassword = "GuardianLens2026!"  # Change this!

    & openssl pkcs12 -export `
        -in $certFile `
        -inkey $keyFile `
        -out $pfxPath `
        -passout "pass:$pfxPassword"

    Write-Host "   PFX created: $pfxPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "→ Add these lines to C:\guardianlens\app\proxy\appsettings.json:" -ForegroundColor Yellow
    Write-Host '   "Proxy": {' -ForegroundColor White
    Write-Host "     `"CertPath`": `"$pfxPath`"," -ForegroundColor Cyan
    Write-Host "     `"CertPassword`": `"$pfxPassword`"" -ForegroundColor Cyan
    Write-Host '   }' -ForegroundColor White

    Write-Host ""
    Write-Host "→ Then restart the proxy service:" -ForegroundColor Yellow
    Write-Host "   Restart-Service -Name GuardianLensProxy" -ForegroundColor White

} else {
    Write-Host ""
    Write-Host "Certificate not created. Common reasons:" -ForegroundColor Red
    Write-Host "  - Domain does not resolve to this server's public IP" -ForegroundColor White
    Write-Host "  - Port 80 is blocked by AWS Security Group" -ForegroundColor White
    Write-Host "  - GuardianLens Proxy service is not running" -ForegroundColor White
}

# ── Auto-renewal task ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "→ Setting up auto-renewal (runs daily at 9 AM)..." -ForegroundColor Yellow

$taskAction  = New-ScheduledTaskAction -Execute $wacs -Argument "--renew --baseuri https://acme-v02.api.letsencrypt.org/"
$taskTrigger = New-ScheduledTaskTrigger -Daily -At "09:00"
$taskSettings = New-ScheduledTaskSettingsSet -RunOnlyIfNetworkAvailable

Register-ScheduledTask `
    -TaskName    "GuardianLens SSL Renewal" `
    -Action      $taskAction `
    -Trigger     $taskTrigger `
    -Settings    $taskSettings `
    -RunLevel    Highest `
    -Force | Out-Null

Write-Host "   Auto-renewal task registered" -ForegroundColor Green
Write-Host ""
