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

        return new TechikIntegrationOptions(
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
    }

    public void ApplyCaptureDirectoryToProcess()
    {
        if (Mode == TechikIntegrationMode.Disabled || string.IsNullOrWhiteSpace(Profile.CaptureDirectory))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY")))
        {
            Environment.SetEnvironmentVariable(
                "OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY",
                Profile.CaptureDirectory,
                EnvironmentVariableTarget.Process);
        }
    }

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

        return new TechikInstallationProfile(
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
    }
}

public sealed class TechikHardwareProtocolClient(TechikIntegrationOptions options) : IHardwareProtocolClient
{
    public Task<IReadOnlyList<HardwareSignalStatus>> ReadSignalsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        if (options.Mode == TechikIntegrationMode.Disabled)
        {
            return Task.FromResult<IReadOnlyList<HardwareSignalStatus>>([]);
        }

        var profile = options.Profile;
        var techikRunning = IsTechikRunning();
        var bridgeReady = options.Mode == TechikIntegrationMode.Bridge && techikRunning;
        var serialPorts = ReadSerialPorts();
        var ioPortPresent = serialPorts.Contains(options.ModbusSerialPort);

        var detectorReady = options.Mode == TechikIntegrationMode.Bridge &&
                            techikRunning &&
                            profile.HasDetectorRuntime &&
                            profile.DetectorCount > 0 &&
                            !string.IsNullOrWhiteSpace(profile.CaptureDirectory) &&
                            Directory.Exists(profile.CaptureDirectory);

        var controllerReady = options.Mode switch
        {
            TechikIntegrationMode.Bridge => bridgeReady,
            TechikIntegrationMode.Direct => profile.HasIoRuntime && ioPortPresent,
            _ => false
        };

        var ejectorReady = options.Mode switch
        {
            TechikIntegrationMode.Bridge => bridgeReady,
            TechikIntegrationMode.Direct => profile.HasRejectorRuntime &&
                                            File.Exists(options.NativeModbusLibraryPath) &&
                                            ioPortPresent &&
                                            options.EnableHardwareOutput &&
                                            options.DirectProtocolConfirmed &&
                                            options.DirectPortMapConfirmed &&
                                            options.ModbusSlaveId > 0,
            _ => false
        };

        var xrayReady = options.Mode == TechikIntegrationMode.Bridge
            ? bridgeReady
            : false;
        var conveyorReady = options.Mode == TechikIntegrationMode.Bridge
            ? bridgeReady
            : false;

        IReadOnlyList<HardwareSignalStatus> result =
        [
            BuildStatus("TECHIK-DETECTOR-0", DeviceType.XrayDetector, detectorReady,
                detectorReady
                    ? $"Techik 探测器桥接目录就绪，{profile.DetectorLinePixels} px × {profile.DetectorSubFrameHeight} 行。"
                    : "Techik 探测器 DLL、配置或图像桥接目录未就绪。", now),
            BuildStatus("TECHIK-XRAY-0", DeviceType.XraySource, xrayReady,
                xrayReady ? $"Techik 进程正在托管 X 光源（配置 COM{profile.XrayComPort}）。" : "尚未验证 X 光源活动通信；直接控制协议仍需现场确认。", now),
            BuildStatus("TECHIK-CONVEYOR-0", DeviceType.Conveyor, conveyorReady,
                conveyorReady ? $"Techik 进程正在托管传送带（配置 COM{profile.ConveyorComPort}）。" : "尚未验证传送带活动通信；直接控制协议仍需现场确认。", now),
            BuildStatus("TECHIK-EJECTOR-0", DeviceType.Ejector, ejectorReady,
                ejectorReady
                    ? $"Techik 剔除链路就绪，配置 {profile.RejectOutputs.Count} 路输出。"
                    : "剔除输出保持安全锁定；需确认 Modbus 站号和端口映射后显式启用。", now),
            BuildStatus("TECHIK-CONTROLLER-0", DeviceType.Controller, controllerReady,
                controllerReady
                    ? options.Mode == TechikIntegrationMode.Bridge
                        ? "Techik 进程正在托管硬件控制。"
                        : $"已发现 {options.ModbusSerialPort} 和 Techik I/O 运行库。"
                    : "Techik 控制进程或配置的串口未就绪。", now)
        ];

        return Task.FromResult(result);
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

public sealed class TechikEjectorController(TechikIntegrationOptions options) : IEjectorController
{
    private readonly SemaphoreSlim _commandGate = new(1, 1);

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

        if (!options.EnableHardwareOutput ||
            !options.DirectProtocolConfirmed ||
            !options.DirectPortMapConfirmed ||
            options.ModbusSlaveId <= 0)
        {
            throw new InvalidOperationException(
                "Techik hardware output is safety-locked. Confirm the protocol, Modbus station and direct port mapping before enabling output.");
        }

        if (!options.Profile.RejectOutputs.TryGetValue(command.NozzleNumber, out var output) ||
            !output.IsEnabled ||
            output.Port < 0)
        {
            throw new InvalidOperationException($"No enabled Techik reject output is mapped to nozzle {command.NozzleNumber}.");
        }

        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (command.TriggerDelayMicroseconds > 0)
            {
                await Task.Delay(TimeSpan.FromTicks(command.TriggerDelayMicroseconds * 10L), cancellationToken);
            }

            using var session = new NativeModbusRtuSession(options);
            session.Connect();
            var activeValue = output.IsActiveHigh ? 1 : 0;
            var inactiveValue = output.IsActiveHigh ? 0 : 1;
            session.WriteBit(output.Port, activeValue);
            try
            {
                await Task.Delay(TimeSpan.FromTicks(Math.Max(1, command.PulseWidthMicroseconds) * 10L), cancellationToken);
            }
            finally
            {
                session.WriteBit(output.Port, inactiveValue);
            }
        }
        finally
        {
            _commandGate.Release();
        }
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
