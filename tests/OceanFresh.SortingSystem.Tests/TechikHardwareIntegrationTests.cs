using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.Infrastructure;

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
            File.WriteAllText(Path.Combine(root, "cfg", "config.ini"), "[SYSTEM]\nname=machine-a\n");
            File.WriteAllText(
                Path.Combine(root, "cfg", "misc_config.ini"),
                "[HISTORY_IMAGE_SAVE]\nsave_path=C:/TechikHistory/img\nsave_image=true\n");
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
            Assert.Equal(0, profile.DetectorType);
            Assert.Equal(0, profile.DetectorNetworkId);
            Assert.Equal(5, profile.XrayComPort);
            Assert.Equal(6, profile.XrayType);
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
        var controller = new TechikEjectorController(options);
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
