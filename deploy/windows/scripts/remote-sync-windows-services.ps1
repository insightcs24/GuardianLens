# =============================================================================
# GuardianLens - Create or update Windows Services (run ON EC2 as Administrator)
# Used by install-services.ps1 and copied + invoked by deploy-all.ps1
# Service names: GuardianLensAPI, GuardianLensProxy (no spaces - SCM / sc.exe)
# =============================================================================

$ErrorActionPreference = "Stop"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script as Administrator."
}

# Paths passed to New-Service / SCM (quoted exe + args)
$apiExe = 'C:\guardianlens\app\api\GuardianLens.API.exe'
$apiRoot = 'C:\guardianlens\app\api'
$apiBinPath = "`"$apiExe`" --contentRoot $apiRoot"

$proxyExe = 'C:\guardianlens\app\proxy\GuardianLens.Proxy.exe'
$proxyRoot = 'C:\guardianlens\app\proxy'
$proxyBinPath = "`"$proxyExe`" --contentRoot $proxyRoot"

$apiEnvStrings = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "ASPNETCORE_URLS=http://localhost:5000"
)

$apiExtraEnvPath = "C:\guardianlens\config\api-extra-env.txt"
if (Test-Path $apiExtraEnvPath) {
    Write-Host "> Merging extra API env: $apiExtraEnvPath" -ForegroundColor Yellow
    $extraApiLines = Get-Content $apiExtraEnvPath -ErrorAction Stop | ForEach-Object {
        $line = $_.Trim()
        if ($line.Length -eq 0) { return }
        if ($line.StartsWith("#")) { return }
        $line
    }
    $apiEnvStrings = @($apiEnvStrings) + @($extraApiLines)
}

$proxyEnvStrings = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "ASPNETCORE_URLS=http://0.0.0.0:80",
    "DOTNET_PRINT_TELEMETRY_MESSAGE=false"
)

$proxyExtraEnvPath = "C:\guardianlens\config\proxy-extra-env.txt"
if (Test-Path $proxyExtraEnvPath) {
    Write-Host "> Merging extra proxy env: $proxyExtraEnvPath" -ForegroundColor Yellow
    $extraLines = Get-Content $proxyExtraEnvPath -ErrorAction Stop | ForEach-Object {
        $line = $_.Trim()
        if ($line.Length -eq 0) { return }
        if ($line.StartsWith("#")) { return }
        $line
    }
    $proxyEnvStrings = @($proxyEnvStrings) + @($extraLines)
}

function Remove-LegacyServiceIfPresent {
    param([string]$LegacyName)
    $svc = Get-Service -Name $LegacyName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "   Removing legacy service: $LegacyName" -ForegroundColor Yellow
        Stop-Service -Name $LegacyName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $LegacyName | Out-Null
        Start-Sleep -Seconds 2
    }
}

function Set-ServiceEnvironment {
    param([string]$ServiceName, [string[]]$EnvStrings)
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path $regPath)) { throw "Registry path missing for service: $ServiceName" }
    Set-ItemProperty -Path $regPath -Name "Environment" -Value $EnvStrings
}

function Ensure-GuardianLensService {
    param(
        [string]$ServiceName,
        [string]$DisplayName,
        [string]$Description,
        [string]$BinPath,
        [string[]]$EnvStrings
    )

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "   Updating existing service: $ServiceName" -ForegroundColor White
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        # sc.exe: space after binPath= ; $BinPath is already `"exe.exe`" --contentRoot ...`
        $cmdLine = "sc config $ServiceName binPath= $BinPath"
        $p = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $cmdLine -Wait -NoNewWindow -PassThru
        if ($p.ExitCode -ne 0) {
            throw "sc config binPath failed for $ServiceName (exit $($p.ExitCode))"
        }
    }
    else {
        Write-Host "   Creating service: $ServiceName" -ForegroundColor White
        New-Service `
            -Name $ServiceName `
            -BinaryPathName $BinPath `
            -DisplayName $DisplayName `
            -Description $Description `
            -StartupType Automatic | Out-Null
    }

    Set-ServiceEnvironment -ServiceName $ServiceName -EnvStrings $EnvStrings
}

Write-Host ""
Write-Host "GuardianLens - sync Windows services" -ForegroundColor Cyan
Write-Host ""

# Remove old spaced service names from earlier deploy scripts
Remove-LegacyServiceIfPresent -LegacyName "GuardianLens API"
Remove-LegacyServiceIfPresent -LegacyName "GuardianLens Proxy"

Write-Host "> Configuring GuardianLensAPI..." -ForegroundColor Yellow
Ensure-GuardianLensService `
    -ServiceName "GuardianLensAPI" `
    -DisplayName "GuardianLens API" `
    -Description "GuardianLens Digital Asset Protection Platform - .NET 8 Backend API" `
    -BinPath $apiBinPath `
    -EnvStrings $apiEnvStrings

Write-Host "> Configuring GuardianLensProxy..." -ForegroundColor Yellow
Ensure-GuardianLensService `
    -ServiceName "GuardianLensProxy" `
    -DisplayName "GuardianLens Proxy (YARP)" `
    -Description "GuardianLens YARP Reverse Proxy - Routes /api to .NET backend, serves React on /" `
    -BinPath $proxyBinPath `
    -EnvStrings $proxyEnvStrings

Write-Host "> Setting service dependency (Proxy depends on API)..." -ForegroundColor Yellow
& sc.exe config GuardianLensProxy depend= GuardianLensAPI | Out-Null
Write-Host "   Dependency set" -ForegroundColor Green

Write-Host "> Configuring automatic recovery on failure..." -ForegroundColor Yellow
foreach ($svc in @("GuardianLensAPI", "GuardianLensProxy")) {
    & sc.exe failure $svc reset= 60 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    Write-Host "   Recovery configured for: $svc" -ForegroundColor White
}

Write-Host "> Starting services..." -ForegroundColor Yellow
Start-Service -Name "GuardianLensAPI"
Start-Sleep -Seconds 3
Start-Service -Name "GuardianLensProxy"
Start-Sleep -Seconds 2

Get-Service -Name "GuardianLensAPI", "GuardianLensProxy" | Format-Table Name, Status, StartType -AutoSize

Write-Host ""
Write-Host "GuardianLens services synced." -ForegroundColor Green
Write-Host ""
