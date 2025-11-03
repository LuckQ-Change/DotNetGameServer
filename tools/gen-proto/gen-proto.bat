@echo off
REM 快速生成 Proto C# 代码的批处理封装
REM 设定控制台代码页为 65001 (UTF-8)，避免中文乱码
chcp 65001 >nul

set SCRIPT_DIR=%~dp0
REM 允许传递参数给 PowerShell 脚本，例如: gen-proto.bat -IncludeGrpc
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%gen-proto.ps1" %*