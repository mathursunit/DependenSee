@echo off
rem Remove sensitive firewall CSVs from the pushed commit and force-push.
setlocal
set "LOG=%~dp0git-release-log.txt"
cd /d "%~dp0.."

echo === scrub assets/FW %DATE% %TIME% === > "%LOG%"
git rm -r --cached "assets/FW" >> "%LOG%" 2>&1
git rm --cached ".fuse_hidden0000000400000001" >> "%LOG%" 2>&1
git add .gitignore >> "%LOG%" 2>&1
git commit --amend --no-edit >> "%LOG%" 2>&1
echo AMEND_EXIT=%errorlevel% >> "%LOG%"

git tag -f v1.9.0 >> "%LOG%" 2>&1
git push --force origin main >> "%LOG%" 2>&1
echo PUSH_EXIT=%errorlevel% >> "%LOG%"
git push --force origin v1.9.0 >> "%LOG%" 2>&1
echo PUSH_TAG_EXIT=%errorlevel% >> "%LOG%"

git ls-tree -r --name-only HEAD | findstr /i "assets/FW" >> "%LOG%" 2>&1
echo TREE_CHECK_EXIT=%errorlevel% (1 means assets/FW gone) >> "%LOG%"
echo === done === >> "%LOG%"
