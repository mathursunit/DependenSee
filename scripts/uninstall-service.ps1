<#
.SYNOPSIS
  Stops and removes the ServiceMap Collector Windows Service.
  Run from an elevated (Administrator) PowerShell prompt.
#>
param(
    [string]$ServiceName = "CarrierDependenSeeCollector"
)

$ErrorActionPreference = "Stop"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run as Administrator."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed."
    return
}

if ($existing.Status -ne 'Stopped') {
    Write-Host "Stopping '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
}

sc.exe delete $ServiceName | Out-Null
Write-Host "Removed service '$ServiceName'. (Collected data under C:\ProgramData\CarrierDependenSee is left in place.)"
