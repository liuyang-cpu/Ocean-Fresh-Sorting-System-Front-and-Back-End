using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public enum TechikIntegrationMode
{
    Disabled = 0,
    Bridge = 1,
    Direct = 2
}

public sealed record TechikRejectOutput(
    int NozzleNumber,
    int Port,
    bool IsEnabled,
    bool IsActiveHigh);

public sealed record TechikIoModuleProfile(
    int Id,
    int Type,
    int ComPort);

public sealed record TechikXrayStartupProfile(
    int Id,
    int Type,
    int ComPort,
    int MaxVoltageKv,
    int MinVoltageKv,
    int MaxCurrentUa,
    int MinCurrentUa,
    int MaxPowerLimit,
    int MinPowerLimit,
    int OpenWaitStableTimeMilliseconds,
    int CloseWaitStableTimeMilliseconds);

public sealed record TechikMotionStartupProfile(
    int Id,
    int Type,
    int ComPort,
    double SpeedMax,
    double SpeedMin,
    double SpeedRatio,
    bool DirectionReverse,
    int PlcId,
    int PlcRunPortForward,
    int PlcRunPortReverse,
    int PlcMonitorPortRun,
    int PlcAnalogId);

public sealed record TechikProductionSetpoints(
    double XrayVoltageKv,
    double XrayCurrentUa,
    double ConveyorSpeed,
    bool ConveyorDirection);

public sealed record TechikDetectorStartupProfile(
    int Id,
    int Type,
    int NetworkId,
    int SubCardPixels,
    int LinePixels,
    int Channels,
    double PitchSize,
    int SubFrameHeight,
    int BoundXrayId,
    bool ScanDirection,
    bool EnableDarkDynamicTracking,
    int DarkDynamicPixelStart,
    int DarkDynamicPixelLast,
    int CcdBinningMode,
    int CcdDualShiftPixels,
    int IasTdiLevel,
    int IasTdiLevelOffset,
    int IasKvThresholdLow,
    int IasKvThresholdHigh,
    int IasSoftBinningMode,
    int CalibrationType,
    int CalibrationDarkLines,
    int CalibrationFullLines,
    int CalibrationDarkTarget,
    int CalibrationFullTarget);

public sealed record TechikInstallationProfile(
    string RootDirectory,
    string MainExecutablePath,
    string ActiveProfileName,
    string SystemConfigPath,
    string MiscConfigPath,
    string CaptureDirectory,
    int DetectorCount,
    int DetectorLinePixels,
    int DetectorSubFrameHeight,
    int XrayComPort,
    int IoComPort,
    int ConveyorComPort,
    decimal XrayToDetectorDistanceMillimeters,
    decimal ConveyorToDetectorDistanceMillimeters,
    decimal RejectorSizeMillimeters,
    IReadOnlyDictionary<int, TechikRejectOutput> RejectOutputs,
    int DetectorType,
    int DetectorNetworkId,
    int XrayType,
    int ConveyorType,
    bool RejectDirectionForward,
    IReadOnlyList<TechikIoModuleProfile> IoModules)
{
    public bool HasMainRuntime => File.Exists(MainExecutablePath);

    public bool HasDetectorRuntime =>
        File.Exists(Path.Combine(RootDirectory, "plugin", "tk_driver_dt.dll"));

    public bool HasIoRuntime =>
        File.Exists(Path.Combine(RootDirectory, "plugin", "tk_driver_io.dll"));

    public bool HasRejectorRuntime =>
        File.Exists(Path.Combine(RootDirectory, "plugin", "tk_driver_rejector.dll"));

    public bool HasXrayRuntime =>
        File.Exists(Path.Combine(RootDirectory, "plugin", "tk_driver_xray.dll"));

    public bool HasMotionRuntime =>
        File.Exists(Path.Combine(RootDirectory, "plugin", "tk_driver_motion.dll"));

    public string NativeModbusLibraryPath => Path.Combine(RootDirectory, "modbus_x64.dll");

    public TechikDetectorStartupProfile DetectorStartup { get; init; } = new(
        0, 0, 0, 128, 1536, 1, 0.4, 50, 0, true, false, 0, 0, 0, 0,
        56, 4, 5, 25, 0, 0, 250, 1000, 0, 52428);

    public TechikXrayStartupProfile XrayStartup { get; init; } = new(
        0, 6, 5, 60, 30, 8000, 500, 350000, 6000, 8000, 5000);

    public TechikMotionStartupProfile MotionStartup { get; init; } = new(
        0, 4, 1, 120, 5, 23, false, 0, 9, 8, 7, 0);

    public TechikProductionSetpoints ProductionSetpoints { get; init; } =
        new(50, 5333, 15, false);
}

