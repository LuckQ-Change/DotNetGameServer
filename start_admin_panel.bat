@echo off
setlocal enabledelayedexpansion

rem Build & launch AdminPanel independently and open management homepage
rem Usage: start_admin_panel.bat [Debug|Release] [PORT]

set "PROJECT_PATH=%~dp0"
if "%PROJECT_PATH:~-1%"=="\" set "PROJECT_PATH=%PROJECT_PATH:~0,-1%"

set "CONFIG=%1"
if not defined CONFIG set "CONFIG=Debug"

set "PORT=%2"
if not defined PORT set "PORT=5600"

echo Building AdminPanel project (%CONFIG%)...
dotnet build "%PROJECT_PATH%\src\AdminPanel\AdminPanel.csproj" -c %CONFIG%
if %errorlevel% neq 0 (
    echo Build failed. Aborting.
    exit /b %errorlevel%
)

rem Prefer net9.0, fallback net8.0
set "ADMIN_TFM=net9.0"
if exist "%PROJECT_PATH%\src\AdminPanel\bin\%CONFIG%\net9.0\AdminPanel.dll" (
    set "ADMIN_TFM=net9.0"
) else if exist "%PROJECT_PATH%\src\AdminPanel\bin\%CONFIG%\net8.0\AdminPanel.dll" (
    set "ADMIN_TFM=net8.0"
)

set "ASPNETCORE_ENVIRONMENT=Development"
set "ADMIN_DLL_PATH=%PROJECT_PATH%\src\AdminPanel\bin\%CONFIG%\%ADMIN_TFM%\AdminPanel.dll"
echo Launching AdminPanel from %ADMIN_DLL_PATH% on http://localhost:%PORT% ...
start "Admin Panel" cmd /k dotnet exec "%ADMIN_DLL_PATH%" --urls=http://localhost:%PORT%

timeout /t 2 /nobreak >nul
start "" "http://localhost:%PORT%/monitoring/status.html"

echo AdminPanel has been launched in a new window. Management page opened.
endlocal