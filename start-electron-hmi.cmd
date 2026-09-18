@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-electron-hmi.ps1"
if errorlevel 1 (
  echo.
  echo Electron HMI startup failed. Press any key to close this window.
  pause >nul
)