public sealed record TechikIntegrationOptions(
    TechikIntegrationMode Mode,
    TechikInstallationProfile Profile,
    bool EnableHardwareOutput,
    bool DirectProtocolConfirmed,
    bool DirectPortMapConfirmed,
    string ModbusSerialPort,
    int ModbusBaudRate,
    char ModbusParity,
    int ModbusDataBits,
    int ModbusStopBits,
    int ModbusSlaveId,
    string NativeModbusLibraryPath)
{
    private const string AdapterEnvVar = "OCEANFRESH_HARDWARE_ADAPTER";
    private const string RootEnvVar = "OCEANFRESH_TECHIK_ROOT";
    private const string CaptureDirectoryEnvVar = "OCEANFRESH_TECHIK_CAPTURE_DIRECTORY";
    private const string OutputEnabledEnvVar = "OCEANFRESH_TECHIK_ENABLE_OUTPUT";
    private const string DirectProtocolConfirmedEnvVar = "OCEANFRESH_TECHIK_DIRECT_PROTOCOL_CONFIRMED";
    private const string DirectPortMapConfirmedEnvVar = "OCEANFRESH_TECHIK_DIRECT_PORT_MAP_CONFIRMED";
    private const string ModbusLibraryEnvVar = "OCEANFRESH_TECHIK_MODBUS_DLL";
    private const string ModbusPortEnvVar = "OCEANFRESH_TECHIK_MODBUS_PORT";
    private const string ModbusBaudEnvVar = "OCEANFRESH_TECHIK_MODBUS_BAUD";
    private const string ModbusParityEnvVar = "OCEANFRESH_TECHIK_MODBUS_PARITY";
    private const string ModbusDataBitsEnvVar = "OCEANFRESH_TECHIK_MODBUS_DATA_BITS";
    private const string ModbusStopBitsEnvVar = "OCEANFRESH_TECHIK_MODBUS_STOP_BITS";
    private const string ModbusSlaveIdEnvVar = "OCEANFRESH_TECHIK_MODBUS_SLAVE_ID";
    private const string DetectorSdkRootEnvVar = "OCEANFRESH_TECHIK_DETECTOR_SDK_ROOT";
    private const string DetectorBridgeExecutableEnvVar = "OCEANFRESH_TECHIK_DETECTOR_BRIDGE";
    private const string DetectorFrameDirectoryEnvVar = "OCEANFRESH_TECHIK_FRAME_DIRECTORY";
    private const string DetectorFrameQueueCapacityEnvVar = "OCEANFRESH_TECHIK_FRAME_QUEUE_CAPACITY";
    private const string DetectorEnabledEnvVar = "OCEANFRESH_TECHIK_ENABLE_DETECTOR";
    private const string PeripheralsEnabledEnvVar = "OCEANFRESH_TECHIK_ENABLE_PERIPHERALS";
    private const string XrayVoltageEnvVar = "OCEANFRESH_TECHIK_XRAY_KV";
    private const string XrayCurrentEnvVar = "OCEANFRESH_TECHIK_XRAY_UA";
    private const string ConveyorSpeedEnvVar = "OCEANFRESH_TECHIK_CONVEYOR_SPEED";
    private const string ConveyorDirectionEnvVar = "OCEANFRESH_TECHIK_CONVEYOR_DIRECTION";

    public static TechikIntegrationOptions LoadFromEnvironment()
    {
        var mode = ParseMode(Environment.GetEnvironmentVariable(AdapterEnvVar));
        var root = Environment.GetEnvironmentVariable(RootEnvVar);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = @"C:\Techik";
        }

        var profile = TechikInstallationProfileReader.Load(root);
        var captureOverride = Environment.GetEnvironmentVariable(CaptureDirectoryEnvVar);
        if (!string.IsNullOrWhiteSpace(captureOverride))
        {
            profile = profile with { CaptureDirectory = Path.GetFullPath(captureOverride) };
        }

        var configuredPort = ReadString(ModbusPortEnvVar, profile.IoComPort > 0 ? $"COM{profile.IoComPort}" : "COM3");
        var parityText = ReadString(ModbusParityEnvVar, "N");
        var libraryOverride = Environment.GetEnvironmentVariable(ModbusLibraryEnvVar);

        var options = new TechikIntegrationOptions(
            mode,
            profile,
            ReadBoolean(OutputEnabledEnvVar),
            ReadBoolean(DirectProtocolConfirmedEnvVar),
            ReadBoolean(DirectPortMapConfirmedEnvVar),
            configuredPort,
            ReadPositiveInt(ModbusBaudEnvVar, 38_400),
            char.ToUpperInvariant(parityText[0]),
            ReadPositiveInt(ModbusDataBitsEnvVar, 8),
            ReadPositiveInt(ModbusStopBitsEnvVar, 2),
            ReadNonNegativeInt(ModbusSlaveIdEnvVar, 0),
            string.IsNullOrWhiteSpace(libraryOverride)
                ? profile.NativeModbusLibraryPath
                : Path.GetFullPath(libraryOverride));
        return options with
        {
            DetectorSdkRoot = ReadPath(
                DetectorSdkRootEnvVar,
                Path.Combine(profile.RootDirectory, "detector-sdk")),
            DetectorBridgeExecutablePath = ReadPath(
                DetectorBridgeExecutableEnvVar,
                Path.Combine(AppContext.BaseDirectory, "HardwareBridge", "TechikDetectorBridge.exe")),
            DetectorFrameDirectory = ReadPath(
                DetectorFrameDirectoryEnvVar,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OceanFreshSortingSystem",
                    "techik-frames")),
            FrameQueueCapacity = Math.Clamp(
                ReadPositiveInt(DetectorFrameQueueCapacityEnvVar, 16),
                4,
                128),
            EnableDetector = ReadBoolean(DetectorEnabledEnvVar),
            EnablePeripherals = ReadBoolean(PeripheralsEnabledEnvVar),
            ProductionSetpoints = new TechikProductionSetpoints(
                ReadDouble(
                    XrayVoltageEnvVar,
                    profile.ProductionSetpoints.XrayVoltageKv),
                ReadDouble(
                    XrayCurrentEnvVar,
                    profile.ProductionSetpoints.XrayCurrentUa),
                ReadDouble(
                    ConveyorSpeedEnvVar,
                    profile.ProductionSetpoints.ConveyorSpeed),
                ReadBoolean(
                    ConveyorDirectionEnvVar,
                    profile.ProductionSetpoints.ConveyorDirection))
        };
    }

    public void ApplyCaptureDirectoryToProcess()
    {
        if (Mode == TechikIntegrationMode.Disabled ||
            (Mode == TechikIntegrationMode.Bridge && string.IsNullOrWhiteSpace(Profile.CaptureDirectory)) ||
            (Mode == TechikIntegrationMode.Direct && string.IsNullOrWhiteSpace(DetectorFrameDirectory)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY")))
        {
            Environment.SetEnvironmentVariable(
                "OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY",
                Mode == TechikIntegrationMode.Direct
                    ? DetectorFrameDirectory
                    : Profile.CaptureDirectory,
                EnvironmentVariableTarget.Process);
        }
    }

    public string DetectorSdkRoot { get; init; } = string.Empty;

    public string DetectorBridgeExecutablePath { get; init; } = string.Empty;

    public string DetectorFrameDirectory { get; init; } = string.Empty;

    public int FrameQueueCapacity { get; init; } = 16;

    public bool EnableDetector { get; init; }

    public bool EnablePeripherals { get; init; }

    public TechikProductionSetpoints ProductionSetpoints { get; init; } =
        new(50, 5333, 15, false);

    private static TechikIntegrationMode ParseMode(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "techik" or "techik-bridge" or "bridge" => TechikIntegrationMode.Bridge,
        "techik-direct" or "direct" => TechikIntegrationMode.Direct,
        _ => TechikIntegrationMode.Disabled
    };

    private static string ReadString(string variableName, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ReadPath(string variableName, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
    }

    private static int ReadPositiveInt(string variableName, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static int ReadNonNegativeInt(string variableName, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : fallback;
    }

    private static bool ReadBoolean(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadBoolean(string variableName, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static double ReadDouble(string variableName, double fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}

public static class TechikInstallationProfileReader
{
    public static TechikInstallationProfile Load(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        var profilePointerPath = Path.Combine(root, "cfg", "config.ini");
        var pointer = IniDocument.Load(profilePointerPath);
        var profileName = pointer.Get("SYSTEM", "name", "prod_default");
        var systemConfigPath = Path.Combine(root, "cfg", profileName, "sys_config.ini");
        var miscConfigPath = Path.Combine(root, "cfg", "misc_config.ini");
        var system = IniDocument.Load(systemConfigPath);
        var misc = IniDocument.Load(miscConfigPath);

        var captureDirectory = misc.Get("HISTORY_IMAGE_SAVE", "save_path", string.Empty);
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            captureDirectory = Path.GetFullPath(captureDirectory.Replace('/', Path.DirectorySeparatorChar));
        }

        var rejectOutputs = new Dictionary<int, TechikRejectOutput>();
        var rejectCount = system.GetInt("REJECT_BULK_AIR", "num", 0);
        for (var index = 0; index < rejectCount; index++)
        {
            rejectOutputs[index + 1] = new TechikRejectOutput(
                index + 1,
                system.GetInt("REJECT_BULK_AIR", $"unit_{index}_port", -1),
                system.GetBoolean("REJECT_BULK_AIR", $"unit_{index}_enable", false),
                system.GetBoolean("REJECT_BULK_AIR", $"unit_{index}_signal_polarity", false));
        }

        var ioModules = new List<TechikIoModuleProfile>();
        var ioCount = system.GetInt("IO_MODULE", "io_num", 0);
        for (var index = 0; index < ioCount; index++)
        {
            var section = $"IO_MODULE_{index}";
            ioModules.Add(new TechikIoModuleProfile(
                system.GetInt(section, "id", index),
                system.GetInt(section, "type", -1),
                system.GetInt(section, "com_port", 0)));
        }

        var profile = new TechikInstallationProfile(
            root,
            Path.Combine(root, "Techik.exe"),
            profileName,
            systemConfigPath,
            miscConfigPath,
            captureDirectory,
            system.GetInt("DETECTOR", "dt_num", 0),
            system.GetInt("DETECTOR_0", "line_pixels", 0),
            system.GetInt("DETECTOR_0", "sub_frame_height", 0),
            system.GetInt("XRAY_TUBE_0", "com_port", 0),
            system.GetInt("IO_MODULE_0", "com_port", 0),
            system.GetInt("MOTION_0", "com_port", 0),
            system.GetDecimal("DT_XRAY_GEOMETRIC", "dis_xray_dt", 0m),
            system.GetDecimal("DT_XRAY_GEOMETRIC", "dis_coy_dt", 0m),
            system.GetDecimal("DT_XRAY_GEOMETRIC", "size_reject", 0m),
            rejectOutputs,
            system.GetInt("DETECTOR_0", "type", -1),
            system.GetInt("DETECTOR_0", "tk_net_id", -1),
            system.GetInt("XRAY_TUBE_0", "type", -1),
            system.GetInt("MOTION_0", "type", -1),
            system.GetBoolean("REJECT_BULK_AIR", "dir", true),
            ioModules);
        return profile with
        {
            XrayStartup = new TechikXrayStartupProfile(
                system.GetInt("XRAY_TUBE_0", "id", 0),
                system.GetInt("XRAY_TUBE_0", "type", 6),
                system.GetInt("XRAY_TUBE_0", "com_port", 5),
                system.GetInt("XRAY_TUBE_0", "max_voltage_kv", 60),
                system.GetInt("XRAY_TUBE_0", "min_voltage_kv", 30),
                system.GetInt("XRAY_TUBE_0", "max_current_ua", 8000),
                system.GetInt("XRAY_TUBE_0", "min_current_ua", 500),
                system.GetInt("XRAY_TUBE_0", "max_power_limit", 350000),
                system.GetInt("XRAY_TUBE_0", "min_power_limit", 6000),
                system.GetInt("XRAY_TUBE_0", "open_wait_stable_time_ms", 8000),
                system.GetInt("XRAY_TUBE_0", "close_wait_stable_time_ms", 5000)),
            MotionStartup = new TechikMotionStartupProfile(
                system.GetInt("MOTION_0", "id", 0),
                system.GetInt("MOTION_0", "type", 4),
                system.GetInt("MOTION_0", "com_port", 1),
                system.GetDouble("MOTION_0", "speed_max", 120),
                system.GetDouble("MOTION_0", "speed_min", 5),
                system.GetDouble("MOTION_0", "speed_ratio", 23),
                system.GetBoolean("MOTION_0", "dir_reverse", false),
                system.GetInt("MOTION_0", "plc_id", 0),
                system.GetInt("MOTION_0", "plc_run_port_fwd", 9),
                system.GetInt("MOTION_0", "plc_run_port_rev", 8),
                system.GetInt("MOTION_0", "plc_mon_port_run", 7),
                system.GetInt("MOTION_0", "plc_analog_id", 0)),
            ProductionSetpoints = TechikProductSetpointReader.Load(
                root,
                new TechikProductionSetpoints(50, 5333, 15, false)),
            DetectorStartup = new TechikDetectorStartupProfile(
                system.GetInt("DETECTOR_0", "id", 0),
                system.GetInt("DETECTOR_0", "type", 0),
                system.GetInt("DETECTOR_0", "tk_net_id", 0),
                system.GetInt("DETECTOR_0", "sub_card_pixel", 128),
                system.GetInt("DETECTOR_0", "line_pixels", 1536),
                system.GetInt("DETECTOR_0", "channels", 1),
                system.GetDouble("DETECTOR_0", "pitch_size", 0.4),
                system.GetInt("DETECTOR_0", "sub_frame_height", 50),
                system.GetInt("DETECTOR_0", "bind_xray_id", 0),
                system.GetBoolean("DETECTOR_0", "tk_scan_dir_mode", true),
                system.GetBoolean("DETECTOR_0", "tk_enable_dark_dy_track", false),
                system.GetInt("DETECTOR_0", "tk_dark_dy_track_pixel_start", 0),
                system.GetInt("DETECTOR_0", "tk_dark_dy_track_pixel_last", 0),
                system.GetInt("DETECTOR_0", "ccd_binning_mode", 0),
                system.GetInt("DETECTOR_0", "ccd_dual_shift_pixels", 0),
                system.GetInt("DETECTOR_0", "ias_tdi_level", 56),
                system.GetInt("DETECTOR_0", "ias_tdi_level_offset", 4),
                system.GetInt("DETECTOR_0", "ias_kv_th_low", 5),
                system.GetInt("DETECTOR_0", "ias_kv_th_high", 25),
                system.GetInt("DETECTOR_0", "ias_soft_binning_mode", 0),
                system.GetInt("DETECTOR_0", "calibra_type", 0),
                system.GetInt("DETECTOR_0", "dycalibra_dark_lines", 250),
                system.GetInt("DETECTOR_0", "dycalibra_full_lines", 1000),
                system.GetInt("DETECTOR_0", "dycalibra_dark_target", 0),
                system.GetInt("DETECTOR_0", "dycalibra_full_target", 52428))
        };
    }
}

internal static class TechikProductSetpointReader
{
    public static TechikProductionSetpoints Load(
        string root,
        TechikProductionSetpoints fallback)
    {
        try
        {
            var productRoot = Path.Combine(root, "product");
            var productIndex = IniDocument.Load(Path.Combine(productRoot, "config.ini"));
            var productId = productIndex.GetInt("PRODUCT", "id", 0);
            if (productId <= 0)
            {
                return fallback;
            }

            var activePath = Path.Combine(productRoot, productId.ToString("000"), "prod_param.ini");
            if (!File.Exists(activePath))
            {
                activePath = Directory
                    .EnumerateFiles(
                        Path.Combine(productRoot, "Delete"),
                        $"ID {productId:000}_*_prod_param.ini",
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? string.Empty;
            }
            if (!File.Exists(activePath))
            {
                return fallback;
            }

            var product = IniDocument.Load(activePath);
            var xray = DecodeQtByteArray(product.Get("PROD_PARAM_XRAY", "xray_0", string.Empty));
            var motion = DecodeQtByteArray(product.Get("PROD_PARAM_MOTION", "motion_0", string.Empty));
            if (xray.Length < 8 || motion.Length < 16)
            {
                return fallback;
            }

            return new TechikProductionSetpoints(
                BitConverter.ToInt32(xray, 0),
                BitConverter.ToInt32(xray, 4),
                BitConverter.ToDouble(motion, 8),
                motion[0] != 0);
        }
        catch
        {
            return fallback;
        }
    }

    private static byte[] DecodeQtByteArray(string value)
    {
        const string prefix = "@ByteArray(";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(')'))
        {
            return [];
        }

        var content = value.AsSpan(prefix.Length, value.Length - prefix.Length - 1);
        var bytes = new List<byte>(content.Length);
        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            if (current != '\\')
            {
                bytes.Add((byte)current);
                continue;
            }

            if (++index >= content.Length)
            {
                break;
            }

            var escaped = content[index];
            if (escaped == '0')
            {
                bytes.Add(0);
                continue;
            }
            if (escaped == 'x' && index + 1 < content.Length)
            {
                var hexStart = index + 1;
                var hexLength = 0;
                while (hexLength < 2 &&
                       hexStart + hexLength < content.Length &&
                       Uri.IsHexDigit(content[hexStart + hexLength]))
                {
                    hexLength++;
                }
                if (hexLength > 0)
                {
                    bytes.Add(Convert.ToByte(
                        content.Slice(hexStart, hexLength).ToString(),
                        16));
                    index += hexLength;
                    continue;
                }
            }

            bytes.Add((byte)escaped);
        }

        return bytes.ToArray();
    }
}

public sealed class TechikHardwareProtocolClient(
    TechikIntegrationOptions options,
    TechikDetectorBridgeProcess detectorBridge) : IHardwareProtocolClient
{
    public async Task<IReadOnlyList<HardwareSignalStatus>> ReadSignalsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Mode == TechikIntegrationMode.Direct)
        {
            try
            {
                await detectorBridge.EnsureStartedAsync(cancellationToken);
                await detectorBridge.RefreshStatusAsync(cancellationToken);
            }
            catch
            {
            }
        }

        var now = DateTimeOffset.UtcNow;

        if (options.Mode == TechikIntegrationMode.Disabled)
        {
            return [];
        }

        var profile = options.Profile;
        var techikRunning = IsTechikRunning();
        var bridgeReady = options.Mode == TechikIntegrationMode.Bridge && techikRunning;
        var serialPorts = ReadSerialPorts();
        var ioPortPresent = serialPorts.Contains(options.ModbusSerialPort);

        var detectorReady = options.Mode switch
        {
            TechikIntegrationMode.Bridge =>
                techikRunning &&
                profile.HasDetectorRuntime &&
                profile.DetectorCount > 0 &&
                !string.IsNullOrWhiteSpace(profile.CaptureDirectory) &&
                Directory.Exists(profile.CaptureDirectory),
            TechikIntegrationMode.Direct =>
                detectorBridge.IsRunning,
            _ => false
        };

        var peripheral = detectorBridge.PeripheralSnapshot;
        var controllerReady = options.Mode switch
        {
            TechikIntegrationMode.Bridge => bridgeReady,
            TechikIntegrationMode.Direct =>
                detectorBridge.PeripheralsConnected &&
                peripheral.PrimaryIoOnline &&
                peripheral.SecondaryIoOnline,
            _ => false
        };

        var ejectorReady = options.Mode switch
        {
            TechikIntegrationMode.Bridge => bridgeReady,
            TechikIntegrationMode.Direct =>
                detectorBridge.PeripheralsConnected &&
                peripheral.PrimaryIoOnline &&
                options.EnableHardwareOutput,
            _ => false
        };

        var xrayReady = options.Mode == TechikIntegrationMode.Bridge
            ? bridgeReady
            : detectorBridge.PeripheralsConnected &&
              peripheral.XrayOnline &&
              peripheral.XrayFaultCode == 0;
        var conveyorReady = options.Mode == TechikIntegrationMode.Bridge
            ? bridgeReady
            : detectorBridge.PeripheralsConnected && peripheral.MotionOnline;

        IReadOnlyList<HardwareSignalStatus> result =
        [
            BuildStatus("TECHIK-DETECTOR-0", DeviceType.XrayDetector, detectorReady,
                detectorReady
                    ? options.Mode == TechikIntegrationMode.Direct
                        ? $"Ocean Fresh 已直接接管 Techik 探测器，输出 {profile.DetectorLinePixels} px 原始线阵图像。"
                        : $"Techik 探测器桥接目录就绪，{profile.DetectorLinePixels} px × {profile.DetectorSubFrameHeight} 行。"
                    : options.Mode == TechikIntegrationMode.Direct
                        ? $"Techik 探测器桥接未运行：{detectorBridge.LastMessage}"
                        : "Techik 探测器 DLL、配置或图像桥接目录未就绪。", now),
            BuildStatus("TECHIK-XRAY-0", DeviceType.XraySource, xrayReady,
                xrayReady
                    ? $"Ocean Fresh 已通过原厂 type={profile.XrayType} 插件接管 COM{profile.XrayComPort} X 光源。"
                    : $"X 光源未在线或故障码非零：{detectorBridge.LastMessage}", now),
            BuildStatus("TECHIK-CONVEYOR-0", DeviceType.Conveyor, conveyorReady,
                conveyorReady
                    ? $"Ocean Fresh 已通过原厂 type={profile.ConveyorType} 插件接管传送带/PLC。"
                    : $"传送带/PLC 未在线：{detectorBridge.LastMessage}", now),
            BuildStatus("TECHIK-EJECTOR-0", DeviceType.Ejector, ejectorReady,
                ejectorReady
                    ? $"Techik 剔除链路就绪，配置 {profile.RejectOutputs.Count} 路输出。"
                    : "剔除链路未就绪或仍处于物理输出安全锁定。", now),
            BuildStatus("TECHIK-CONTROLLER-0", DeviceType.Controller, controllerReady,
                controllerReady
                    ? options.Mode == TechikIntegrationMode.Bridge
                        ? "Techik 进程正在托管硬件控制。"
                        : "Techik type=3/type=7 IO 插件均已在线。"
                    : "Techik 控制进程或配置的串口未就绪。", now)
        ];

        return result;
    }

    private static HardwareSignalStatus BuildStatus(
        string deviceNo,
        DeviceType type,
        bool isReady,
        string message,
        DateTimeOffset updatedAt) =>
        new(
            deviceNo,
            type,
            isReady ? DeviceState.Idle : DeviceState.Faulted,
            isReady ? HardwareSignalSeverity.Normal : HardwareSignalSeverity.Critical,
            isReady ? $"{deviceNo}_READY" : $"{deviceNo}_NOT_READY",
            message,
            null,
            string.Empty,
            updatedAt);

    private static bool IsTechikRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("Techik");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<string> ReadSerialPorts()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return ports;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is null)
            {
                return ports;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is string port && !string.IsNullOrWhiteSpace(port))
                {
                    ports.Add(port);
                }
            }
        }
        catch
        {
        }

        return ports;
    }
}

