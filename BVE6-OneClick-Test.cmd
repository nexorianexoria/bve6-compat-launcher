@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0benchmarks\RunB5OneClick.ps1"
set "code=%errorlevel%"
echo.
if not "%code%"=="0" echo Test did not complete. Review the message above.
echo Press any key to close this window.
pause >nul
exit /b %code%