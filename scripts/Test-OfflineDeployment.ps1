param(
    [Parameter(Mandatory = $false)]
    [string]$DeploymentRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($DeploymentRoot)
$python = Join-Path $root "PythonRuntime\python.exe"
$bridge = Join-Path $root "HardwareBridge\TechikDetectorBridge.exe"
$detectorSdk = Join-Path $root "DetectorSdk"
$techikRuntime = Join-Path $root "TechikRuntime"
$model = Join-Path $root "YoloService\models\oil_clam\v1\best.pt"

$requiredFiles = @(
    $python,
    $bridge,
    (Join-Path $detectorSdk "tk_driver_dt.dll"),
    (Join-Path $techikRuntime "plugin\tk_driver_xray.dll"),
    (Join-Path $techikRuntime "plugin\tk_driver_io.dll"),
    (Join-Path $techikRuntime "plugin\tk_driver_motion.dll"),
    $model,
    (Join-Path $root "OceanFresh.SortingSystem.HMI.exe"),
    (Join-Path $root "LocalApi\OceanFresh.SortingSystem.LocalApi.exe")
)

$missing = @($requiredFiles | Where-Object { !(Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw "Deployment is incomplete. Missing:`n$($missing -join "`n")"
}

Write-Host "[1/3] Required deployment files: OK"

$env:PYTHONUTF8 = "1"
$env:YOLO_CONFIG_DIR = Join-Path $root "Data\ultralytics"
$pythonCheck = @'
import torch, torchvision, ultralytics, fastapi, uvicorn, cv2
gpu_name = torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU fallback only'
print('python_runtime=ok')
print(f'torch={torch.__version__}')
print(f'torchvision={torchvision.__version__}')
print(f'ultralytics={ultralytics.__version__}')
print(f'cuda_build={torch.version.cuda}')
print(f'cuda_available={torch.cuda.is_available()}')
print(f'gpu_name={gpu_name}')
'@
& $python -c $pythonCheck
if ($LASTEXITCODE -ne 0) {
    throw "Python/YOLO runtime validation failed."
}
Write-Host "[2/3] Python and YOLO imports: OK"

& $bridge --sdk-root $detectorSdk --runtime-root $techikRuntime --run-peripherals
if ($LASTEXITCODE -ne 0) {
    throw "Captured Techik plugin inspection failed."
}
Write-Host "[3/3] Captured Techik plugins: OK"
Write-Host "Offline deployment validation passed. No physical hardware output was enabled."
