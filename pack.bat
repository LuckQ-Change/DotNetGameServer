@echo off
REM 快捷打包：默认 Release + win-x64 + 非自包含
REM 设定控制台代码页为 65001 (UTF-8)，避免中文乱码
chcp 65001 >nul
set SCRIPT_DIR=%~dp0
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%pack.ps1" -Configuration Release -Runtime win-x64 -SelfContained false
pause