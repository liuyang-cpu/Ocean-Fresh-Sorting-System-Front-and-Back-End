@echo off
setlocal

cd /d "%~dp0"

if not "%OCEANFRESH_YOLO_PYTHON%"=="" (
  set "PYTHON_EXE=%OCEANFRESH_YOLO_PYTHON%"
) else if exist "%~dp0..\..\..\.venv\Scripts\python.exe" (
  set "PYTHON_EXE=%~dp0..\..\..\.venv\Scripts\python.exe"
) else (
  set "PYTHON_EXE=python"
)

if "%OCEANFRESH_YOLO_HOST%"=="" set "OCEANFRESH_YOLO_HOST=127.0.0.1"
if "%OCEANFRESH_YOLO_PORT%"=="" set "OCEANFRESH_YOLO_PORT=8010"

echo Starting Ocean Fresh YOLO FastAPI service...
echo Python: %PYTHON_EXE%
echo Host: %OCEANFRESH_YOLO_HOST%
echo Port: %OCEANFRESH_YOLO_PORT%

"%PYTHON_EXE%" -m uvicorn app:app --host %OCEANFRESH_YOLO_HOST% --port %OCEANFRESH_YOLO_PORT%
