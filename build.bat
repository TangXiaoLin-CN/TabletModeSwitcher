@echo off
echo ==========================================
echo   TabletModeSwitcher Build Script
echo ==========================================
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1"

echo.
pause
