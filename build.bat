@echo off
setlocal enabledelayedexpansion
rem ---------------------------------------------------------------------------
rem  Build the Accessible Live Captions app.
rem  Usage:  build.bat [Release|Debug]      (default: Release)
rem  Double-click it in Explorer, or run it from a terminal.
rem ---------------------------------------------------------------------------

rem Run from the folder this script lives in, whatever the current directory is.
cd /d "%~dp0"

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: the .NET SDK ^(dotnet^) was not found on your PATH.
    echo Install it from https://dotnet.microsoft.com/download and try again.
    set "EXITCODE=1"
    goto :end
)

echo Building AccessibleLiveCaptions in %CONFIG% configuration...
echo.
dotnet build "AccessibleLiveCaptions.csproj" --configuration %CONFIG% -nologo
set "EXITCODE=!ERRORLEVEL!"

echo.
if "!EXITCODE!"=="0" (
    echo Build succeeded.
) else (
    echo Build FAILED with exit code !EXITCODE!.
)

:end
rem Pause only when double-clicked (Explorer launches us via "cmd /c ..."), so
rem terminal users aren't nagged. Detect the /c switch without any external tools.
set "CL=!cmdcmdline!"
if not "!CL:/c=!"=="!CL!" pause
exit /b !EXITCODE!
