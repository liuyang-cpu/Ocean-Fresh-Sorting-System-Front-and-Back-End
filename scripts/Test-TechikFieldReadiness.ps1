[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$TechikRoot = "C:\Techik",

    [Parameter(Mandatory = $false)]
    [string]$JsonOutput
)

$ErrorActionPreference = "Stop"

function Read-IniFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $result = @{}
    $section = ""
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $result
    }

    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = $rawLine.Trim()
        if (!$line -or $line.StartsWith(";") -or $line.StartsWith("#")) {
            continue
        }

        if ($line.StartsWith("[") -and $line.EndsWith("]")) {
            $section = $line.Substring(1, $line.Length - 2)
            if (!$result.ContainsKey($section)) {
                $result[$section] = @{}
            }
            continue
        }

        $separator = $line.IndexOf("=")
        if ($separator -lt 1) {
            continue
        }

        if (!$result.ContainsKey($section)) {
            $result[$section] = @{}
        }

        $key = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim()
        $result[$section][$key] = $value
    }

    return $result
}

function Get-IniValue {
    param(
        [hashtable]$Document,
        [string]$Section,
        [string]$Key,
        [string]$Default = ""
    )

    if ($Document.ContainsKey($Section) -and $Document[$Section].ContainsKey($Key)) {
        return [string]$Document[$Section][$Key]
    }

    return $Default
}

$resolvedRoot = [IO.Path]::GetFullPath($TechikRoot)
$configPath = Join-Path $resolvedRoot "cfg\config.ini"
$pointer = Read-IniFile -Path $configPath
$profileName = Get-IniValue -Document $pointer -Section "SYSTEM" -Key "name" -Default "prod_default"
$systemConfigPath = Join-Path $resolvedRoot "cfg\$profileName\sys_config.ini"
$miscConfigPath = Join-Path $resolvedRoot "cfg\misc_config.ini"
$systemConfig = Read-IniFile -Path $systemConfigPath
$miscConfig = Read-IniFile -Path $miscConfigPath

$requiredFiles = [ordered]@{
    MainExecutable = Join-Path $resolvedRoot "Techik.exe"
    DetectorPlugin = Join-Path $resolvedRoot "plugin\tk_driver_dt.dll"
    XrayPlugin = Join-Path $resolvedRoot "plugin\tk_driver_xray.dll"
    MotionPlugin = Join-Path $resolvedRoot "plugin\tk_driver_motion.dll"
    IoPlugin = Join-Path $resolvedRoot "plugin\tk_driver_io.dll"
    RejectorPlugin = Join-Path $resolvedRoot "plugin\tk_driver_rejector.dll"
    ModbusRuntime = Join-Path $resolvedRoot "modbus_x64.dll"
}

$fileChecks = foreach ($entry in $requiredFiles.GetEnumerator()) {
    [pscustomobject]@{
        Name = $entry.Key
        Path = $entry.Value
        Present = Test-Path -LiteralPath $entry.Value -PathType Leaf
    }
}

$serialPorts = @()
try {
    $serialPorts = @(Get-CimInstance Win32_SerialPort | ForEach-Object { $_.DeviceID.ToUpperInvariant() })
}
catch {
    try {
        $serialPorts = @([System.IO.Ports.SerialPort]::GetPortNames() | ForEach-Object { $_.ToUpperInvariant() })
    }
    catch {
        $serialPorts = @()
    }
}

$xrayCom = Get-IniValue -Document $systemConfig -Section "XRAY_TUBE_0" -Key "com_port"
$xrayType = Get-IniValue -Document $systemConfig -Section "XRAY_TUBE_0" -Key "type"
$motionCom = Get-IniValue -Document $systemConfig -Section "MOTION_0" -Key "com_port"
$motionType = Get-IniValue -Document $systemConfig -Section "MOTION_0" -Key "type"
$detectorType = Get-IniValue -Document $systemConfig -Section "DETECTOR_0" -Key "type"
$detectorNetId = Get-IniValue -Document $systemConfig -Section "DETECTOR_0" -Key "tk_net_id"
$detectorPixels = Get-IniValue -Document $systemConfig -Section "DETECTOR_0" -Key "line_pixels"
$ioCountText = Get-IniValue -Document $systemConfig -Section "IO_MODULE" -Key "io_num" -Default "0"
$ioCount = 0
[void][int]::TryParse($ioCountText, [ref]$ioCount)

