@echo off
rem One-click build of the v1.10.0.0 MSI with full logging.
rem Output: dist\installer\Carrier-DependenSee-1.10.0.0-x64.msi
setlocal
set "LOG=%~dp0build-log.txt"
set "PATH=%PATH%;C:\Program Files\dotnet"

echo === Carrier DependenSee installer build (1.10.0.0) ===> "%LOG%"
echo Timestamp: %DATE% %TIME%>> "%LOG%"
echo.>> "%LOG%"

echo --- locating dotnet SDK --->> "%LOG%"
where dotnet>> "%LOG%" 2>&1
dotnet --version>> "%LOG%" 2>&1
if errorlevel 1 (
  echo.>> "%LOG%"
  echo RESULT: DOTNET_SDK_MISSING>> "%LOG%"
  goto :end
)

echo.>> "%LOG%"
echo --- running build-installer.ps1 -Version 1.10.0.0 --->> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Version 1.10.0.0>> "%LOG%" 2>&1
echo RESULT: BUILD_EXITCODE=%errorlevel%>> "%LOG%"

:end
echo === finished ===>> "%LOG%"
