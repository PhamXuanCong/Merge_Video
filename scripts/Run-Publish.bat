@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-portable.ps1"

echo.
echo ============================================
echo  Exit code: %ERRORLEVEL%
echo ============================================
pause
