# =============================================================================
# GuardianLens - Windows EC2 One-Time Setup Script
# Run this ONCE on your Windows EC2 instance as Administrator
#
# Right-click PowerShell, choose Run as Administrator, then:
#   Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
#   .\setup-windows-ec2.ps1
# =============================================================================

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  GuardianLens - Windows EC2 Setup" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Create directory structure ---
Write-Host "> [1/6] Creating application directories..." -ForegroundColor Yellow

$dirs = @(
    "C:\guardianlens\app\api",          # .NET API published binaries
    "C:\guardianlens\app\proxy",        # YARP proxy published binaries
    "C:\guardianlens\app\frontend",     # React static files
    "C:\guardianlens\data",             # SQLite database (persists between deploys)
    "C:\guardianlens\wwwroot\assets",   # Watermarked images uploaded by users
    "C:\guardianlens\logs",             # Application log files
    "C:\guardianlens\ssl"               # SSL certificates
)

foreach ($dir in $dirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "   Created: $dir" -ForegroundColor Green
    } else {
        Write-Host "   Already exists: $dir" -ForegroundColor Gray
    }
}

# --- Step 2: Install .NET 8 ASP.NET Core Runtime (framework-dependent publish) ---
Write-Host ""
Write-Host "> [2/6] Installing .NET 8 ASP.NET Core Runtime..." -ForegroundColor Yellow

function Test-DotNet8Ready {
    try {
        $v = & dotnet --version 2>$null
        return ($v -and $v.StartsWith("8."))
    }
    catch { return $false }
}

if (Test-DotNet8Ready) {
    Write-Host "   .NET 8 SDK/runtime already on PATH: $(dotnet --version)" -ForegroundColor Green
}
else {
    $installed = $false

    # Prefer winget (Server 2022+ / Windows 11, and many AMIs after updates)
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        Write-Host "   Installing via winget (Microsoft.DotNet.AspNetCore.8)..." -ForegroundColor White
        try {
            & winget install --id Microsoft.DotNet.AspNetCore.8 -e --accept-source-agreements --accept-package-agreements -h
        }
        catch {
            Write-Host "   winget install failed, falling back to dotnet-install.ps1" -ForegroundColor Yellow
        }
        $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [Environment]::GetEnvironmentVariable("Path", "User")
        if (Test-DotNet8Ready) { $installed = $true }
    }

    if (-not $installed) {
        Write-Host "   Using official dotnet-install.ps1 (machine-wide under Program Files)..." -ForegroundColor White
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $installScript = "$env:TEMP\dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
        $dotnetRoot = "C:\Program Files\dotnet"
        & $installScript -Channel 8.0 -Runtime aspnetcore -Quality GA -InstallDir $dotnetRoot
        Remove-Item $installScript -ErrorAction SilentlyContinue

        $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
        if ($machinePath -notlike "*$dotnetRoot*") {
            [Environment]::SetEnvironmentVariable(
                "Path",
                ($machinePath.TrimEnd(";") + ";" + $dotnetRoot),
                "Machine")
        }
        $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [Environment]::GetEnvironmentVariable("Path", "User")
    }

    if (Test-DotNet8Ready) {
        Write-Host "   .NET 8 is available: $(dotnet --version)" -ForegroundColor Green
    }
    else {
        Write-Host "   dotnet still not on PATH. Open a new PowerShell window or reboot, then verify: dotnet --version" -ForegroundColor Yellow
        Write-Host "   Manual install: https://dotnet.microsoft.com/download/dotnet/8.0 (ASP.NET Core Runtime, Windows x64)" -ForegroundColor White
    }
}

# --- Step 3: Configure Windows Firewall ---
Write-Host ""
Write-Host "> [3/6] Configuring Windows Firewall rules..." -ForegroundColor Yellow

$firewallRules = @(
    @{ Name = "GuardianLens HTTP";  Port = 80;  Protocol = "TCP" },
    @{ Name = "GuardianLens HTTPS"; Port = 443; Protocol = "TCP" }
)

foreach ($rule in $firewallRules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "   Firewall rule already exists: $($rule.Name)" -ForegroundColor Gray
    } else {
        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Direction   Inbound `
            -Protocol    $rule.Protocol `
            -LocalPort   $rule.Port `
            -Action      Allow | Out-Null
        Write-Host "   Created firewall rule: $($rule.Name) (port $($rule.Port))" -ForegroundColor Green
    }
}

# Port 5000 stays LOCAL ONLY - .NET API is NOT exposed to internet
# YARP proxy on port 80 is the only public entry point
Write-Host "   Note: Port 5000 (.NET API) stays local - NOT opened in firewall" -ForegroundColor Cyan

# --- Step 4: Set permissions on app directories ---
Write-Host ""
Write-Host "> [4/6] Setting directory permissions..." -ForegroundColor Yellow

# Give the LOCAL SYSTEM account (used by Windows Services) full control
$acl = Get-Acl "C:\guardianlens"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "NT AUTHORITY\SYSTEM",
    "FullControl",
    "ContainerInherit,ObjectInherit",
    "None",
    "Allow"
)
$acl.SetAccessRule($rule)
Set-Acl "C:\guardianlens" $acl
Write-Host "   Permissions set for SYSTEM account" -ForegroundColor Green

# --- Step 5: Install Git (for pulling updates) ---
Write-Host ""
Write-Host "> [5/6] Checking Git installation..." -ForegroundColor Yellow

$git = & git --version 2>$null
if ($git) {
    Write-Host "   Git already installed: $git" -ForegroundColor Green
} else {
    Write-Host "   Git not found. Download from https://git-scm.com and install manually." -ForegroundColor Yellow
    Write-Host "   Then re-run this script or continue without it." -ForegroundColor Yellow
}

# --- Step 6: Open AWS Security Group reminder ---
Write-Host ""
Write-Host "> [6/6] AWS Security Group reminder..." -ForegroundColor Yellow
Write-Host "   Make sure your EC2 Security Group has these INBOUND rules:" -ForegroundColor White
Write-Host "   Type        Port   Source" -ForegroundColor Gray
Write-Host "   ----------  -----  ------------" -ForegroundColor Gray
Write-Host "   RDP         3389   My IP only" -ForegroundColor Gray
Write-Host "   HTTP        80     0.0.0.0/0" -ForegroundColor Gray
Write-Host "   HTTPS       443    0.0.0.0/0" -ForegroundColor Gray
Write-Host "   Do NOT open port 5000 - it stays internal." -ForegroundColor Yellow

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "  Setup complete! Run deploy-all.ps1 next." -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
Write-Host ""
