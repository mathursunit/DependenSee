@echo off
rem Commit everything, tag, and push. Output: scripts\git-release-log.txt
setlocal
set "LOG=%~dp0git-release-log.txt"
set "TAG=v1.14.0"
cd /d "%~dp0.."

echo === git release %TAG% %DATE% %TIME% === > "%LOG%"
git remote -v >> "%LOG%" 2>&1
git rev-parse --abbrev-ref HEAD >> "%LOG%" 2>&1

git add -A >> "%LOG%" 2>&1
git commit -m "v1.14.0: local/remote separation (Release C)" -m "- Installer split into Collector and Console features (ADDLOCAL=CollectorFeature|ConsoleFeature); default installs both; feature-tree UI" -m "- Console mode: when no local collector is present (or --console), This-Machine tabs hide and the app opens on Fleet with a blue banner" -m "- LIVE (green) / SNAPSHOT (amber) badges in the header; console banner distinct" -m "- Portable projects: machine DB paths stored relative to the workspace folder so a project can move to a share/USB and still resolve" -m "- 170 tests" >> "%LOG%" 2>&1
echo COMMIT_EXIT=%errorlevel% >> "%LOG%"

git tag %TAG% >> "%LOG%" 2>&1
echo TAG_EXIT=%errorlevel% >> "%LOG%"

git push origin HEAD >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"

git push origin %TAG% >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git log --oneline -3 >> "%LOG%" 2>&1
echo === done === >> "%LOG%"
