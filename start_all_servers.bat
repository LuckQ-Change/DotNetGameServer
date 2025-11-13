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
rem Align all services with appsettings.json: directory.tokenSecret = "changeme-dev"
set DIRECTORY_TOKEN_SECRET=changeme-dev

rem Auto-detect present servers by project file and start with incremental ports
set BASE_PORT=5500
set HEALTH_BASE=5101

call :try_start "DirectoryServer" directory %BASE_PORT% %HEALTH_BASE%
set /a BASE_PORT+=1
set /a HEALTH_BASE+=1

call :try_start "LoginServer" login %BASE_PORT% %HEALTH_BASE%
set /a BASE_PORT+=1
set /a HEALTH_BASE+=1

call :try_start "BattleServer" battle %BASE_PORT% %HEALTH_BASE%
set /a BASE_PORT+=1
set /a HEALTH_BASE+=1

call :try_start "LogicGameServer" logic %BASE_PORT% %HEALTH_BASE%

echo Auto-detected servers have been launched in new windows.
goto :eof

:try_start
rem %1=ProjectDirName %2=service %3=port %4=healthPort
setlocal
set "_PROJ=%~1"
set "_SERVICE=%~2"
set "_PORT=%~3"
set "_HEALTH=%~4"

set "_CSPROJ=%PROJECT_PATH%\src\%_PROJ%\%_PROJ%.csproj"
if /I "%_PROJ%"=="LogicGameServer" set "_CSPROJ=%PROJECT_PATH%\src\LogicGameServer\LogicServer.csproj"
if /I "%_PROJ%"=="BattleServer" set "_CSPROJ=%PROJECT_PATH%\src\BattleServer\BattleServer.csproj"
if /I "%_PROJ%"=="LoginServer" set "_CSPROJ=%PROJECT_PATH%\src\LoginServer\LoginServer.csproj"
if /I "%_PROJ%"=="DirectoryServer" set "_CSPROJ=%PROJECT_PATH%\src\DirectoryServer\DirectoryServer.csproj"

if exist "%_CSPROJ%" (
  echo Starting %_SERVICE% on port %_PORT% (health http://localhost:%_HEALTH%)
  start "%_SERVICE%" cmd /k dotnet exec "%DLL_PATH%" -- --service=%_SERVICE% --port=%_PORT% --healthCheckUrl=http://localhost:%_HEALTH%
) else (
  echo Skipped %_SERVICE% (project not found: %_CSPROJ%)
)
endlocal & exit /b 0

:fail
echo Build failed. Aborting.
exit /b 1
