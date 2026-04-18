# Called by win-acme after certificate renewal so Kestrel reloads the PFX.
# Install path on EC2: C:\guardianlens\tools\reload-proxy-after-cert.ps1
$ErrorActionPreference = "Stop"
Restart-Service -Name "GuardianLensProxy" -Force -ErrorAction Stop
