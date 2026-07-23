using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Tests;

public sealed class DeviceAndAlarmWorkflowTests
{
    [Fact]
    public async Task HardwareDeviceService_RejectsDuplicateDeviceNo()
    {
        var existingId = Guid.NewGuid();
        var repository = new FakeHardwareDeviceRepository(
        [
            new HardwareDevice(
                existingId,
                "CV-001",
                "传送带",
                DeviceType.Conveyor,
                "FW-CV-1.0.0",
                DeviceState.Idle,
                null,
                "尚未自检",
                true,
                "")
        ]);
        var service = new HardwareDeviceService(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new UpsertHardwareDeviceRequest(
                    null,
                    "CV-001",
                    "重复传送带",
                    DeviceType.Conveyor,
                    "FW-CV-1.0.1",
                    DeviceState.Idle,
                    true,
                    ""),
                CancellationToken.None));

        Assert.Equal("设备编号已存在，必须全局唯一。", exception.Message);
    }

    [Fact]
    public async Task HardwareDeviceService_RunSelfCheck_UpdatesResult()
    {
        var deviceId = Guid.NewGuid();
        var repository = new FakeHardwareDeviceRepository(
        [
            new HardwareDevice(
                deviceId,
                "XR-001",
                "X 光探测器",
                DeviceType.XrayDetector,
                "FW-XR-1.0.0",
                DeviceState.Idle,
                null,
                "尚未自检",
                true,
                "")
        ]);
        var service = new HardwareDeviceService(repository);

        var result = await service.RunSelfCheckAsync(deviceId, CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("自检通过，设备通讯与基础状态正常。", result.Message);
        Assert.NotNull(repository.Items.Single().LastSelfCheckAt);
        Assert.Equal(DeviceState.Idle, repository.Items.Single().State);
    }

    [Fact]
    public async Task AlarmService_Acknowledge_RemovesAlarmFromActiveList()
    {
        var alarmId = Guid.NewGuid();
        var repository = new FakeAlarmRepository(
        [
            new AlarmEvent(
                alarmId,
                AlarmSeverity.Warning,
                "EJ-001",
                "EJECTOR-WARN",
                "剔除设备响应延迟偏高",
                DateTimeOffset.UtcNow,
                false)
        ]);
        var service = new AlarmService(repository);

        await service.AcknowledgeAsync(alarmId, CancellationToken.None);

        Assert.Empty(await service.GetActiveAsync(CancellationToken.None));
    }

    private sealed class FakeHardwareDeviceRepository(IReadOnlyList<HardwareDevice> seedItems) : IHardwareDeviceRepository
    {
        public List<HardwareDevice> Items { get; } = seedItems.ToList();

        public Task<IReadOnlyList<HardwareDevice>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>(Items.OrderBy(x => x.DeviceNo).ToList());

        public Task<HardwareDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<HardwareDevice?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<HardwareDevice> UpsertAsync(HardwareDevice device, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == device.Id);
            if (existing >= 0)
            {
                Items[existing] = device;
            }
            else
            {
                Items.Add(device);
            }

            return Task.FromResult(device);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAlarmRepository(IReadOnlyList<AlarmEvent> seedItems) : IAlarmRepository
    {
        private readonly List<AlarmEvent> _items = seedItems.ToList();

        public Task AddAsync(AlarmEvent alarmEvent, CancellationToken cancellationToken)
        {
            _items.Add(alarmEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmEvent>> GetActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlarmEvent>>(_items.Where(x => !x.IsAcknowledged).ToList());

        public Task AcknowledgeAsync(Guid alarmId, CancellationToken cancellationToken)
        {
            var index = _items.FindIndex(x => x.Id == alarmId);
            if (index >= 0)
            {
                _items[index] = _items[index] with { IsAcknowledged = true };
            }

            return Task.CompletedTask;
        }
    }
}
