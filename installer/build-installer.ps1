<#
.SYNOPSIS
  Publishes ServiceMap self-contained and builds the Windows Installer (.msi).
.DESCRIPTION
  Requires the .NET 8 SDK. Installs the WiX v5 dotnet tool and UI extension if
  missing, publishes the collector and GUI with the runtime bundled, then builds
  a single .msi that installs both, registers/starts the Windows Service, and
  adds a Start Menu shortcut. Output lands in dist\installer\ (gitignored);
  distribute via GitHub Releases, not source control.
.PARAMETER Runtime
  Target RID. Default win-x64 (use win-arm64 for ARM).
.PARAMETER Version
  Product version stamped into the MSI. Default 1.0.0.0.
.PARAMETER SignThumbprint
  Optional SHA1 thumbprint of a code-signing certificate in the current user's
  or machine's certificate store. When provided, the MSI is Authenticode-signed
  with signtool so SmartScreen and enterprise install policies trust it.
.PARAMETER TimestampUrl
  RFC3161 timestamp server used when signing. Default DigiCert.
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Version = "1.6.0.0",
    [string]$SignThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
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
$msiDir = "$dist\installer"
New-Item -ItemType Directory -Force -Path $msiDir | Out-Null
$msi = "$msiDir\Carrier-DependenSee-$Version-$arch.msi"
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

if ($SignThumbprint) {
    Write-Host "==> Signing MSI" -ForegroundColor Cyan
    $signtool = Get-Command signtool -ErrorAction SilentlyContinue
    if (-not $signtool) {
        # Fall back to the newest Windows SDK install.
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending | Select-Object -First 1
    }
    if (-not $signtool) { throw "signtool.exe not found - install the Windows SDK or add signtool to PATH." }
    & $signtool sign /sha1 $SignThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $msi
    Write-Host "Signed with certificate $SignThumbprint" -ForegroundColor Green
}

Write-Host ""
Write-Host "Built: $msi" -ForegroundColor Green
Write-Host "Double-click to install (elevation required). The collector service"
Write-Host "starts automatically; launch Carrier DependenSee from the Start Menu."
