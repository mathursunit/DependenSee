@echo off
setlocal enabledelayedexpansion
set "LOG=%~dp0build-log.txt"
rem Ensure a freshly-installed SDK is found even if PATH hasn't refreshed.
set "PATH=%PATH%;C:\Program Files\dotnet"

echo === Carrier DependenSee installer build ===> "%LOG%"
echo Timestamp: %DATE% %TIME%>> "%LOG%"
echo.>> "%LOG%"

echo --- locating dotnet SDK --->> "%LOG%"
where dotnet>> "%LOG%" 2>&1
dotnet --version>> "%LOG%" 2>&1
if errorlevel 1 (
  echo.>> "%LOG%"
  echo RESULT: DOTNET_SDK_MISSING>> "%LOG%"
  echo The .NET 8 SDK was not found. Install it from:>> "%LOG%"
  echo   https://dotnet.microsoft.com/download/dotnet/8.0>> "%LOG%"
  goto :end
)

echo.>> "%LOG%"
echo --- running build-installer.ps1 (this can take a few minutes) --->> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1">> "%LOG%" 2>&1
echo.>> "%LOG%"
echo RESULT: BUILD_EXITCODE=%errorlevel%>> "%LOG%"

:end
echo.>> "%LOG%"
echo === finished ===>> "%LOG%"
