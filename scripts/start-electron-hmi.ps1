[CmdletBinding()]
param(
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$WorkspaceRoot = Split-Path -Parent $ScriptRoot
$LocalApiUrl = $env:OCEANFRESH_LOCAL_API_URL
if ([string]::IsNullOrWhiteSpace($LocalApiUrl)) {
    $LocalApiUrl = "http://127.0.0.1:5188"
}
$LocalApiUrl = $LocalApiUrl.TrimEnd("/")

$LocalApiProject = Join-Path $WorkspaceRoot "src\OceanFresh.SortingSystem.LocalApi\OceanFresh.SortingSystem.LocalApi.csproj"
$ElectronHmiDir = Join-Path $WorkspaceRoot "src\OceanFresh.SortingSystem.ElectronHmi"
$LogsDir = Join-Path $WorkspaceRoot "logs"
$LocalApiOutLog = Join-Path $LogsDir "localapi.out.log"
$LocalApiErrLog = Join-Path $LogsDir "localapi.err.log"
$StartedApiProcess = $null

function Test-LocalApiReady {
    param([string]$Url)

    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$Url/" -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-LocalApiReady {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-LocalApiReady -Url $Url) {
            return $true
        }

        Start-Sleep -Milliseconds 700
    }

    return $false
}

try {
    Write-Host "OceanFresh Electron HMI starter" -ForegroundColor Cyan
    Write-Host "Workspace: $WorkspaceRoot"
    Write-Host "LocalApi:  $LocalApiUrl"

    if (!(Test-Path $LocalApiProject)) {
        throw "LocalApi project not found: $LocalApiProject"
    }

    if (!(Test-Path $ElectronHmiDir)) {
        throw "Electron HMI directory not found: $ElectronHmiDir"
    }

    New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null

    if (Test-LocalApiReady -Url $LocalApiUrl) {
        Write-Host "LocalApi is already running." -ForegroundColor Green
    }
    else {
        Write-Host "Starting LocalApi in background..." -ForegroundColor Yellow
        $StartedApiProcess = Start-Process `
            -FilePath "dotnet" `
            -ArgumentList "run --project `"$LocalApiProject`"" `
            -WorkingDirectory $WorkspaceRoot `
            -RedirectStandardOutput $LocalApiOutLog `
            -RedirectStandardError $LocalApiErrLog `
            -WindowStyle Hidden `
            -PassThru

        if (!(Wait-LocalApiReady -Url $LocalApiUrl -TimeoutSeconds 60)) {
            throw "LocalApi did not become ready within 60 seconds. Check logs: $LocalApiOutLog and $LocalApiErrLog"
        }

        Write-Host "LocalApi is ready." -ForegroundColor Green
    }

    Push-Location $ElectronHmiDir
    try {
        if (!$SkipInstall -and !(Test-Path (Join-Path $ElectronHmiDir "node_modules"))) {
            Write-Host "node_modules not found. Running npm install..." -ForegroundColor Yellow
            npm install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed."
            }
        }

        $env:OCEANFRESH_LOCAL_API_URL = $LocalApiUrl
        Write-Host "Starting Electron + Vue HMI..." -ForegroundColor Cyan
        npm run dev
        if ($LASTEXITCODE -ne 0) {
            throw "npm run dev failed."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($StartedApiProcess -and !$StartedApiProcess.HasExited) {
        Write-Host "Stopping background LocalApi process..." -ForegroundColor Yellow
        Stop-Process -Id $StartedApiProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
