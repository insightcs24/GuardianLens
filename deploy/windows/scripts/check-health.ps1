# =============================================================================
# GuardianLens - Health Check & Diagnostics (run ON EC2)
# =============================================================================

Write-Host ""
Write-Host "GuardianLens - Health Check" -ForegroundColor Cyan
Write-Host "===========================" -ForegroundColor Cyan
Write-Host ""

$checks = @()

# --- Check 1: Windows Services ---
Write-Host "> Windows Services:" -ForegroundColor Yellow
$services = @("GuardianLensAPI", "GuardianLensProxy")
foreach ($svc in $services) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($s) {
        $color = if ($s.Status -eq "Running") { "Green" } else { "Red" }
        Write-Host "   $($s.DisplayName): $($s.Status)" -ForegroundColor $color
        $checks += @{ Test = $s.DisplayName; Pass = ($s.Status -eq "Running") }
    } else {
        Write-Host "   $svc: NOT INSTALLED" -ForegroundColor Red
        $checks += @{ Test = $svc; Pass = $false }
    }
}

# --- Check 2: Port 5000 (.NET API) ---
Write-Host ""
Write-Host "> Port listeners:" -ForegroundColor Yellow
$port5000 = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
$port80   = Get-NetTCPConnection -LocalPort 80   -State Listen -ErrorAction SilentlyContinue

Write-Host "   Port 5000 (.NET API):   $(if ($port5000) { 'Listening OK' } else { 'Not listening' })" -ForegroundColor $(if ($port5000) { "Green" } else { "Red" })
Write-Host "   Port 80  (YARP Proxy):  $(if ($port80)   { 'Listening OK' } else { 'Not listening' })" -ForegroundColor $(if ($port80) { "Green" } else { "Red" })

# --- Check 3: API health endpoint ---
Write-Host ""
Write-Host "> API endpoints:" -ForegroundColor Yellow
$endpoints = @(
    @{ Url = "http://localhost:5000/api/assets/dashboard"; Label = "API direct (port 5000)" },
    @{ Url = "http://localhost/api/assets/dashboard";      Label = "API via YARP (port 80)" },
    @{ Url = "http://localhost/swagger";                   Label = "Swagger UI via YARP" },
    @{ Url = "http://localhost/";                          Label = "React frontend" }
)
foreach ($ep in $endpoints) {
    try {
        $r = Invoke-WebRequest -Uri $ep.Url -UseBasicParsing -TimeoutSec 5
        Write-Host "   $($ep.Label): HTTP $($r.StatusCode) OK" -ForegroundColor Green
        $checks += @{ Test = $ep.Label; Pass = $true }
    } catch {
        Write-Host "   $($ep.Label): Failed - $($_.Exception.Message)" -ForegroundColor Red
        $checks += @{ Test = $ep.Label; Pass = $false }
    }
}

# --- Check 3b: Blockchain status (Live vs Simulation) ---
Write-Host ""
Write-Host "> Blockchain (API):" -ForegroundColor Yellow
try {
    $bc = Invoke-RestMethod -Uri "http://localhost:5000/api/blockchain/status" -TimeoutSec 5
    $mode = $bc.mode
    $configured = $bc.isConfigured
    $net = $bc.network
    $live = ($configured -eq $true -and $mode -eq "Live")
    Write-Host "   mode=$mode  isConfigured=$configured  network=$net" -ForegroundColor $(if ($live) { "Green" } else { "White" })
    if (-not $live) {
        Write-Host "   (Simulation until Blockchain__PrivateKey + Blockchain__ContractAddress are set - see blockchain/README.txt)" -ForegroundColor Yellow
    }
    $checks += @{ Test = "Blockchain /api/blockchain/status"; Pass = $true }
} catch {
    Write-Host "   /api/blockchain/status failed - $($_.Exception.Message)" -ForegroundColor Red
    $checks += @{ Test = "Blockchain /api/blockchain/status"; Pass = $false }
}

# --- Check 4: Database file ---
Write-Host ""
Write-Host "> Database:" -ForegroundColor Yellow
$dbPath = "C:\guardianlens\data\guardianlens.db"
if (Test-Path $dbPath) {
    $dbSize = (Get-Item $dbPath).Length
    Write-Host "   Database exists: $dbPath ($([math]::Round($dbSize/1KB, 1)) KB)" -ForegroundColor Green
} else {
    Write-Host "   Database NOT found at $dbPath" -ForegroundColor Yellow
    Write-Host "   (Will be created on first API request)" -ForegroundColor White
}

# --- Check 5: Disk space ---
Write-Host ""
Write-Host "> Disk space:" -ForegroundColor Yellow
$disk = Get-PSDrive C
$freeGB = [math]::Round($disk.Free / 1GB, 1)
$usedGB = [math]::Round($disk.Used / 1GB, 1)
$color = if ($freeGB -lt 2) { "Red" } elseif ($freeGB -lt 5) { "Yellow" } else { "Green" }
Write-Host "   C: drive - Used: ${usedGB}GB, Free: ${freeGB}GB" -ForegroundColor $color

# --- Recent logs ---
Write-Host ""
Write-Host "> Recent Windows Event Log entries:" -ForegroundColor Yellow
Get-EventLog -LogName Application -Source "GuardianLens*" -Newest 5 -ErrorAction SilentlyContinue |
    Select-Object TimeGenerated, EntryType, Message |
    Format-Table -AutoSize -Wrap

# --- Summary ---
Write-Host ""
$passed = ($checks | Where-Object { $_.Pass }).Count
$total  = $checks.Count
$allOk  = $passed -eq $total
Write-Host "Summary: $passed/$total checks passed" -ForegroundColor $(if ($allOk) { "Green" } else { "Yellow" })
if ($allOk) {
    Write-Host "All systems operational!" -ForegroundColor Green
} else {
    Write-Host "Some checks failed - review output above" -ForegroundColor Yellow
}
Write-Host ""
