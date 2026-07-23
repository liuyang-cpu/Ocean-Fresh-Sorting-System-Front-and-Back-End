[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [string]$TechikRoot = "C:\Techik",

    [Parameter(Mandatory = $false)]
    [string]$PythonRuntimePath,

    [Parameter(Mandatory = $false)]
    [string]$ModelSourcePath,

    [Parameter(Mandatory = $false)]
    [string]$DetectorSdkSourcePath = "F:\tk_driver_dt_demo 23-6-19"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $workspaceRoot "artifacts\field-package\OceanFresh-win-x64"
}

$packageRoot = [IO.Path]::GetFullPath($OutputDirectory)
$hmiProject = Join-Path $workspaceRoot "src\OceanFresh.SortingSystem.HMI\OceanFresh.SortingSystem.HMI.csproj"
$apiProject = Join-Path $workspaceRoot "src\OceanFresh.SortingSystem.LocalApi\OceanFresh.SortingSystem.LocalApi.csproj"
$apiOutput = Join-Path $packageRoot "LocalApi"
$yoloOutput = Join-Path $packageRoot "YoloService"
$docsOutput = Join-Path $packageRoot "Docs"
$bridgeOutput = Join-Path $packageRoot "HardwareBridge"
$detectorSdkOutput = Join-Path $packageRoot "DetectorSdk"
$techikRuntimeOutput = Join-Path $packageRoot "TechikRuntime"

if (Test-Path -LiteralPath $packageRoot) {
    $resolvedPackageRoot = [IO.Path]::GetFullPath($packageRoot)
    $resolvedArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot "artifacts"))
    if (!$resolvedPackageRoot.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a package outside the workspace artifacts directory: $resolvedPackageRoot"
    }

    Remove-Item -LiteralPath $resolvedPackageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $packageRoot, $apiOutput, $yoloOutput, $docsOutput, $bridgeOutput -Force | Out-Null

dotnet publish $hmiProject -c Release -r win-x64 --self-contained true -o $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw "HMI publish failed."
}

dotnet publish $apiProject -c Release -r win-x64 --self-contained true -o $apiOutput
if ($LASTEXITCODE -ne 0) {
    throw "LocalApi publish failed."
}

$yoloFiles = @(
    "predict\youge\predict_youge.py",
    "predict\youge\predict_youge.json",
    "predict\youge\service\app.py",
    "predict\youge\service\requirements.txt",
    "predict\youge\service\start_yolo_fastapi_service.bat"
)
foreach ($relativePath in $yoloFiles) {
    $source = Join-Path $workspaceRoot $relativePath
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination $yoloOutput -Force
    }
}

if ($PythonRuntimePath) {
    $resolvedPythonRuntime = [IO.Path]::GetFullPath($PythonRuntimePath)
    if (!(Test-Path -LiteralPath $resolvedPythonRuntime -PathType Container)) {
        throw "Python runtime directory was not found: $resolvedPythonRuntime"
    }

    Copy-Item -LiteralPath $resolvedPythonRuntime -Destination (Join-Path $packageRoot "PythonRuntime") -Recurse -Force
}

if ($ModelSourcePath) {
    $resolvedModelPath = [IO.Path]::GetFullPath($ModelSourcePath)
    if (!(Test-Path -LiteralPath $resolvedModelPath)) {
        throw "Model source was not found: $resolvedModelPath"
    }

    $modelOutput = Join-Path $packageRoot "Models"
    New-Item -ItemType Directory -Path $modelOutput -Force | Out-Null
    Copy-Item -LiteralPath $resolvedModelPath -Destination $modelOutput -Recurse -Force

    # A clean installation enables channel 1 and resolves this path relative
    # to the standalone YOLO service working directory.
    $seedModelOutput = Join-Path $yoloOutput "models\oil_clam\v1"
    New-Item -ItemType Directory -Path $seedModelOutput -Force | Out-Null
    Copy-Item -LiteralPath $resolvedModelPath -Destination (Join-Path $seedModelOutput "best.pt") -Force
}

Copy-Item -LiteralPath (Join-Path $workspaceRoot "docs\techik-hardware-integration.md") -Destination $docsOutput -Force
Copy-Item -LiteralPath (Join-Path $workspaceRoot "docs\techik-detector-abi-recovery.md") -Destination $docsOutput -Force
Copy-Item -LiteralPath (Join-Path $workspaceRoot "docs\offline-field-deployment.md") -Destination $docsOutput -Force
Copy-Item -LiteralPath (Join-Path $workspaceRoot "scripts\Test-OfflineDeployment.ps1") -Destination $packageRoot -Force

