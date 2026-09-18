@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "PYTHON_EXE=%CD%\.venv\Scripts\python.exe"
set "HMI_EXE=%CD%\src\OceanFresh.SortingSystem.HMI\bin\Release\net8.0-windows\OceanFresh.SortingSystem.HMI.exe"
set "API_EXE=%CD%\src\OceanFresh.SortingSystem.LocalApi\bin\Release\net8.0\OceanFresh.SortingSystem.LocalApi.exe"

if not exist "%PYTHON_EXE%" (
  echo ERROR: Python environment is not ready.
  echo Run setup-environment.cmd once before starting the application.
  pause
  exit /b 1
)

if not exist "%HMI_EXE%" (
  echo ERROR: The application has not been built.
  echo Run setup-environment.cmd once before starting the application.
  pause
  exit /b 1
)

if not exist "%API_EXE%" (
  echo ERROR: LocalApi has not been built.
  echo Run setup-environment.cmd once before starting the application.
  pause
  exit /b 1
)

set "OCEANFRESH_YOLO_PYTHON=%PYTHON_EXE%"
echo Starting Ocean Fresh Sorting System...
echo The application will start LocalApi and the YOLO service automatically.
start "" "%HMI_EXE%"
exit /b 0
