@echo off
set "LOG=%~dp0install-sdk-log.txt"
echo === install .NET 8 SDK via winget ===> "%LOG%"
echo Timestamp: %DATE% %TIME%>> "%LOG%"
echo.>> "%LOG%"
where winget>> "%LOG%" 2>&1
if errorlevel 1 (
  echo RESULT: WINGET_MISSING>> "%LOG%"
  echo winget is not available. Install the .NET 8 SDK manually from:>> "%LOG%"
  echo   https://dotnet.microsoft.com/download/dotnet/8.0>> "%LOG%"
  goto :end
)
echo --- running winget install (approve the UAC prompt) --->> "%LOG%"
winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements>> "%LOG%" 2>&1
echo.>> "%LOG%"
echo RESULT: WINGET_EXITCODE=%errorlevel%>> "%LOG%"
:end
echo.>> "%LOG%"
echo === finished ===>> "%LOG%"
