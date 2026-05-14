@echo off
setlocal EnableExtensions

set "PROJECT_PATH=%~dp0"
if "%PROJECT_PATH:~-1%"=="\" set "PROJECT_PATH=%PROJECT_PATH:~0,-1%"

set "CONFIG=Debug"
set "SINGLE_WINDOW=0"

set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_ENVIRONMENT=Development"
set "DIRECTORY_TOKEN_SECRET=dev-secret"

:parse_args
if "%~1"=="" goto :args_done
if /I "%~1"=="single" (
  set "SINGLE_WINDOW=1"
  shift
  goto :parse_args
)
if /I "%~1"=="--single" (
  set "SINGLE_WINDOW=1"
  shift
  goto :parse_args
)
if /I "%~1"=="debug" (
  set "CONFIG=Debug"
  shift
  goto :parse_args
)
if /I "%~1"=="release" (
  set "CONFIG=Release"
  shift
  goto :parse_args
)
set "CONFIG=%~1"
shift
goto :parse_args

:args_done
echo Building ServerHost project (%CONFIG%)...
dotnet build "%PROJECT_PATH%\src\ServerHost\ServerHost.csproj" -c "%CONFIG%"
if errorlevel 1 goto :fail

set "TFM=net8.0"
if exist "%PROJECT_PATH%\src\ServerHost\bin\%CONFIG%\net9.0\ServerHost.dll" set "TFM=net9.0"
set "DLL_PATH=%PROJECT_PATH%\src\ServerHost\bin\%CONFIG%\%TFM%\ServerHost.dll"

if not exist "%DLL_PATH%" (
  echo ServerHost output not found: %DLL_PATH%
  goto :fail
)

if "%SINGLE_WINDOW%"=="1" goto :launch_single

echo Launching server instances from %DLL_PATH% ...
set BASE_PORT=5500
set HEALTH_BASE=5101

call :try_start "DirectoryServer" "directory" %BASE_PORT% %HEALTH_BASE%
set /a BASE_PORT+=1
set /a HEALTH_BASE+=1

call :try_start "LoginServer" "login" %BASE_PORT% %HEALTH_BASE%
set /a BASE_PORT+=1
set /a HEALTH_BASE+=1

call :try_start "BattleServer" "battle" %BASE_PORT% %HEALTH_BASE%
set /a BASE_PORT+=1
set /a HEALTH_BASE+=1

call :try_start "LogicGameServer" "logic" %BASE_PORT% %HEALTH_BASE%

echo All server windows have been launched.
goto :eof

:launch_single
echo Launching all services in one window...
dotnet exec "%DLL_PATH%" -- --service all
goto :eof

:try_start
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
  echo Starting %_SERVICE% on port %_PORT% with health http://localhost:%_HEALTH%
  start "%_SERVICE%" cmd /k dotnet exec "%DLL_PATH%" -- --service %_SERVICE% --port %_PORT% --healthCheckUrl http://localhost:%_HEALTH%
) else (
  echo Skipped %_SERVICE% because project was not found: %_CSPROJ%
)

endlocal & exit /b 0

:fail
echo Build failed. Aborting.
exit /b 1
