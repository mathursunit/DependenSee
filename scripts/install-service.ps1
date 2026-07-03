<#
.SYNOPSIS
  Installs the ServiceMap Collector as a Windows Service (LocalSystem).
.DESCRIPTION
  Must be run from an elevated (Administrator) PowerShell prompt.
  Registers the collector with the Service Control Manager and starts it.
  Prefers the self-contained published exe (dist\collector) so no .NET runtime is
  required on the machine; falls back to a framework build under bin\.
.PARAMETER ExePath
  Path to ServiceMap.Collector.exe. If omitted, common locations are probed.
#>
param(
    [string]$ExePath,
    [string]$ServiceName = "CarrierDependenSeeCollector",
    [string]$DisplayName = "Carrier DependenSee Collector"
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator."
    }
}

Assert-Admin

if (-not $ExePath) {
    $candidates = @(
        "$PSScriptRoot\..\dist\collector\CarrierDependenSee.Collector.exe",
        "$PSScriptRoot\CarrierDependenSee.Collector.exe",
        "$PSScriptRoot\..\src\ServiceMap.Collector\bin\Release\net8.0-windows\win-x64\publish\CarrierDependenSee.Collector.exe",
        "$PSScriptRoot\..\src\ServiceMap.Collector\bin\Release\net8.0-windows\CarrierDependenSee.Collector.exe"
    )
    $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $ExePath) {
        throw "Could not find CarrierDependenSee.Collector.exe. Run scripts\publish.ps1 first, or pass -ExePath."
    }
}

$ExePath = (Resolve-Path $ExePath).Path

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists. Stopping and removing it first..."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host "Registering service '$ServiceName' -> $ExePath"
sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto DisplayName= "$DisplayName" obj= "LocalSystem" | Out-Null
sc.exe description $ServiceName "Maps registered services and samples inbound/outbound connections over time." | Out-Null

Write-Host "Starting service..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-Table -AutoSize

Write-Host "Done. Data collects to C:\ProgramData\CarrierDependenSee\servicemap.db"
