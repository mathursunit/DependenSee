@echo off
setlocal
set "LOG=%~dp0git-release-log.txt"
echo === flip private %DATE% %TIME% === > "%LOG%"
where gh >> "%LOG%" 2>&1
if errorlevel 1 (
  echo GH_MISSING >> "%LOG%"
  goto :end
)
gh repo edit mathursunit/DependenSee --visibility private --accept-visibility-change-consequences >> "%LOG%" 2>&1
echo GH_EXIT=%errorlevel% >> "%LOG%"
:end
echo === done === >> "%LOG%"
