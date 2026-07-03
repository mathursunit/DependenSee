<#
.SYNOPSIS
  Publishes the collector and GUI as self-contained, single-file Windows
  executables with the .NET 8 runtime bundled in. No .NET install is required on
  the target machine.
.PARAMETER Runtime
  Target runtime identifier. Default win-x64. Use win-arm64 for ARM machines.
.PARAMETER OutDir
  Output root. Default: <repo>\dist
.NOTES
  Requires only the .NET 8 SDK on the BUILD machine (not the target).
#>
param(
    [string]$Runtime = "win-x64",
    [string]$OutDir  = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path "$PSScriptRoot\.."

$common = @(
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true"
)

Write-Host "Publishing Collector (self-contained, $Runtime)..." -ForegroundColor Cyan
dotnet publish "$repo\src\ServiceMap.Collector\ServiceMap.Collector.csproj" `
    @common -o "$OutDir\collector"

Write-Host "Publishing GUI (self-contained, $Runtime)..." -ForegroundColor Cyan
dotnet publish "$repo\src\ServiceMap.App\ServiceMap.App.csproj" `
    @common -o "$OutDir\app"

# Copy the service scripts next to the collector so the GUI's install button
# can find them relative to the exe.
Copy-Item "$PSScriptRoot\install-service.ps1"   "$OutDir\collector\" -Force
Copy-Item "$PSScriptRoot\uninstall-service.ps1" "$OutDir\collector\" -Force

Write-Host ""
Write-Host "Done. Bundled executables (no .NET install needed on target):" -ForegroundColor Green
Write-Host "  Collector : $OutDir\collector\ServiceMap.Collector.exe"
Write-Host "  GUI       : $OutDir\app\ServiceMap.App.exe"
