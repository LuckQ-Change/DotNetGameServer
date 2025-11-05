@echo off
setlocal ENABLEEXTENSIONS

REM Minimal ASCII-only starter to avoid codepage issues
echo [INFO] Checking Node.js...
set "NODE_EXE=node"
where node >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\nodejs\node.exe" set "NODE_EXE=%ProgramFiles%\nodejs\node.exe"
  if exist "%ProgramFiles(x86)%\nodejs\node.exe" set "NODE_EXE=%ProgramFiles(x86)%\nodejs\node.exe"
)
if "%NODE_EXE%"=="node" (
  where node >nul 2>&1 || (
    echo [ERROR] Node.js not found. Install: https://nodejs.org/
    exit /b 1
  )
)

REM Select npm command (fallback to typical install paths)
set "NPM_CMD=npm.cmd"
where npm >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\nodejs\npm.cmd" set "NPM_CMD=%ProgramFiles%\nodejs\npm.cmd"
  if exist "%ProgramFiles(x86)%\nodejs\npm.cmd" set "NPM_CMD=%ProgramFiles(x86)%\nodejs\npm.cmd"
)

REM Compute directories
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "ROOT_DIR=%SCRIPT_DIR%\.."

REM Install dependencies at repo root (package.json at root)
if not exist "%ROOT_DIR%\node_modules" (
  echo [INFO] Installing dependencies at repo root...
  pushd "%ROOT_DIR%" >nul
  call "%NPM_CMD%" install || (
    echo [ERROR] npm install failed at repo root.
    popd >nul
    exit /b 1
  )
  popd >nul
)

REM Run review CLI from repo root so outputs land under root
echo [INFO] Running AI code review...
pushd "%ROOT_DIR%" >nul
"%NODE_EXE%" "%SCRIPT_DIR%\ci-review.js"
set ERR=%ERRORLEVEL%
popd >nul
if not "%ERR%"=="0" (
  echo [ERROR] Review failed with exit code %ERR%
  exit /b %ERR%
)

REM Open reviews folder under repo root if exists
if exist "%ROOT_DIR%\reviews" (
  echo [INFO] Opening reviews directory...
  start "" explorer "%ROOT_DIR%\reviews"
)

echo [OK] Done.
exit /b 0