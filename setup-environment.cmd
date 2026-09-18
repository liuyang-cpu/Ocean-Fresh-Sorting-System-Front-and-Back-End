@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem Keep large installer caches on the same drive as the repository.
rem This prevents pip and NuGet from silently filling the Windows system drive.
set "OCEANFRESH_SETUP_CACHE=%CD%\.setup-cache"
set "TEMP=%OCEANFRESH_SETUP_CACHE%\temp"
set "TMP=%TEMP%"
set "PIP_CACHE_DIR=%OCEANFRESH_SETUP_CACHE%\pip"
set "NUGET_PACKAGES=%OCEANFRESH_SETUP_CACHE%\nuget"
if not exist "%TEMP%" mkdir "%TEMP%"
if not exist "%PIP_CACHE_DIR%" mkdir "%PIP_CACHE_DIR%"
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%"

echo [0/6] Checking available disk space...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot((Get-Location).Path)); if ($drive.AvailableFreeSpace -lt 5GB) { Write-Host ('ERROR: At least 5 GB of free space is required on ' + $drive.Name + ' Current free space: ' + [math]::Round($drive.AvailableFreeSpace / 1GB, 2) + ' GB.'); exit 1 }"
if errorlevel 1 (
  pause
  exit /b 1
)

echo [1/6] Checking .NET SDK...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 8 SDK x64 is not installed or is not in PATH.
  echo Install .NET 8 SDK, then run this file again.
  pause
  exit /b 1
)

dotnet --version | findstr /b "8." >nul
if errorlevel 1 (
  echo ERROR: This project requires .NET 8 SDK.
  dotnet --version
  pause
  exit /b 1
)

echo [2/6] Preparing Python 3.11 virtual environment...
if not exist ".venv\Scripts\python.exe" (
  where py >nul 2>&1
  if not errorlevel 1 (
    py -3.11 -m venv .venv
  ) else (
    where python >nul 2>&1
    if errorlevel 1 (
      echo ERROR: Python 3.11 x64 is not installed or is not in PATH.
      pause
      exit /b 1
    )
    python -m venv .venv
  )
)

if not exist ".venv\Scripts\python.exe" (
  echo ERROR: Failed to create .venv.
  pause
  exit /b 1
)

set "PYTHON_EXE=%CD%\.venv\Scripts\python.exe"

"%PYTHON_EXE%" -c "import struct,sys; raise SystemExit(0 if sys.version_info[:2] == (3,11) and struct.calcsize('P') * 8 == 64 else 1)"
if errorlevel 1 (
  echo ERROR: Python must be version 3.11 x64.
  "%PYTHON_EXE%" --version
  pause
  exit /b 1
)

echo [3/6] Updating pip...
"%PYTHON_EXE%" -m pip install --upgrade pip
if errorlevel 1 goto :install_failed

echo [4/6] Installing PyTorch and YOLO dependencies...
if /I "%~1"=="cpu" goto :install_cpu
where nvidia-smi >nul 2>&1
if errorlevel 1 goto :install_cpu

echo NVIDIA driver detected. Installing CUDA 11.8 PyTorch packages...
"%PYTHON_EXE%" -m pip install torch==2.5.0 torchvision==0.20.0 --index-url https://download.pytorch.org/whl/cu118
if errorlevel 1 goto :install_failed
goto :install_common

:install_cpu
echo Installing CPU PyTorch packages...
"%PYTHON_EXE%" -m pip install torch==2.5.0 torchvision==0.20.0 --index-url https://download.pytorch.org/whl/cpu
if errorlevel 1 goto :install_failed

:install_common
"%PYTHON_EXE%" -m pip install -r "predict\youge\service\requirements.txt"
if errorlevel 1 goto :install_failed

echo [5/6] Restoring .NET packages...
dotnet restore "OceanFresh.SortingSystem.sln"
if errorlevel 1 goto :build_failed

echo [6/6] Building the application...
dotnet build "OceanFresh.SortingSystem.sln" -c Release --no-restore
if errorlevel 1 goto :build_failed

echo.
echo Environment setup completed successfully.
echo You can now double-click start-oceanfresh.cmd.
pause
exit /b 0

:install_failed
echo.
echo ERROR: Python dependency installation failed.
echo Check the internet connection and the error message above.
pause
exit /b 1

:build_failed
echo.
echo ERROR: .NET restore or build failed.
echo Check the internet connection and the error message above.
pause
exit /b 1
