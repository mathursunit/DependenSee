@echo off
rem Commit everything, tag, and push. Output: scripts\git-release-log.txt
setlocal
set "LOG=%~dp0git-release-log.txt"
set "TAG=v1.13.0"
cd /d "%~dp0.."

echo === git release %TAG% %DATE% %TIME% === > "%LOG%"
git remote -v >> "%LOG%" 2>&1
git rev-parse --abbrev-ref HEAD >> "%LOG%" 2>&1

git add -A >> "%LOG%" 2>&1
git commit -m "v1.13.0: deeper data gathering (Release B)" -m "- DNS-Client ETW capture, folded to distinct name<->IP per process (schema v5)" -m "- Resource-utilization sampling (CPU/mem/disk/net) with p95/peak rollup for right-sizing" -m "- Config scavenging: hardcoded endpoints from app.config/appsettings/.env, reconciled vs traffic; passwords always masked, opt-in raw" -m "- Identity/auth dependency mapping (Kerberos/LDAP/GC/DC) + non-builtin service accounts" -m "- Baseline save + diff (missing/new/unchanged) for pre/post-cutover validation" -m "- All surfaced in the dossier (sheets + CSVs + dossier.json); split retention extended to new tables" -m "- 167 tests" >> "%LOG%" 2>&1
echo COMMIT_EXIT=%errorlevel% >> "%LOG%"

git tag %TAG% >> "%LOG%" 2>&1
echo TAG_EXIT=%errorlevel% >> "%LOG%"

git push origin HEAD >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"

git push origin %TAG% >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git log --oneline -3 >> "%LOG%" 2>&1
echo === done === >> "%LOG%"
