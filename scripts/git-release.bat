@echo off
rem Commit everything, tag, and push. Output: scripts\git-release-log.txt
setlocal
set "LOG=%~dp0git-release-log.txt"
set "TAG=v1.9.0"
cd /d "%~dp0.."

echo === git release %TAG% %DATE% %TIME% === > "%LOG%"
git remote -v >> "%LOG%" 2>&1
git rev-parse --abbrev-ref HEAD >> "%LOG%" 2>&1

git add -A >> "%LOG%" 2>&1
git commit -m "v1.9.0: tests, ETW capture, flow aggregation, remote-scan bursts + conntrack, firewall reconciliation overhaul" -m "- xunit test suite (136 tests) wired into CI" -m "- ETW kernel-network capture of short-lived connections (elevated); polling fallback" -m "- Write-time connection_flows aggregation (schema v4, backfill); split raw/flow retention" -m "- Direction inference via sliding listen-port tracker; shared classifier for remote scans" -m "- Remote scans: multi-sweep bursts per session, Linux conntrack harvest, per-host retention, fleet auto-import, sweep-count coverage metadata in the firewall PDF" -m "- Firewall reconciliation: protocol-aware service matching, safe port-name parsing, traffic-based primary-IP pick, rule zones in grid, multi-file policy load, unresolved-reference surfacing, reverse reconciliation (unused allow rules), filtered CSV export" -m "- Fix: DataGrid DateTime-through-converter rendering (0001-01-01); pre-formatted local time properties" -m "- Installer: output to dist/installer, optional Authenticode signing; manual update check in About" >> "%LOG%" 2>&1
echo COMMIT_EXIT=%errorlevel% >> "%LOG%"

git tag %TAG% >> "%LOG%" 2>&1
echo TAG_EXIT=%errorlevel% >> "%LOG%"

git push origin HEAD >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"

git push origin %TAG% >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git log --oneline -3 >> "%LOG%" 2>&1
echo === done === >> "%LOG%"