public sealed class TechikEjectorController(
    TechikIntegrationOptions options,
    TechikDetectorBridgeProcess bridge) : IEjectorController
{
    public async Task ExecuteAsync(EjectCommand command, CancellationToken cancellationToken)
    {
        if (command.Action == DefectHandlingAction.Pass)
        {
            return;
        }

        if (options.Mode != TechikIntegrationMode.Direct)
        {
            throw new InvalidOperationException(
                "Techik bridge mode only reads images and health state; it cannot send Ocean Fresh eject commands.");
        }

        if (!options.EnableHardwareOutput || !options.EnablePeripherals)
        {
            throw new InvalidOperationException(
                "Techik hardware output is safety-locked. Enable the captured peripheral runtime after field verification.");
        }

        if (!options.Profile.RejectOutputs.TryGetValue(command.NozzleNumber, out var output) ||
            !output.IsEnabled ||
            output.Port < 0)
        {
            throw new InvalidOperationException($"No enabled Techik reject output is mapped to nozzle {command.NozzleNumber}.");
        }

        if (command.TriggerDelayMicroseconds > 0)
        {
            await Task.Delay(
                TimeSpan.FromTicks(command.TriggerDelayMicroseconds * 10L),
                cancellationToken);
        }

        await bridge.PulseRejectAsync(
            output.Port,
            output.IsActiveHigh,
            Math.Max(1, command.PulseWidthMicroseconds),
            cancellationToken);
    }
}

