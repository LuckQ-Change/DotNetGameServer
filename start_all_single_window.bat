@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-all.ps1" -Single %*
echo.
echo Script finished. Press any key to exit.
pause
