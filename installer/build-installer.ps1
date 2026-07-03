<#
.SYNOPSIS
  Publishes ServiceMap self-contained and builds the Windows Installer (.msi).
.DESCRIPTION
  Requires the .NET 8 SDK. Installs the WiX v5 dotnet tool and UI extension if
  missing, publishes the collector and GUI with the runtime bundled, then builds
  a single .msi that installs both, registers/starts the Windows Service, and
  adds a Start Menu shortcut.
.PARAMETER Runtime
  Target RID. Default win-x64 (use win-arm64 for ARM).
.PARAMETER Version
  Product version stamped into the MSI. Default 1.0.0.0.
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Version = "1.3.0.0"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path "$PSScriptRoot\.."
$dist = "$repo\dist"
$arch = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64" }

Write-Host "==> Ensuring WiX v5 tool is installed" -ForegroundColor Cyan
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    dotnet tool install --global wix --version 5.0.2
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}
wix extension add -g WixToolset.UI.wixext/5.0.2 2>$null | Out-Null

Write-Host "==> Generating branding icon (if logo present)" -ForegroundColor Cyan
& "$repo\scripts\make-icon.ps1"

Write-Host "==> Publishing collector + GUI (self-contained $Runtime)" -ForegroundColor Cyan
$pub = @("-c","Release","-r",$Runtime,"--self-contained","true",
         "-p:PublishSingleFile=true","-p:DebugType=none","-p:DebugSymbols=false")
dotnet publish "$repo\src\ServiceMap.Collector\ServiceMap.Collector.csproj" @pub -o "$dist\collector"
dotnet publish "$repo\src\ServiceMap.App\ServiceMap.App.csproj"             @pub -o "$dist\app"

Write-Host "==> Building MSI" -ForegroundColor Cyan
$msi = "$repo\Carrier-DependenSee-$Version-$arch.msi"
Push-Location $PSScriptRoot
try {
    $logoIco = "$repo\assets\DependenSee.ico"
    $logoArg = if (Test-Path $logoIco) { @("-d", "LogoIco=$logoIco") } else { @() }
    wix build "CarrierDependenSee.wxs" `
        -arch $arch `
        -ext WixToolset.UI.wixext `
        -b "$PSScriptRoot" `
        -d SrcDist="$dist" `
        -d Version="$Version" `
        @logoArg `
        -o $msi
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Built: $msi" -ForegroundColor Green
Write-Host "Double-click to install (elevation required). The collector service"
Write-Host "starts automatically; launch Carrier DependenSee from the Start Menu."
