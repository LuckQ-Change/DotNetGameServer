@echo off
setlocal

rem Resolve project root to this script's directory
set "PROJECT_PATH=%~dp0"
if "%PROJECT_PATH:~-1%"=="\" set "PROJECT_PATH=%PROJECT_PATH:~0,-1%"

rem Build configuration (optional first argument), default to Debug
set "CONFIG=Debug"
if not "%~1"=="" set "CONFIG=%~1"

echo Building ServerHost project (%CONFIG%)...
dotnet build "%PROJECT_PATH%\src\ServerHost\ServerHost.csproj" -c %CONFIG%
if errorlevel 1 goto :fail

rem Target framework detection (prefer net9.0, fallback to net8.0)
set "TFM=net9.0"
if exist "%PROJECT_PATH%\src\ServerHost\bin\%CONFIG%\net8.0\ServerHost.dll" set "TFM=net8.0"

set "DLL_PATH=%PROJECT_PATH%\src\ServerHost\bin\%CONFIG%\%TFM%\ServerHost.dll"
echo Launching server instances from %DLL_PATH% ...

rem Ensure Development environment and provide a non-default DIRECTORY_TOKEN_SECRET to satisfy production guard if misconfigured
set DOTNET_ENVIRONMENT=Development
set ASPNETCORE_ENVIRONMENT=Development
if not defined DIRECTORY_TOKEN_SECRET set DIRECTORY_TOKEN_SECRET=local-dev-%RANDOM%-%RANDOM%

rem Use parent environment to set service-specific variables, then start child windows without problematic quote escaping
set SERVICE__TYPE=directory
set SERVICE__PORT=5500
set SERVICE__HEALTHCHECKURL=http://localhost:5101
start "Directory Server" cmd /k dotnet exec "%DLL_PATH%" -- --service=directory --port=5500 --healthCheckUrl=http://localhost:5101

set SERVICE__TYPE=login
set SERVICE__PORT=5501
set SERVICE__HEALTHCHECKURL=http://localhost:5102
start "Login Server"     cmd /k dotnet exec "%DLL_PATH%" -- --service=login     --port=5501 --healthCheckUrl=http://localhost:5102

set SERVICE__TYPE=resource
set SERVICE__PORT=5502
set SERVICE__HEALTHCHECKURL=http://localhost:5103
start "Resource Server"  cmd /k dotnet exec "%DLL_PATH%" -- --service=resource  --port=5502 --healthCheckUrl=http://localhost:5103

set SERVICE__TYPE=battle
set SERVICE__PORT=5503
set SERVICE__HEALTHCHECKURL=http://localhost:5104
start "Battle Server"    cmd /k dotnet exec "%DLL_PATH%" -- --service=battle    --port=5503 --healthCheckUrl=http://localhost:5104

set SERVICE__TYPE=logic
set SERVICE__PORT=5504
set SERVICE__HEALTHCHECKURL=http://localhost:5105
start "Logic Server"     cmd /k dotnet exec "%DLL_PATH%" -- --service=logic     --port=5504 --healthCheckUrl=http://localhost:5105

echo All server instances have been launched in new windows.
goto :eof

:fail
echo Build failed. Aborting.
exit /b 1