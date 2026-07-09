<#
    reset-database.ps1
    Stops the Carrier DependenSee collector, deletes the accumulated history
    database, and restarts the collector so it begins fresh (with the discovery
    noise filter active). Run it AFTER installing 1.3.2+.

    Just double-click it, or run:
        powershell -ExecutionPolicy Bypass -File .\scripts\reset-database.ps1
    It will prompt once for administrator rights (needed to stop the service).
#>

# Self-elevate: relaunch as administrator if we aren't already.
$isAdmin = ([Security.Principal.WindowsPrincipal]`
    [Security.Principal.WindowsIdentity]::GetCurrent()`
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$ErrorActionPreference = "Stop"
$dir = Join-Path $env:ProgramData "CarrierDependenSee"

# Locate the service by its registered name (fall back to display name).
$svc = Get-Service -Name "CarrierDependenSeeCollector" -ErrorAction SilentlyContinue
if (-not $svc) { $svc = Get-Service -DisplayName "Carrier DependenSee Collector" -ErrorAction SilentlyContinue }

if ($svc) {
    Write-Host "Stopping collector service..."
    Stop-Service $svc.Name -Force -ErrorAction SilentlyContinue
    try { $svc.WaitForStatus('Stopped', '00:00:30') } catch {}
    Start-Sleep -Seconds 2
} else {
    Write-Host "Collector service not found (not installed?) - deleting the database anyway."
}

Write-Host "Deleting database files in $dir ..."
foreach ($f in "servicemap.db", "servicemap.db-wal", "servicemap.db-shm") {
    $p = Join-Path $dir $f
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "  removed $f" }
}

if ($svc) {
    Write-Host "Restarting collector service..."
    Start-Service $svc.Name
}

Write-Host ""
Write-Host "Done. A fresh, empty database will be created on the next sweep." -ForegroundColor Green
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
