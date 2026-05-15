@echo off
setlocal

set "SINGLE="
set "RELEASE="

:parse_args
if "%~1"=="" goto :args_done
if /I "%~1"=="single" set "SINGLE=-Single" & shift & goto :parse_args
if /I "%~1"=="--single" set "SINGLE=-Single" & shift & goto :parse_args
if /I "%~1"=="release" set "RELEASE=-Release" & shift & goto :parse_args
set "RELEASE=-Config=%~1"
shift
goto :parse_args

:args_done

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-all.ps1" %SINGLE% %RELEASE%
echo.
echo Script finished. Press any key to exit.
pause
