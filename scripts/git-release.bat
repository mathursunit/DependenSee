@echo off
rem Commit everything, tag, and push. Output: scripts\git-release-log.txt
setlocal
set "LOG=%~dp0git-release-log.txt"
set "TAG=v1.15.0"
cd /d "%~dp0.."

echo === git release %TAG% %DATE% %TIME% === > "%LOG%"
git remote -v >> "%LOG%" 2>&1
git rev-parse --abbrev-ref HEAD >> "%LOG%" 2>&1

git add -A >> "%LOG%" 2>&1
git commit -m "v1.15.0: sidebar nav, drill-in breadcrumb, retire Map/Annotations, installer location prompt" -m "- Left sidebar of sections (TabStripPlacement=Left) replacing the top tab strip; Machine dropdown + LIVE/SNAPSHOT badges kept" -m "- Drill-in: View this server lands on the machine Dashboard, snapshot-framed with a Back-to-Fleet breadcrumb" -m "- Retired the Map and Annotations views" -m "- Installer: WixUI_Mondo so Custom setup offers feature selection AND an install-location Browse" -m "- 170 tests" >> "%LOG%" 2>&1
echo COMMIT_EXIT=%errorlevel% >> "%LOG%"

git tag %TAG% >> "%LOG%" 2>&1
echo TAG_EXIT=%errorlevel% >> "%LOG%"

git push origin HEAD >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"

git push origin %TAG% >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git log --oneline -3 >> "%LOG%" 2>&1
echo === done === >> "%LOG%"
