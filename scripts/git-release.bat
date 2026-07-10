@echo off
rem Commit everything, tag, and push. Output: scripts\git-release-log.txt
setlocal
set "LOG=%~dp0git-release-log.txt"
set "TAG=v1.12.0"
cd /d "%~dp0.."

echo === git release %TAG% %DATE% %TIME% === > "%LOG%"
git remote -v >> "%LOG%" 2>&1
git rev-parse --abbrev-ref HEAD >> "%LOG%" 2>&1

git add -A >> "%LOG%" 2>&1
git commit -m "v1.12.0: analysis + export intelligence (Release A)" -m "- Migration readiness score per machine (window/sweeps/attribution, explained) in Fleet and dossier" -m "- Freeze-drift detection: dependencies first seen in last 7 days (Fleet column, dossier sheet + CSV)" -m "- Risk protocol findings: telnet/FTP/r-services/cleartext LDAP/NetBIOS/internet-exposed listeners" -m "- Fleet-wide workbook: inventory + readiness, cross-dependency list, wave rollup with cross-wave counts" -m "- Dossier now includes dossier.json and cloud-rules/ (AWS SG + Azure NSG Terraform + neutral JSON)" -m "- Headless CLI: export-dossier --machine|--all|--local --hours --out" -m "- 154 tests" >> "%LOG%" 2>&1
echo COMMIT_EXIT=%errorlevel% >> "%LOG%"

git tag %TAG% >> "%LOG%" 2>&1
echo TAG_EXIT=%errorlevel% >> "%LOG%"

git push origin HEAD >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"

git push origin %TAG% >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git log --oneline -3 >> "%LOG%" 2>&1
echo === done === >> "%LOG%"
