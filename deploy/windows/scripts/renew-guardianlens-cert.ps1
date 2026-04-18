#Requires -RunAsAdministrator
# =============================================================================
# Run on the server when win-acme renews the certificate (e.g. from Task Scheduler
# after: wacs.exe --renew). Restarts the proxy so Kestrel reloads the PFX.
#
# Optional: chain from win-acme "Installation" script after you attach it in wacs.
# =============================================================================
$ErrorActionPreference = "Stop"
$wacs = "C:\guardianlens\tools\wacs\wacs.exe"
if (Test-Path $wacs) {
    & $wacs --renew
}
Restart-Service -Name "GuardianLensProxy" -Force
