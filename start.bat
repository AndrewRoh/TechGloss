@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Startup failed. Check messages above.
    pause
    exit /b %ERRORLEVEL%
)
exit /b 0