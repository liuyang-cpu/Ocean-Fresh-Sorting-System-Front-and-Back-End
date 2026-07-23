param(
    [Parameter(Mandatory = $false)]
    [string]$DeploymentRoot
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($DeploymentRoot)) {
    $DeploymentRoot = $PSScriptRoot
}
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

Write-Host "[1/4] Required deployment files: OK"

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
Write-Host "[2/4] Python and YOLO imports: OK"

& $bridge --sdk-root $detectorSdk --runtime-root $techikRuntime --run-peripherals
if ($LASTEXITCODE -ne 0) {
    throw "Captured Techik plugin inspection failed."
}
Write-Host "[3/4] Captured Techik plugins: OK"

$pipeName = "OceanFresh.Techik.SelfTest.$([Guid]::NewGuid().ToString('N'))"
$pipe = [System.IO.Pipes.NamedPipeServerStream]::new(
    $pipeName,
    [System.IO.Pipes.PipeDirection]::In,
    1,
    [System.IO.Pipes.PipeTransmissionMode]::Byte,
    [System.IO.Pipes.PipeOptions]::Asynchronous,
    1048576,
    1048576)
try {
    $connection = $pipe.BeginWaitForConnection($null, $null)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $bridge
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $escapedDetectorSdk = $detectorSdk.Replace('"', '\"')
    $startInfo.Arguments =
        "--sdk-root `"$escapedDetectorSdk`" --frame-pipe $pipeName --frame-stream-self-test"
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (!$process.Start()) {
        throw "Detector frame stream self-test process failed to start."
    }

    $pipe.EndWaitForConnection($connection)
    $frame = [byte[]]::new(48)
    $offset = 0
    while ($offset -lt $frame.Length) {
        $read = $pipe.Read($frame, $offset, $frame.Length - $offset)
        if ($read -le 0) {
            throw "Detector frame pipe closed before a complete frame was read."
        }
        $offset += $read
    }

    if (!$process.WaitForExit(10000)) {
        $process.Kill()
        throw "Detector frame stream self-test timed out."
    }
    $standardError = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) {
        throw "Detector frame stream self-test failed: $standardError"
    }
    if ([Text.Encoding]::ASCII.GetString($frame, 0, 4) -ne "OFR1" -or
        [BitConverter]::ToInt32($frame, 12) -ne 2 -or
        [BitConverter]::ToInt32($frame, 16) -ne 2 -or
        [BitConverter]::ToUInt16($frame, 40) -ne 1 -or
        [BitConverter]::ToUInt16($frame, 46) -ne 65535) {
        throw "Detector frame stream self-test returned an invalid binary frame."
    }
}
finally {
    if ($null -ne $process) {
        $process.Dispose()
    }
    $pipe.Dispose()
}
Write-Host "[4/4] Realtime detector memory stream: OK"
Write-Host "Offline deployment validation passed. No physical hardware output was enabled."
