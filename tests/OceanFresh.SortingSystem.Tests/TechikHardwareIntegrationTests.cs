using System.Buffers.Binary;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OceanFresh.SortingSystem.Tests;

public sealed class TechikHardwareIntegrationTests
{
    [Fact]
    public void ProfileReader_ImportsCapturedTechikHardwareConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oceanfresh-techik-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "cfg", "machine-a"));
            Directory.CreateDirectory(Path.Combine(root, "product", "008"));
            File.WriteAllText(Path.Combine(root, "cfg", "config.ini"), "[SYSTEM]\nname=machine-a\n");
            File.WriteAllText(
                Path.Combine(root, "cfg", "misc_config.ini"),
                "[HISTORY_IMAGE_SAVE]\nsave_path=C:/TechikHistory/img\nsave_image=true\n");
            File.WriteAllText(
                Path.Combine(root, "product", "config.ini"),
                "[PRODUCT]\nid=8\n");
            File.WriteAllText(
                Path.Combine(root, "product", "008", "prod_param.ini"),
                """
                [PROD_PARAM_XRAY]
                xray_0=@ByteArray(\x32\x00\x00\x00\xd5\x14\x00\x00)
                [PROD_PARAM_MOTION]
                motion_0=@ByteArray(\x01\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x80\x56\x40)
                """);
            File.WriteAllText(
                Path.Combine(root, "cfg", "machine-a", "sys_config.ini"),
                """
                [DETECTOR]
                dt_num=1
                [DETECTOR_0]
                type=0
                line_pixels=1536
                sub_frame_height=50
                tk_net_id=0
                [XRAY_TUBE_0]
                type=6
                com_port=5
                max_voltage_kv=60
                min_voltage_kv=30
                max_current_ua=8000
                min_current_ua=500
                max_power_limit=350000
                min_power_limit=6000
                open_wait_stable_time_ms=8000
                close_wait_stable_time_ms=5000
                [IO_MODULE]
                io_num=2
                [IO_MODULE_0]
                id=0
                type=3
                com_port=3
                [IO_MODULE_1]
                id=1
                type=7
                com_port=1
                [MOTION_0]
                type=4
                com_port=1
                speed_max=120
                speed_min=5
                speed_ratio=23
                dir_reverse=false
                plc_id=0
                plc_run_port_fwd=9
                plc_run_port_rev=8
                plc_mon_port_run=7
                plc_analog_id=0
                [DT_XRAY_GEOMETRIC]
                dis_xray_dt=830
                dis_coy_dt=35
                size_reject=604
                [REJECT_BULK_AIR]
                num=2
                dir=true
                unit_0_enable=true
                unit_0_port=1000
                unit_0_signal_polarity=false
                unit_1_enable=true
                unit_1_port=1001
                unit_1_signal_polarity=true
                """);

            var profile = TechikInstallationProfileReader.Load(root);

            Assert.Equal("machine-a", profile.ActiveProfileName);
            Assert.Equal(1, profile.DetectorCount);
            Assert.Equal(1536, profile.DetectorLinePixels);
            Assert.Equal(50, profile.DetectorSubFrameHeight);
            Assert.Equal(128, profile.DetectorStartup.SubCardPixels);
            Assert.Equal(1536, profile.DetectorStartup.LinePixels);
            Assert.Equal(1, profile.DetectorStartup.Channels);
            Assert.Equal(0.4, profile.DetectorStartup.PitchSize);
            Assert.Equal(250, profile.DetectorStartup.CalibrationDarkLines);
            Assert.Equal(1000, profile.DetectorStartup.CalibrationFullLines);
            Assert.Equal(52428, profile.DetectorStartup.CalibrationFullTarget);
            Assert.Equal(0, profile.DetectorType);
            Assert.Equal(0, profile.DetectorNetworkId);
            Assert.Equal(5, profile.XrayComPort);
            Assert.Equal(6, profile.XrayType);
            Assert.Equal(60, profile.XrayStartup.MaxVoltageKv);
            Assert.Equal(30, profile.XrayStartup.MinVoltageKv);
            Assert.Equal(8000, profile.XrayStartup.MaxCurrentUa);
            Assert.Equal(500, profile.XrayStartup.MinCurrentUa);
            Assert.Equal(8000, profile.XrayStartup.OpenWaitStableTimeMilliseconds);
            Assert.Equal(5000, profile.XrayStartup.CloseWaitStableTimeMilliseconds);
            Assert.Equal(3, profile.IoComPort);
            Assert.Collection(
                profile.IoModules,
                module =>
                {
                    Assert.Equal(0, module.Id);
                    Assert.Equal(3, module.Type);
                    Assert.Equal(3, module.ComPort);
                },
                module =>
                {
                    Assert.Equal(1, module.Id);
                    Assert.Equal(7, module.Type);
                    Assert.Equal(1, module.ComPort);
                });
            Assert.Equal(1, profile.ConveyorComPort);
            Assert.Equal(4, profile.ConveyorType);
            Assert.Equal(120, profile.MotionStartup.SpeedMax);
            Assert.Equal(5, profile.MotionStartup.SpeedMin);
            Assert.Equal(23, profile.MotionStartup.SpeedRatio);
            Assert.Equal(9, profile.MotionStartup.PlcRunPortForward);
            Assert.Equal(8, profile.MotionStartup.PlcRunPortReverse);
            Assert.Equal(7, profile.MotionStartup.PlcMonitorPortRun);
            Assert.Equal(50, profile.ProductionSetpoints.XrayVoltageKv);
            Assert.Equal(5333, profile.ProductionSetpoints.XrayCurrentUa);
            Assert.Equal(90, profile.ProductionSetpoints.ConveyorSpeed);
            Assert.True(profile.ProductionSetpoints.ConveyorDirection);
            Assert.True(profile.RejectDirectionForward);
            Assert.Equal(830m, profile.XrayToDetectorDistanceMillimeters);
            Assert.Equal(604m, profile.RejectorSizeMillimeters);
            Assert.Equal(1000, profile.RejectOutputs[1].Port);
            Assert.False(profile.RejectOutputs[1].IsActiveHigh);
            Assert.Equal(1001, profile.RejectOutputs[2].Port);
            Assert.True(profile.RejectOutputs[2].IsActiveHigh);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RawFrameCodec_ConvertsTechikLittleEndianL16FrameToPng()
    {
        var raw = new byte[40 + 4 * sizeof(ushort)];
        raw[0] = (byte)'O';
        raw[1] = (byte)'F';
        raw[2] = (byte)'R';
        raw[3] = (byte)'1';
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(12, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(16, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(40, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(42, 2), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(44, 2), 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(46, 2), ushort.MaxValue);

        var png = await TechikRawFrameCodec.EncodePngAsync(raw, CancellationToken.None);
        using var image = Image.Load<L16>(png);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal((ushort)0, image[0, 0].PackedValue);
        Assert.Equal((ushort)1024, image[1, 0].PackedValue);
        Assert.Equal((ushort)32768, image[0, 1].PackedValue);
        Assert.Equal(ushort.MaxValue, image[1, 1].PackedValue);
    }

    [Fact]
    public async Task EjectorController_RefusesOutputUntilSafetyFlagsAreConfirmed()
    {
        var profile = new TechikInstallationProfile(
            @"C:\Techik",
            @"C:\Techik\Techik.exe",
            "prod_default",
            @"C:\Techik\cfg\prod_default\sys_config.ini",
            @"C:\Techik\cfg\misc_config.ini",
            @"D:\History\img",
            1,
            1536,
            50,
            5,
            3,
            1,
            830m,
            35m,
            604m,
            new Dictionary<int, TechikRejectOutput>
            {
                [1] = new(1, 1000, true, false)
            },
            DetectorType: 0,
            DetectorNetworkId: 0,
            XrayType: 6,
            ConveyorType: 4,
            RejectDirectionForward: true,
            IoModules: [new TechikIoModuleProfile(0, 3, 3), new TechikIoModuleProfile(1, 7, 1)]);
        var options = new TechikIntegrationOptions(
            TechikIntegrationMode.Direct,
            profile,
            EnableHardwareOutput: false,
            DirectProtocolConfirmed: false,
            DirectPortMapConfirmed: false,
            "COM3",
            38_400,
            'N',
            8,
            2,
            1,
            @"C:\Techik\modbus_x64.dll");
        using var bridge = new TechikDetectorBridgeProcess(options);
        var controller = new TechikEjectorController(options, bridge);
        var command = new EjectCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "foreign",
            DefectHandlingAction.AirJet,
            1,
            0,
            0,
            50_000,
            DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ExecuteAsync(command, CancellationToken.None));

        Assert.Contains("safety-locked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
