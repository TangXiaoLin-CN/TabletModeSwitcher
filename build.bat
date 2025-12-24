@echo off
chcp 65001 >nul
echo ==========================================
echo   TabletModeSwitcher 打包脚本
echo ==========================================
echo.

REM 检查是否以管理员身份运行
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [警告] 建议以管理员身份运行此脚本
    echo.
)

REM 运行 PowerShell 打包脚本
powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1"

echo.
pause
