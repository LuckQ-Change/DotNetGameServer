@echo off

echo Terminating existing server processes...
taskkill /F /IM ServerHost.exe /T >nul 2>&1

set "PROJECT_PATH=d:\Study and Job\NewServerGame\DotNetGameServer"

echo Building ServerHost project first...
dotnet build "%PROJECT_PATH%\src\ServerHost\ServerHost.csproj"

if %errorlevel% neq 0 (
    echo Build failed. Aborting.
    exit /b %errorlevel%
)

set "DLL_PATH=%PROJECT_PATH%\src\ServerHost\bin\Debug\net8.0\ServerHost.dll"

rem Start server instances using the pre-built DLL
start "Directory Server" cmd /k dotnet exec "%DLL_PATH%" -- --service=directory --port=5500 --healthCheckUrl=http://*:5101
start "Login Server" cmd /k dotnet exec "%DLL_PATH%" -- --service=login --port=5501 --healthCheckUrl=http://*:5102
start "Resource Server" cmd /k dotnet exec "%DLL_PATH%" -- --service=resource --port=5502 --healthCheckUrl=http://*:5103
start "Battle Server" cmd /k dotnet exec "%DLL_PATH%" -- --service=battle --port=5503 --healthCheckUrl=http://*:5104
start "Logic Server" cmd /k dotnet exec "%DLL_PATH%" -- --service=logic --port=5504 --healthCheckUrl=http://*:5105

echo All server instances have been launched in new windows.