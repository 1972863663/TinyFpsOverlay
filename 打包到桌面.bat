@echo off
setlocal
cd /d "%~dp0"

echo [TinyFpsOverlay] stopping running process...
taskkill /IM TinyFpsOverlay.exe /F >nul 2>nul

echo [TinyFpsOverlay] clean...
dotnet clean
if errorlevel 1 goto :fail

echo [TinyFpsOverlay] build release...
dotnet build -c Release
if errorlevel 1 goto :fail

echo [TinyFpsOverlay] publish win-x64 single file...
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :fail

set "PUBLISH_DIR=%~dp0bin\Release\net8.0-windows\win-x64\publish"
set "DESKTOP_DIR=%USERPROFILE%\Desktop\TinyFpsOverlay"

echo [TinyFpsOverlay] copy publish folder to desktop...
if not exist "%DESKTOP_DIR%" mkdir "%DESKTOP_DIR%"
robocopy "%PUBLISH_DIR%" "%DESKTOP_DIR%" /MIR
if errorlevel 8 goto :fail

echo.
echo Done.
echo Desktop EXE:
echo "%DESKTOP_DIR%\TinyFpsOverlay.exe"
echo.
pause
exit /b 0

:fail
echo.
echo Failed. See output above.
pause
exit /b 1