$ioModules = for ($index = 0; $index -lt $ioCount; $index++) {
    $section = "IO_MODULE_$index"
    $comPort = Get-IniValue -Document $systemConfig -Section $section -Key "com_port"
    [pscustomobject]@{
        Id = Get-IniValue -Document $systemConfig -Section $section -Key "id" -Default ([string]$index)
        Type = Get-IniValue -Document $systemConfig -Section $section -Key "type"
        ComPort = if ($comPort) { "COM$comPort" } else { "" }
        PresentInWindows = if ($comPort) { $serialPorts -contains "COM$comPort" } else { $false }
    }
}

$rejectCount = Get-IniValue -Document $systemConfig -Section "REJECT_BULK_AIR" -Key "num" -Default "0"
$rejectDirection = Get-IniValue -Document $systemConfig -Section "REJECT_BULK_AIR" -Key "dir"
$captureDirectory = Get-IniValue -Document $miscConfig -Section "HISTORY_IMAGE_SAVE" -Key "save_path"

$report = [pscustomobject]@{
    GeneratedAt = (Get-Date).ToString("o")
    Mode = "ReadOnlyInspection"
    TechikRoot = $resolvedRoot
    ActiveProfile = $profileName
    Configuration = [pscustomobject]@{
        Pointer = $configPath
        System = $systemConfigPath
        Misc = $miscConfigPath
        Present = (Test-Path -LiteralPath $systemConfigPath -PathType Leaf)
    }
    RuntimeFiles = $fileChecks
    Detector = [pscustomobject]@{
        Type = $detectorType
        NetworkId = $detectorNetId
        LinePixels = $detectorPixels
    }
    Xray = [pscustomobject]@{
        Type = $xrayType
        ComPort = if ($xrayCom) { "COM$xrayCom" } else { "" }
        PresentInWindows = if ($xrayCom) { $serialPorts -contains "COM$xrayCom" } else { $false }
    }
    Motion = [pscustomobject]@{
        Type = $motionType
        ComPort = if ($motionCom) { "COM$motionCom" } else { "" }
        PresentInWindows = if ($motionCom) { $serialPorts -contains "COM$motionCom" } else { $false }
    }
    IoModules = @($ioModules)
    Rejector = [pscustomobject]@{
        OutputCount = $rejectCount
        DirectionForward = $rejectDirection
        FirstLogicalPort = Get-IniValue -Document $systemConfig -Section "REJECT_BULK_AIR" -Key "unit_0_port"
        LastLogicalPort = if ([int]$rejectCount -gt 0) {
            Get-IniValue -Document $systemConfig -Section "REJECT_BULK_AIR" -Key ("unit_{0}_port" -f ([int]$rejectCount - 1))
        } else {
            ""
        }
    }
    CaptureDirectory = $captureDirectory
    Safety = [pscustomobject]@{
        HardwareOutputEnabled = $false
        DirectProtocolConfirmed = $false
        DirectPortMapConfirmed = $false
        Message = "This script never opens a serial port and never sends a hardware command."
    }
}

$report | Format-List
$report.RuntimeFiles | Format-Table -AutoSize
$report.IoModules | Format-Table -AutoSize

if ($JsonOutput) {
    $jsonPath = [IO.Path]::GetFullPath($JsonOutput)
    $jsonDirectory = Split-Path -Parent $jsonPath
    if ($jsonDirectory -and !(Test-Path -LiteralPath $jsonDirectory)) {
        New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
    }

    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    Write-Host "Read-only readiness report written to: $jsonPath"
}
