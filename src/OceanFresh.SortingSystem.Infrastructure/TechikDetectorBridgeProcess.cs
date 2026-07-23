using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed record TechikPeripheralSnapshot(
    bool XrayOnline,
    bool XrayEnabled,
    double XrayVoltageReference,
    double XrayCurrentReference,
    double XrayVoltageMonitor,
    double XrayCurrentMonitor,
    int XrayFaultCode,
    bool MotionOnline,
    bool MotionRunning,
    bool MotionDirection,
    double MotionSpeed,
    bool PrimaryIoOnline,
    bool SecondaryIoOnline)
{
    public static TechikPeripheralSnapshot Empty { get; } =
        new(false, false, 0, 0, 0, 0, 0, false, false, false, 0, false, false);
}

public sealed record TechikDetectorFrame(
    byte[] RawFrame,
    int DetectorId,
    int Width,
    int Height,
    long Sequence,
    DateTimeOffset CapturedAt);

public sealed class TechikDetectorBridgeProcess(TechikIntegrationOptions options) : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> CapturedPluginHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tk_driver_xray.dll"] = "10811669DBBC37BA74BE6C605B206DA33D03F0A9CACEBF936DAC25C825D9CC87",
            ["tk_driver_motion.dll"] = "D106E6523AF2BBBC5440E19EE84F440DF051309C9BF9EBC89FEC0BD81C5E329F",
            ["tk_driver_io.dll"] = "30CCB267CAE838C4F04A81729A81D35F9FC100A398DDA03A2B9EFFCF76B1D87F"
        };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly object _responseGate = new();
    private Process? _process;
    private TaskCompletionSource<bool>? _started;
    private TaskCompletionSource<bool>? _pendingCommand;
    private string? _pendingEvent;
    private NamedPipeServerStream? _framePipe;
    private CancellationTokenSource? _framePumpCancellation;
    private Task? _framePump;
    private Channel<TechikDetectorFrame>? _frames;
    private string _lastMessage = "探测器桥接尚未启动。";
    private bool _detectorStarted;
    private bool _peripheralsConnected;
    private TechikPeripheralSnapshot _peripheralSnapshot = TechikPeripheralSnapshot.Empty;

    public bool IsRunning => _process is { HasExited: false } &&
                             _started?.Task.IsCompletedSuccessfully == true;

    public string LastMessage => _lastMessage;

    public bool PeripheralsConnected => _peripheralsConnected;

    public TechikPeripheralSnapshot PeripheralSnapshot => _peripheralSnapshot;

    public bool IsDirectFrameStreamingEnabled =>
        options.Mode == TechikIntegrationMode.Direct && options.EnableDetector;

    public bool FilesAreReady =>
        File.Exists(options.DetectorBridgeExecutablePath) &&
        File.Exists(Path.Combine(options.DetectorSdkRoot, "tk_driver_dt.dll")) &&
        (!options.EnablePeripherals ||
         CapturedPluginHashes.Keys.All(name =>
             File.Exists(Path.Combine(options.Profile.RootDirectory, "plugin", name))));

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (options.Mode != TechikIntegrationMode.Direct || !options.EnableDetector)
        {
            return;
        }

        if (IsRunning)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }

            if (!FilesAreReady)
            {
                throw new FileNotFoundException(
                    $"Techik 探测器桥接文件不完整。Bridge={options.DetectorBridgeExecutablePath}; SDK={options.DetectorSdkRoot}");
            }

            if (options.EnablePeripherals)
            {
                ValidateCapturedPeripheralRuntime();
            }

            Directory.CreateDirectory(options.DetectorFrameDirectory);
            var framePipeName =
                $"OceanFresh.Techik.Detector.{Environment.ProcessId}.{Guid.NewGuid():N}";
            _framePipe = new NamedPipeServerStream(
                framePipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                1024 * 1024,
                1024 * 1024);
            _framePumpCancellation = new CancellationTokenSource();
            _frames = Channel.CreateBounded<TechikDetectorFrame>(
                new BoundedChannelOptions(options.FrameQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            var pipeConnection = _framePipe.WaitForConnectionAsync(cancellationToken);
            var startup = options.Profile.DetectorStartup;
            var startInfo = new ProcessStartInfo
            {
                FileName = options.DetectorBridgeExecutablePath,
                WorkingDirectory = options.DetectorSdkRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Add(startInfo, "--run-detector");
            Add(startInfo, "--sdk-root", options.DetectorSdkRoot);
            Add(startInfo, "--frame-pipe", framePipeName);
            Add(startInfo, "--output-directory", options.DetectorFrameDirectory);
            Add(startInfo, "--aggregate-height", "300");
            Add(startInfo, "--frame-queue-capacity", options.FrameQueueCapacity);
            Add(startInfo, "--id", startup.Id);
            Add(startInfo, "--type", startup.Type);
            Add(startInfo, "--network-id", startup.NetworkId);
            Add(startInfo, "--sub-card-pixels", startup.SubCardPixels);
            Add(startInfo, "--line-pixels", startup.LinePixels);
            Add(startInfo, "--channels", startup.Channels);
            Add(startInfo, "--pitch-size", startup.PitchSize);
            Add(startInfo, "--sub-frame-height", startup.SubFrameHeight);
            Add(startInfo, "--bound-xray-id", startup.BoundXrayId);
            Add(startInfo, "--scan-direction", startup.ScanDirection ? 1 : 0);
            Add(startInfo, "--dark-tracking", startup.EnableDarkDynamicTracking ? 1 : 0);
            Add(startInfo, "--dark-pixel-start", startup.DarkDynamicPixelStart);
            Add(startInfo, "--dark-pixel-last", startup.DarkDynamicPixelLast);
            Add(startInfo, "--ccd-binning-mode", startup.CcdBinningMode);
            Add(startInfo, "--ccd-dual-shift-pixels", startup.CcdDualShiftPixels);
            Add(startInfo, "--ias-tdi-level", startup.IasTdiLevel);
            Add(startInfo, "--ias-tdi-level-offset", startup.IasTdiLevelOffset);
            Add(startInfo, "--ias-kv-threshold-low", startup.IasKvThresholdLow);
            Add(startInfo, "--ias-kv-threshold-high", startup.IasKvThresholdHigh);
            Add(startInfo, "--ias-soft-binning-mode", startup.IasSoftBinningMode);
            Add(startInfo, "--calibration-type", startup.CalibrationType);
            Add(startInfo, "--calibration-dark-lines", startup.CalibrationDarkLines);
            Add(startInfo, "--calibration-full-lines", startup.CalibrationFullLines);
            Add(startInfo, "--calibration-dark-target", startup.CalibrationDarkTarget);
            Add(startInfo, "--calibration-full-target", startup.CalibrationFullTarget);
            if (options.EnablePeripherals)
            {
                var xray = options.Profile.XrayStartup;
                var motion = options.Profile.MotionStartup;
                var primaryIo = options.Profile.IoModules.FirstOrDefault(x => x.Type == 3)
                    ?? throw new InvalidOperationException("Techik type=3 IO module is missing from sys_config.ini.");
                var secondaryIo = options.Profile.IoModules.FirstOrDefault(x => x.Type == 7)
                    ?? throw new InvalidOperationException("Techik type=7 IO module is missing from sys_config.ini.");

                Add(startInfo, "--run-peripherals");
                Add(startInfo, "--runtime-root", options.Profile.RootDirectory);
                Add(startInfo, "--xray-id", xray.Id);
                Add(startInfo, "--xray-type", xray.Type);
                Add(startInfo, "--xray-com-port", xray.ComPort);
                Add(startInfo, "--xray-max-voltage-kv", xray.MaxVoltageKv);
                Add(startInfo, "--xray-min-voltage-kv", xray.MinVoltageKv);
                Add(startInfo, "--xray-max-current-ua", xray.MaxCurrentUa);
                Add(startInfo, "--xray-min-current-ua", xray.MinCurrentUa);
                Add(startInfo, "--xray-max-power-limit", xray.MaxPowerLimit);
                Add(startInfo, "--xray-min-power-limit", xray.MinPowerLimit);
                Add(startInfo, "--xray-open-wait-ms", xray.OpenWaitStableTimeMilliseconds);
                Add(startInfo, "--xray-close-wait-ms", xray.CloseWaitStableTimeMilliseconds);
                Add(startInfo, "--motion-id", motion.Id);
                Add(startInfo, "--motion-type", motion.Type);
                Add(startInfo, "--motion-com-port", motion.ComPort);
                Add(startInfo, "--motion-speed-max", motion.SpeedMax);
                Add(startInfo, "--motion-speed-min", motion.SpeedMin);
                Add(startInfo, "--motion-speed-ratio", motion.SpeedRatio);
                Add(startInfo, "--motion-direction-reverse", motion.DirectionReverse ? 1 : 0);
                Add(startInfo, "--motion-plc-id", motion.PlcId);
                Add(startInfo, "--motion-plc-run-port-forward", motion.PlcRunPortForward);
                Add(startInfo, "--motion-plc-run-port-reverse", motion.PlcRunPortReverse);
                Add(startInfo, "--motion-plc-monitor-port-run", motion.PlcMonitorPortRun);
                Add(startInfo, "--motion-plc-analog-id", motion.PlcAnalogId);
                Add(startInfo, "--io-primary-id", primaryIo.Id);
                Add(startInfo, "--io-primary-type", primaryIo.Type);
                Add(startInfo, "--io-primary-com-port", primaryIo.ComPort);
                Add(startInfo, "--io-secondary-id", secondaryIo.Id);
                Add(startInfo, "--io-secondary-type", secondaryIo.Type);
                Add(startInfo, "--io-secondary-com-port", secondaryIo.ComPort);
            }

            _detectorStarted = false;
            _peripheralsConnected = false;
            _started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += HandleOutput;
            _process.ErrorDataReceived += HandleError;
            _process.Exited += HandleExit;
            if (!_process.Start())
            {
                throw new InvalidOperationException("TechikDetectorBridge 进程启动失败。");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            await pipeConnection.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            _framePump = PumpFramesAsync(
                _framePipe,
                _frames.Writer,
                _framePumpCancellation.Token);
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch
        {
            StopProcess();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartMachineAsync(CancellationToken cancellationToken)
    {
        EnsureOutputEnabled();
        await EnsureStartedAsync(cancellationToken);
        var setpoints = options.ProductionSetpoints;
        await SendCommandAsync(
            FormattableString.Invariant(
                $"machine_start {setpoints.XrayVoltageKv:R} {setpoints.XrayCurrentUa:R} {setpoints.ConveyorSpeed:R} {(setpoints.ConveyorDirection ? 1 : 0)}"),
            "machine_started",
            cancellationToken);
    }

    public async Task StopMachineAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning || !options.EnablePeripherals)
        {
            return;
        }
        await SendCommandAsync("machine_stop", "machine_stopped", cancellationToken);
    }

    public async Task<TechikPeripheralSnapshot> RefreshStatusAsync(
        CancellationToken cancellationToken)
    {
        if (!IsRunning || !options.EnablePeripherals)
        {
            return _peripheralSnapshot;
        }
        await SendCommandAsync("status", "peripheral_status", cancellationToken);
        return _peripheralSnapshot;
    }

    public async Task PulseRejectAsync(
        int port,
        bool activeHigh,
        long pulseMicroseconds,
        CancellationToken cancellationToken)
    {
        EnsureOutputEnabled();
        await EnsureStartedAsync(cancellationToken);
        await SendCommandAsync(
            FormattableString.Invariant(
                $"eject {port} {(activeHigh ? 1 : 0)} {pulseMicroseconds}"),
            "eject_completed",
            cancellationToken);
    }

    public async Task<TechikDetectorFrame?> ReadFrameAsync(
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        var frames = _frames
            ?? throw new InvalidOperationException("Techik 探测器实时帧通道尚未建立。");
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            return await frames.Reader.ReadAsync(readTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException exception)
        {
            StopProcess();
            throw new InvalidOperationException(
                "Techik 探测器实时帧通道已关闭。",
                exception.InnerException ?? exception);
        }
    }

    public void Dispose()
    {
        StopProcess();
        _gate.Dispose();
        _commandGate.Dispose();
    }

    private void HandleOutput(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }

        _lastMessage = args.Data;
        try
        {
            using var document = JsonDocument.Parse(args.Data);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var eventProperty))
            {
                return;
            }

            var eventName = eventProperty.GetString();
            switch (eventName)
            {
                case "detector_started":
                    _detectorStarted = true;
                    TryCompleteStartup();
                    break;
                case "peripherals_connected":
                    _peripheralsConnected = true;
                    TryCompleteStartup();
                    break;
                case "peripheral_status":
                    _peripheralSnapshot = new TechikPeripheralSnapshot(
                        ReadBoolean(root, "xray_online"),
                        ReadBoolean(root, "xray_enabled"),
                        ReadDouble(root, "xray_voltage_ref"),
                        ReadDouble(root, "xray_current_ref"),
                        ReadDouble(root, "xray_voltage_mon"),
                        ReadDouble(root, "xray_current_mon"),
                        ReadInt(root, "xray_fault"),
                        ReadBoolean(root, "motion_online"),
                        ReadBoolean(root, "motion_running"),
                        ReadBoolean(root, "motion_direction"),
                        ReadDouble(root, "motion_speed"),
                        ReadBoolean(root, "io_primary_online"),
                        ReadBoolean(root, "io_secondary_online"));
                    break;
            }

            CompletePendingCommand(eventName);
        }
        catch (JsonException)
        {
        }
    }

    private void HandleError(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }

        _lastMessage = args.Data;
        if (args.Data.Contains("\"event\":\"fatal\"", StringComparison.Ordinal))
        {
            _started?.TrySetException(new InvalidOperationException(args.Data));
        }
        if (args.Data.Contains("\"event\":\"command_error\"", StringComparison.Ordinal))
        {
            lock (_responseGate)
            {
                _pendingCommand?.TrySetException(new InvalidOperationException(args.Data));
            }
        }
    }

    private void HandleExit(object? sender, EventArgs args)
    {
        var exitCode = _process?.ExitCode ?? -1;
        _started?.TrySetException(
            new InvalidOperationException($"TechikDetectorBridge 已退出，退出码 {exitCode}。{_lastMessage}"));
    }

    private void StopProcess()
    {
        var framePumpCancellation = _framePumpCancellation;
        var framePipe = _framePipe;
        var frames = _frames;
        _framePumpCancellation = null;
        _framePipe = null;
        _framePump = null;
        _frames = null;
        framePumpCancellation?.Cancel();
        framePipe?.Dispose();
        frames?.Writer.TryComplete();
        framePumpCancellation?.Dispose();

        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("stop");
                process.StandardInput.Flush();
                if (!process.WaitForExit(3_000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            process.Dispose();
            _detectorStarted = false;
            _peripheralsConnected = false;
            _peripheralSnapshot = TechikPeripheralSnapshot.Empty;
        }
    }

    private static async Task PumpFramesAsync(
        Stream stream,
        ChannelWriter<TechikDetectorFrame> writer,
        CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                writer.TryWrite(await TechikRawFrameCodec.ReadFrameAsync(
                    stream,
                    cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            completionError = exception;
        }
        finally
        {
            writer.TryComplete(completionError);
        }
    }

    private async Task SendCommandAsync(
        string command,
        string expectedEvent,
        CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var process = _process;
            if (process is null || process.HasExited)
            {
                throw new InvalidOperationException("TechikDetectorBridge 未运行。");
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_responseGate)
            {
                _pendingEvent = expectedEvent;
                _pendingCommand = completion;
            }

            await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        finally
        {
            lock (_responseGate)
            {
                _pendingEvent = null;
                _pendingCommand = null;
            }
            _commandGate.Release();
        }
    }

    private void TryCompleteStartup()
    {
        if (_detectorStarted && (!options.EnablePeripherals || _peripheralsConnected))
        {
            _started?.TrySetResult(true);
        }
    }

    private void CompletePendingCommand(string? eventName)
    {
        lock (_responseGate)
        {
            if (string.Equals(eventName, _pendingEvent, StringComparison.Ordinal))
            {
                _pendingCommand?.TrySetResult(true);
            }
        }
    }

    private void EnsureOutputEnabled()
    {
        if (!options.EnablePeripherals || !options.EnableHardwareOutput)
        {
            throw new InvalidOperationException(
                "Techik physical output is safety-locked. Enable the captured peripheral runtime and hardware output after field verification.");
        }
    }

    private void ValidateCapturedPeripheralRuntime()
    {
        foreach (var expected in CapturedPluginHashes)
        {
            var path = Path.Combine(options.Profile.RootDirectory, "plugin", expected.Key);
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Techik 插件版本不匹配，拒绝使用固定 ABI：{expected.Key}; SHA256={actual}");
            }
        }
    }

    private static bool ReadBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static double ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed)
            ? parsed
            : 0;

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static void Add(ProcessStartInfo startInfo, string name) =>
        startInfo.ArgumentList.Add(name);

    private static void Add(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static void Add(ProcessStartInfo startInfo, string name, int value) =>
        Add(startInfo, name, value.ToString(CultureInfo.InvariantCulture));

    private static void Add(ProcessStartInfo startInfo, string name, double value) =>
        Add(startInfo, name, value.ToString("R", CultureInfo.InvariantCulture));
}
