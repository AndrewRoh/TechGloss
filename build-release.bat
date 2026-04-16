@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed. Check messages above.
    pause
    exit /b %ERRORLEVEL%
)
pause
