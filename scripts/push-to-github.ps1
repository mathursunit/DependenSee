<#
    push-to-github.ps1
    One-time (and repeatable) push of Carrier DependenSee to GitHub.

    Run this ON YOUR WINDOWS MACHINE (not the Claude sandbox) so it uses the
    GitHub credentials already stored in Windows Credential Manager:

        cd D:\Projects2026\CarrierMigrationMappingTool
        powershell -ExecutionPolicy Bypass -File .\scripts\push-to-github.ps1

    Re-running it later just commits and pushes any new changes.
#>

$ErrorActionPreference = "Stop"

# Move to the project root (this script lives in .\scripts)
Set-Location (Join-Path $PSScriptRoot "..")

$RepoUrl = "https://github.com/mathursunit/DependenSee.git"

# Initialise the repo on first run
if (-not (Test-Path ".git")) {
    git init | Out-Null
    git branch -M main
}

git config user.name  "Sunit Mathur"
git config user.email "sunit.mathur@gmail.com"

# Point 'origin' at the repo (add or update)
if (git remote | Select-String -Pattern '^origin$' -Quiet) {
    git remote set-url origin $RepoUrl
} else {
    git remote add origin $RepoUrl
}

git add -A

# Commit only if there is something to commit
if (git diff --cached --quiet) {
    Write-Host "Nothing new to commit."
} else {
    $msg = "Carrier DependenSee 1.3.0 - service-name resolution, Excel-style column filters, filter/export overhaul"
    git commit -m $msg
}

# Push (first push sets upstream). Windows Credential Manager handles auth.
git push -u origin main

Write-Host ""
Write-Host "Done. View it at https://github.com/mathursunit/DependenSee" -ForegroundColor Green