public sealed class TechikProductionHardwareController(
    TechikIntegrationOptions options,
    TechikDetectorBridgeProcess bridge) : IProductionHardwareController
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        options.Mode == TechikIntegrationMode.Direct
            ? bridge.StartMachineAsync(cancellationToken)
            : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) =>
        options.Mode == TechikIntegrationMode.Direct
            ? bridge.StopMachineAsync(cancellationToken)
            : Task.CompletedTask;
}

public sealed class SimulatedProductionHardwareController : IProductionHardwareController
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal sealed class NativeModbusRtuSession : IDisposable
{
    private nint _library;
    private readonly ModbusClose _close;
    private readonly ModbusFree _free;
    private readonly ModbusConnect _connect;
    private readonly ModbusSetSlave _setSlave;
    private readonly ModbusWriteBit _writeBit;
    private nint _context;
    private bool _connected;

    public NativeModbusRtuSession(TechikIntegrationOptions options)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("The captured Techik runtime and modbus_x64.dll require a 64-bit process.");
        }

        if (!File.Exists(options.NativeModbusLibraryPath))
        {
            throw new FileNotFoundException("Techik modbus_x64.dll was not found.", options.NativeModbusLibraryPath);
        }

        if (options.ModbusSlaveId <= 0)
        {
            throw new InvalidOperationException(
                "A confirmed positive Modbus slave id is required for direct hardware access.");
        }

        _library = NativeLibrary.Load(options.NativeModbusLibraryPath);
        try
        {
            var create = GetExport<ModbusNewRtu>("modbus_new_rtu");
            _connect = GetExport<ModbusConnect>("modbus_connect");
            _setSlave = GetExport<ModbusSetSlave>("modbus_set_slave");
            _writeBit = GetExport<ModbusWriteBit>("modbus_write_bit");
            _close = GetExport<ModbusClose>("modbus_close");
            _free = GetExport<ModbusFree>("modbus_free");

            _context = create(
                NormalizeSerialPort(options.ModbusSerialPort),
                options.ModbusBaudRate,
                (byte)options.ModbusParity,
                options.ModbusDataBits,
                options.ModbusStopBits);
            if (_context == nint.Zero)
            {
                throw new InvalidOperationException("modbus_new_rtu returned a null context.");
            }

            EnsureSuccess(_setSlave(_context, options.ModbusSlaveId), "modbus_set_slave");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Connect()
    {
        EnsureContext();
        EnsureSuccess(_connect(_context), "modbus_connect");
        _connected = true;
    }

    public void WriteBit(int address, int value)
    {
        EnsureContext();
        if (!_connected)
        {
            throw new InvalidOperationException("The Modbus RTU session is not connected.");
        }

        EnsureSuccess(_writeBit(_context, address, value), $"modbus_write_bit({address})");
    }

    public void Dispose()
    {
        if (_context != nint.Zero)
        {
            if (_connected)
            {
                _close(_context);
            }

            _free(_context);
            _context = nint.Zero;
            _connected = false;
        }

        if (_library != nint.Zero)
        {
            NativeLibrary.Free(_library);
            _library = nint.Zero;
        }
    }

    private T GetExport<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private void EnsureContext()
    {
        if (_context == nint.Zero)
        {
            throw new ObjectDisposedException(nameof(NativeModbusRtuSession));
        }
    }

    private static void EnsureSuccess(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Techik Modbus operation failed: {operation}.");
        }
    }

    private static string NormalizeSerialPort(string port) =>
        port.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) ? port : $@"\\.\{port}";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate nint ModbusNewRtu(
        [MarshalAs(UnmanagedType.LPStr)] string device,
        int baud,
        byte parity,
        int dataBit,
        int stopBit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ModbusConnect(nint context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ModbusSetSlave(nint context, int slaveId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ModbusWriteBit(nint context, int address, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ModbusClose(nint context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ModbusFree(nint context);
}

internal sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniDocument Load(string path)
    {
        var document = new IniDocument();
        if (!File.Exists(path))
        {
            return document;
        }

        var currentSection = string.Empty;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!document._sections.TryGetValue(currentSection, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                document._sections[currentSection] = values;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"');
        }

        return document;
    }

    public string Get(string section, string key, string fallback) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
            ? value
            : fallback;

    public int GetInt(string section, string key, int fallback) =>
        int.TryParse(Get(section, key, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public decimal GetDecimal(string section, string key, decimal fallback) =>
        decimal.TryParse(Get(section, key, string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public double GetDouble(string section, string key, double fallback) =>
        double.TryParse(Get(section, key, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public bool GetBoolean(string section, string key, bool fallback)
    {
        var value = Get(section, key, string.Empty);
        return value.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => fallback
        };
    }
}
