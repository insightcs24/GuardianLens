# =============================================================================
# GuardianLens - Install Windows Services (run ON the EC2 instance)
#
# Copy this script to C:\guardianlens\ on your EC2 instance, then run:
#   Right-click PowerShell - Run as Administrator
#   .\install-services.ps1
#
# Run this AFTER you have copied the published .NET binaries to:
#   C:\guardianlens\app\api\       (GuardianLens.API.exe)
#   C:\guardianlens\app\proxy\     (GuardianLens.Proxy.exe)
#
# Implementation: remote-sync-windows-services.ps1 (same logic as deploy-all.ps1)
# =============================================================================

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "GuardianLens - Windows Service Installation" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

$syncScript = Join-Path $PSScriptRoot "remote-sync-windows-services.ps1"
if (-not (Test-Path $syncScript)) {
    throw "Missing: $syncScript"
}

& $syncScript

Write-Host ""
Write-Host "> Testing API endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost/api/assets/dashboard" -UseBasicParsing
    Write-Host "   API responding: HTTP $($response.StatusCode)" -ForegroundColor Green
}
catch {
    Write-Host "   API not yet responding - check logs in C:\guardianlens\logs\" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "  Services installed and started!" -ForegroundColor Green
Write-Host "  View in Services (services.msc):" -ForegroundColor Green
Write-Host "    - GuardianLens API" -ForegroundColor Green
Write-Host "    - GuardianLens Proxy (YARP)" -ForegroundColor Green
Write-Host "  Live blockchain: optional C:\guardianlens\config\api-extra-env.txt" -ForegroundColor Green
Write-Host "    (Blockchain__PrivateKey, Blockchain__ContractAddress - see repo blockchain\README.txt)" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
Write-Host ""
