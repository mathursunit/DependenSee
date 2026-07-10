@echo off
rem Commit everything, tag, and push. Output: scripts\git-release-log.txt
setlocal
set "LOG=%~dp0git-release-log.txt"
set "TAG=v1.10.0"
cd /d "%~dp0.."

echo === git release %TAG% %DATE% %TIME% === > "%LOG%"
git remote -v >> "%LOG%" 2>&1
git rev-parse --abbrev-ref HEAD >> "%LOG%" 2>&1

git add -A >> "%LOG%" 2>&1
git commit -m "v1.10.0: server migration dossier export + reveal-in-Explorer after export" -m "- One-click per-server dossier zip: Excel workbook (ClosedXML) with Overview/Services/Listening/Inbound/Outbound/Cross-dependencies/Firewall reconciliation/Unused rules/Annotations tabs, per-section CSVs, firewall PDF, provenance manifest" -m "- Dossier runs its own policy reconciliation when a policy folder is configured" -m "- Export buttons on History (active machine) and Fleet (selected machine)" -m "- All exports reveal the saved file in Explorer afterwards (Settings toggle, default on)" -m "- 140 tests" >> "%LOG%" 2>&1
echo COMMIT_EXIT=%errorlevel% >> "%LOG%"

git tag %TAG% >> "%LOG%" 2>&1
echo TAG_EXIT=%errorlevel% >> "%LOG%"

git push origin HEAD >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"

git push origin %TAG% >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git log --oneline -3 >> "%LOG%" 2>&1
echo === done === >> "%LOG%"