$bridgeProject = Join-Path $workspaceRoot "external\techik-detector-bridge\TechikDetectorBridge.vcxproj"
$msbuildCandidates = @(@(
    "D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($msbuildCandidates.Count -eq 0) {
    throw "Visual Studio 2022 C++ build tools were not found; TechikDetectorBridge cannot be built."
}

$bridgeIntermediate = Join-Path $workspaceRoot "artifacts\techik-bridge-package\obj"
& $msbuildCandidates[0] $bridgeProject /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$bridgeOutput/" "/p:IntDir=$bridgeIntermediate/"
if ($LASTEXITCODE -ne 0) {
    throw "TechikDetectorBridge build failed."
}

if (!(Test-Path -LiteralPath $DetectorSdkSourcePath -PathType Container)) {
    throw "Detector SDK source was not found: $DetectorSdkSourcePath"
}

Copy-Item -LiteralPath $DetectorSdkSourcePath -Destination $detectorSdkOutput -Recurse -Force
if (!(Test-Path -LiteralPath (Join-Path $detectorSdkOutput "tk_driver_dt.dll") -PathType Leaf)) {
    throw "Detector SDK package does not contain tk_driver_dt.dll."
}

$capturedRuntimeItems = @(
    "cfg",
    "product",
    "plugin\tk_driver_dt.dll",
    "plugin\tk_driver_xray.dll",
    "plugin\tk_driver_motion.dll",
    "plugin\tk_driver_io.dll",
    "plugin\tk_driver_rejector.dll",
    "Qt5Core.dll",
    "Qt5SerialPort.dll",
    "MSVCP140.dll",
    "VCRUNTIME140.dll",
    "modbus_x64.dll",
    "license.dat"
)
foreach ($relativePath in $capturedRuntimeItems) {
    $source = Join-Path $TechikRoot $relativePath
    if (!(Test-Path -LiteralPath $source)) {
        continue
    }

    $destination = Join-Path $techikRuntimeOutput $relativePath
    $destinationParent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}

$fieldConfig = @"
@echo off
set "OCEANFRESH_HARDWARE_ADAPTER=techik-direct"
set "OCEANFRESH_TECHIK_ROOT=%~dp0TechikRuntime"
set "OCEANFRESH_TECHIK_ENABLE_DETECTOR=true"
set "OCEANFRESH_TECHIK_FRAME_QUEUE_CAPACITY=16"
set "OCEANFRESH_TECHIK_ENABLE_PERIPHERALS=true"
set "OCEANFRESH_TECHIK_DETECTOR_BRIDGE=%~dp0HardwareBridge\TechikDetectorBridge.exe"
set "OCEANFRESH_TECHIK_DETECTOR_SDK_ROOT=%~dp0DetectorSdk"
set "OCEANFRESH_DATA_ROOT=%~dp0Data"
set "OCEANFRESH_TECHIK_FRAME_DIRECTORY=%~dp0Data\techik-frames"
set "OCEANFRESH_TECHIK_ENABLE_OUTPUT=false"
set "OCEANFRESH_YOLO_PYTHON=%~dp0PythonRuntime\python.exe"
set "OCEANFRESH_PYTHON_EXE=%~dp0PythonRuntime\python.exe"
set "YOLO_CONFIG_DIR=%~dp0Data\ultralytics"
set "PYTHONUTF8=1"
"@
$fieldConfig | Set-Content -LiteralPath (Join-Path $packageRoot "field-config.cmd") -Encoding ASCII

$launcher = @"
@echo off
cd /d "%~dp0"
call "%~dp0field-config.cmd"
start "" "%~dp0OceanFresh.SortingSystem.HMI.exe"
"@
$launcher | Set-Content -LiteralPath (Join-Path $packageRoot "start-oceanfresh.cmd") -Encoding ASCII

$checker = @"
@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-OfflineDeployment.ps1"
pause
"@
$checker | Set-Content -LiteralPath (Join-Path $packageRoot "check-environment.cmd") -Encoding ASCII

$manifest = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    runtimeIdentifier = "win-x64"
    selfContainedDotNet = $true
    hardwareMode = "techik-direct"
    detectorDirectEnabled = $true
    physicalOutputEnabled = $false
    techikRoot = "TechikRuntime"
    capturedTechikSource = $TechikRoot
    includesPythonRuntime = [bool]$PythonRuntimePath
    includesModel = [bool]$ModelSourcePath
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot "field-package.json") -Encoding UTF8

Write-Host "Field package created at: $packageRoot"
Write-Host "Captured detector and peripheral plugins are enabled. Physical X-ray, conveyor and reject outputs remain safety-locked."
