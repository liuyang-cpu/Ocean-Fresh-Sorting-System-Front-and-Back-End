[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [string]$TechikRoot = "C:\Techik",

    [Parameter(Mandatory = $false)]
    [string]$PythonRuntimePath,

    [Parameter(Mandatory = $false)]
    [string]$ModelSourcePath
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

if (Test-Path -LiteralPath $packageRoot) {
    $resolvedPackageRoot = [IO.Path]::GetFullPath($packageRoot)
    $resolvedArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot "artifacts"))
    if (!$resolvedPackageRoot.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a package outside the workspace artifacts directory: $resolvedPackageRoot"
    }

    Remove-Item -LiteralPath $resolvedPackageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $packageRoot, $apiOutput, $yoloOutput, $docsOutput -Force | Out-Null

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
}

Copy-Item -LiteralPath (Join-Path $workspaceRoot "docs\techik-hardware-integration.md") -Destination $docsOutput -Force
Copy-Item -LiteralPath (Join-Path $workspaceRoot "docs\techik-detector-abi-recovery.md") -Destination $docsOutput -Force

$escapedTechikRoot = $TechikRoot.Replace("%", "%%")
$fieldConfig = @"
@echo off
set "OCEANFRESH_HARDWARE_ADAPTER=techik-bridge"
set "OCEANFRESH_TECHIK_ROOT=$escapedTechikRoot"
set "OCEANFRESH_TECHIK_ENABLE_OUTPUT=false"
set "OCEANFRESH_TECHIK_DIRECT_PROTOCOL_CONFIRMED=false"
set "OCEANFRESH_TECHIK_DIRECT_PORT_MAP_CONFIRMED=false"
set "OCEANFRESH_TECHIK_MODBUS_SLAVE_ID=0"
"@
$fieldConfig | Set-Content -LiteralPath (Join-Path $packageRoot "field-config.cmd") -Encoding ASCII

$launcher = @"
@echo off
cd /d "%~dp0"
call "%~dp0field-config.cmd"
start "" "%~dp0OceanFresh.SortingSystem.HMI.exe"
"@
$launcher | Set-Content -LiteralPath (Join-Path $packageRoot "start-oceanfresh.cmd") -Encoding ASCII

$manifest = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    runtimeIdentifier = "win-x64"
    selfContainedDotNet = $true
    hardwareMode = "techik-bridge"
    physicalOutputEnabled = $false
    techikRoot = $TechikRoot
    includesPythonRuntime = [bool]$PythonRuntimePath
    includesModel = [bool]$ModelSourcePath
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot "field-package.json") -Encoding UTF8

Write-Host "Field package created at: $packageRoot"
Write-Host "Physical hardware output remains disabled."
