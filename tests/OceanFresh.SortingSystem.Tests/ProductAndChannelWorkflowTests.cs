using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.Infrastructure;

namespace OceanFresh.SortingSystem.Tests;

public sealed class ProductAndChannelWorkflowTests
{
    [Fact]
    public async Task DeleteProductAsync_ThrowsReadableMessage_WhenProductIsStillBoundToChannel()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9001", "测试油蛤", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
        ]);
        var traitRepository = new FakeSeafoodTraitRepository();
        var channelRepository = new FakeChannelConfigRepository([
            CreateChannelConfig(productId)
        ]);
        var service = new SeafoodProductService(productRepository, traitRepository, channelRepository, new FakeModelRegistryRepository([]), new FakeProductPredictConfigStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(productId, CancellationToken.None));

        Assert.Equal("该海鲜产品仍被通道使用，不能删除。请先解除通道绑定。", exception.Message);
    }

    [Fact]
    public async Task DeleteProductAsync_DeletesProduct_WhenNoChannelUsesIt()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9002", "可删产品", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
        ]);
        var traitRepository = new FakeSeafoodTraitRepository();
        var channelRepository = new FakeChannelConfigRepository([]);
        var service = new SeafoodProductService(productRepository, traitRepository, channelRepository, new FakeModelRegistryRepository([]), new FakeProductPredictConfigStore());

        await service.DeleteAsync(productId, CancellationToken.None);

        Assert.DoesNotContain(productRepository.Items, x => x.Id == productId);
    }

    [Fact]
    public async Task UpsertProductAsync_SavesNormalAndDefectTraits()
    {
        var productRepository = new FakeSeafoodProductRepository([]);
        var traitRepository = new FakeSeafoodTraitRepository();
        var channelRepository = new FakeChannelConfigRepository([]);
        var service = new SeafoodProductService(productRepository, traitRepository, channelRepository, new FakeModelRegistryRepository([]), new FakeProductPredictConfigStore());

        var result = await service.UpsertAsync(
            new UpsertSeafoodProductRequest(
                null,
                "SP-9003",
                "花蛤测试",
                true,
                @"E:\classes\test.txt",
                """{"0":"正常","1":"碎壳","2":"泥包","3":"空壳"}""",
                null,
                null,
                [
                    new UpsertSeafoodTraitRequest("正常", true),
                    new UpsertSeafoodTraitRequest("碎壳", false),
                    new UpsertSeafoodTraitRequest("泥包", false)
                ]),
            CancellationToken.None);

        Assert.Equal("SP-0001", result.Product.Code);
        Assert.Equal(3, result.Traits.Count);
        Assert.Contains(result.Traits, x => x.Name == "正常" && x.IsNormal);
        Assert.Contains(result.Traits, x => x.Name == "碎壳" && !x.IsNormal);
        Assert.Contains(result.Traits, x => x.Name == "泥包" && !x.IsNormal);
    }

    [Fact]
    public async Task UpsertAndDeleteChannelAsync_CompletesRoundTrip()
    {
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var channelRepository = new FakeChannelConfigRepository([]);
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9004", "美贝测试", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
        ]);
        var modelRepository = new FakeModelRegistryRepository([
            new ModelVersion(
                modelId,
                Guid.NewGuid(),
                "MB-MV-001",
                "mb.pt",
                "美贝模型",
                ModelStatus.Normal,
                DateTimeOffset.UtcNow)
        ]);
        var runtimeStateStore = new FakeRuntimeStateStore();
        var service = new ChannelConfigService(channelRepository, productRepository, modelRepository, runtimeStateStore);

        var saved = await service.UpsertAsync(
            new UpsertChannelConfigRequest(
                null,
                3,
                "3号通道",
                productId,
                modelId,
                DefectHandlingAction.Sink,
                0.61m,
                true),
            CancellationToken.None);

        Assert.Single(channelRepository.Items);
        Assert.Equal("3号通道", saved.Name);
        Assert.Equal("mb.pt", saved.ModelPath);

        await service.DeleteAsync(saved.Id, CancellationToken.None);

        Assert.Empty(channelRepository.Items);
    }

    [Fact]
    public async Task SelectActiveChannelAsync_ActivatesSelectedChannelAndReleasesPreviousChannel()
    {
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var firstChannel = CreateChannelConfig(productId) with
        {
            ModelVersionId = modelId,
            IsEnabled = true
        };
        var secondChannel = CreateChannelConfig(productId) with
        {
            Id = Guid.NewGuid(),
            ChannelNo = 2,
            Name = "2号通道",
            ModelVersionId = modelId,
            IsEnabled = false
        };
        var channelRepository = new FakeChannelConfigRepository([firstChannel, secondChannel]);
        var service = new ChannelConfigService(
            channelRepository,
            new FakeSeafoodProductRepository([
                new SeafoodProduct(productId, "SP-9012", "油蛤", true, "", "{}", "")
            ]),
            new FakeModelRegistryRepository([
                new ModelVersion(modelId, Guid.NewGuid(), "YG-MV-003", "yg.pt", "", ModelStatus.Normal, DateTimeOffset.UtcNow)
            ]),
            new FakeRuntimeStateStore());

        var selected = await service.SelectActiveAsync(secondChannel.Id, CancellationToken.None);

        Assert.True(selected.IsEnabled);
        Assert.False(channelRepository.Items.Single(x => x.Id == firstChannel.Id).IsEnabled);
        Assert.True(channelRepository.Items.Single(x => x.Id == secondChannel.Id).IsEnabled);
    }

    [Fact]
    public async Task DashboardUsesEnabledChannel_WhenMachineIsStoppedAfterChannelSwitch()
    {
        var previousProductId = Guid.NewGuid();
        var activeProductId = Guid.NewGuid();
        var previousModelId = Guid.NewGuid();
        var activeModelId = Guid.NewGuid();
        var previousChannel = CreateChannelConfig(previousProductId) with
        {
            ModelVersionId = previousModelId,
            IsEnabled = false
        };
        var activeChannel = CreateChannelConfig(activeProductId) with
        {
            Id = Guid.NewGuid(),
            ChannelNo = 2,
            Name = "2号通道",
            ModelVersionId = activeModelId,
            IsEnabled = true
        };
        var sessions = new FakeDetectionSessionRepository();
        await sessions.AddAsync(
            new DetectionSession(
                Guid.NewGuid(),
                "DS-PREVIOUS",
                previousChannel.Id,
                previousProductId,
                previousModelId,
                DateTimeOffset.UtcNow.AddMinutes(-10),
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DetectionSessionStatus.Stopped),
            CancellationToken.None);
        var dashboardService = new RuntimeDashboardService(
            new FakeRuntimeStateStore(RuntimeMode.Stopped),
            new InMemoryRuntimeDataSourceStore(),
            new FakeChannelConfigRepository([previousChannel, activeChannel]),
            new FakeSeafoodProductRepository([
                new SeafoodProduct(previousProductId, "SP-OLD", "旧产品", true, "", "{}", ""),
                new SeafoodProduct(activeProductId, "SP-NEW", "新产品", true, "", "{}", "")
            ]),
            new FakeModelRegistryRepository([
                new ModelVersion(previousModelId, previousProductId, "OLD-MODEL", "old.pt", "", ModelStatus.Normal, DateTimeOffset.UtcNow),
                new ModelVersion(activeModelId, activeProductId, "NEW-MODEL", "new.pt", "", ModelStatus.Normal, DateTimeOffset.UtcNow)
            ]),
            new FakeInspectionRecordRepository(),
            sessions);

        var dashboard = await dashboardService.GetDashboardAsync(CancellationToken.None);

        Assert.Equal("2号通道", dashboard.Summary.CurrentChannel);
        Assert.Equal("新产品", dashboard.Summary.CurrentProduct);
        Assert.Equal("NEW-MODEL", dashboard.Summary.CurrentModel);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsChannelSwitch_WhenMachineRunning()
    {
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var channelRepository = new FakeChannelConfigRepository([
            CreateChannelConfig(productId) with
            {
                Id = channelId,
                ModelVersionId = modelId,
                IsEnabled = false
            }
        ]);
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9011", "油蛤", true, "", "{}", "")
        ]);
        var modelRepository = new FakeModelRegistryRepository([
            new ModelVersion(modelId, Guid.NewGuid(), "YG-MV-002", "yg.pt", "", ModelStatus.Normal, DateTimeOffset.UtcNow)
        ]);
        var runtimeStateStore = new FakeRuntimeStateStore(RuntimeMode.Running);
        var service = new ChannelConfigService(channelRepository, productRepository, modelRepository, runtimeStateStore);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new UpsertChannelConfigRequest(
                    channelId,
                    1,
                    "1号通道",
                    productId,
                    modelId,
                    DefectHandlingAction.Sink,
                    0.58m,
                    true),
                CancellationToken.None));

        Assert.Equal("机器运行中不可切换通道，请先停止机器。", exception.Message);
    }

    [Fact]
    public async Task RuntimeCoordinator_StartDetection_RequiresMachineRunning()
    {
        var coordinator = new RuntimeCoordinator(
            new FakeRuntimeStateStore(),
            new InMemoryRuntimeDataSourceStore(),
            new FakeChannelConfigRepository([]),
            new FakeModelRegistryRepository([]),
            new FakeDetectionSessionRepository(),
            new FakeHardwareInterlockService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartDetectionAsync(CancellationToken.None));

        Assert.Equal("机器尚未启动，无法开始检测。", exception.Message);
    }

    [Fact]
    public async Task RuntimeCoordinator_StartMachine_AutomaticallyCreatesDetectionSession()
    {
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sessionRepository = new FakeDetectionSessionRepository();
        var coordinator = new RuntimeCoordinator(
            new FakeRuntimeStateStore(),
            new InMemoryRuntimeDataSourceStore(),
            new FakeChannelConfigRepository([
                new ChannelConfig(
                    channelId,
                    1,
                    "1号通道",
                    productId,
                    modelId,
                    "yg.pt",
                    DefectHandlingAction.Sink,
                    0.58m,
                    true,
                    null,
                    null,
                    420m,
                    1m,
                    40,
                    25,
                    "[]")
            ]),
            new FakeModelRegistryRepository([
                new ModelVersion(modelId, Guid.NewGuid(), "YG-MV-003", "yg.pt", "", ModelStatus.Normal, DateTimeOffset.UtcNow)
            ]),
            sessionRepository,
            new FakeHardwareInterlockService());

        await coordinator.StartAsync(CancellationToken.None);
        var session = await sessionRepository.GetActiveAsync(CancellationToken.None);
        var repeatedStartException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartDetectionAsync(CancellationToken.None));
        var stopped = await coordinator.StopDetectionAsync(CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("当前已有检测任务运行中，请先停止检测。", repeatedStartException.Message);
        Assert.Equal(DetectionSessionStatus.Running, session!.Status);
        Assert.Single(sessionRepository.Items);
        Assert.NotNull(stopped);
        Assert.Equal(DetectionSessionStatus.Stopped, stopped!.Status);
        Assert.NotNull(stopped.EndedAt);
    }

    [Fact]
    public async Task RuntimeCoordinator_StartMachine_BlocksWhenHardwareInterlockFails()
    {
        var stateStore = new FakeRuntimeStateStore();
        var coordinator = new RuntimeCoordinator(
            stateStore,
            new InMemoryRuntimeDataSourceStore(),
            new FakeChannelConfigRepository([]),
            new FakeModelRegistryRepository([]),
            new FakeDetectionSessionRepository(),
            new FakeHardwareInterlockService(false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(CancellationToken.None));

        Assert.Equal("存在 1 条关键硬件联锁告警，系统禁止运行。", exception.Message);
        Assert.Equal(RuntimeMode.Faulted, stateStore.GetSnapshot().RuntimeMode);
        Assert.Equal(DeviceState.Faulted, stateStore.GetSnapshot().DeviceState);
    }

    [Fact]
    public async Task RuntimeCoordinator_Reset_RequiresStoppedMachine_AndClearsCurrentRunState()
    {
        var runningStore = new FakeRuntimeStateStore(RuntimeMode.Running);
        var runningCoordinator = new RuntimeCoordinator(
            runningStore,
            new InMemoryRuntimeDataSourceStore(),
            new FakeChannelConfigRepository([]),
            new FakeModelRegistryRepository([]),
            new FakeDetectionSessionRepository(),
            new FakeHardwareInterlockService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runningCoordinator.ResetAsync(CancellationToken.None));

        Assert.Equal("设备运行中无法重置，请先停止设备。", exception.Message);

        var stoppedStore = new FakeRuntimeStateStore(RuntimeMode.SafeStop);
        stoppedStore.Update(stoppedStore.GetSnapshot() with
        {
            CurrentBatch = "DS-TEST",
            TotalInspected = 12,
            TotalRejected = 3,
            YieldRate = 75m
        });
        var stoppedCoordinator = new RuntimeCoordinator(
            stoppedStore,
            new InMemoryRuntimeDataSourceStore(),
            new FakeChannelConfigRepository([]),
            new FakeModelRegistryRepository([]),
            new FakeDetectionSessionRepository(),
            new FakeHardwareInterlockService());

        await stoppedCoordinator.ResetAsync(CancellationToken.None);
        var reset = stoppedStore.GetSnapshot();

        Assert.Equal(RuntimeMode.Stopped, reset.RuntimeMode);
        Assert.Equal(DeviceState.Idle, reset.DeviceState);
        Assert.Equal(string.Empty, reset.CurrentBatch);
        Assert.Equal(0, reset.TotalInspected);
        Assert.Equal(0, reset.TotalRejected);
        Assert.Equal(100m, reset.YieldRate);
        Assert.Null(reset.MachineStartedAt);
    }

    [Fact]
    public void RuntimeDataSourceService_DefaultsToXrayCamera()
    {
        var service = new RuntimeDataSourceService(
            new InMemoryRuntimeDataSourceStore(),
            new FakeRuntimeStateStore());

        var config = service.Get();

        Assert.Equal(RuntimeDataSourceMode.XrayCamera, config.Mode);
        Assert.Equal("X 光相机", config.ModeText);
        Assert.True(config.IsHardwareExecutionEnabled);
        Assert.Equal(0, config.ImageCount);
    }

    [Fact]
    public async Task RuntimeDataSourceService_LocalDirectory_DisablesHardwareExecution()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"oceanfresh-local-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tempDirectory, "frame-001.png"), [1, 2, 3], CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(tempDirectory, "notes.txt"), "ignore", CancellationToken.None);

            var service = new RuntimeDataSourceService(
                new InMemoryRuntimeDataSourceStore(),
                new FakeRuntimeStateStore());

            var config = service.Update(new UpdateRuntimeDataSourceRequest(
                RuntimeDataSourceMode.LocalImageDirectory,
                tempDirectory,
                12));

            Assert.Equal(RuntimeDataSourceMode.LocalImageDirectory, config.Mode);
            Assert.Equal("本地图片目录", config.ModeText);
            Assert.False(config.IsHardwareExecutionEnabled);
            Assert.Equal(1, config.ImageCount);
            Assert.Equal(12, config.FrameIntervalMilliseconds);
            Assert.Equal(Path.GetFullPath(tempDirectory), config.LocalDirectoryPath);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeDataSourceService_RejectsLocalDirectorySwitch_WhenMachineRunning()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"oceanfresh-local-source-running-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tempDirectory, "frame-001.png"), [1], CancellationToken.None);
            var service = new RuntimeDataSourceService(
                new InMemoryRuntimeDataSourceStore(),
                new FakeRuntimeStateStore(RuntimeMode.Running));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                service.Update(new UpdateRuntimeDataSourceRequest(
                    RuntimeDataSourceMode.LocalImageDirectory,
                    tempDirectory,
                    RuntimeDataSourceConfig.DefaultFrameIntervalMilliseconds)));

            Assert.Equal("请先停止机器后再切换数据源。", exception.Message);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeConfiguredImageSource_LocalDirectory_EmitsOfflineFrames()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"oceanfresh-local-source-frame-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tempDirectory, "frame-001.png"), [9, 8, 7], CancellationToken.None);
            var stateStore = new FakeRuntimeStateStore(RuntimeMode.Running);
            var dataSourceStore = new InMemoryRuntimeDataSourceStore();
            dataSourceStore.Update(new RuntimeDataSourceConfig(
                RuntimeDataSourceMode.LocalImageDirectory,
                tempDirectory,
                1,
                1,
                0,
                false,
                "本地图片目录已选择，共 1 张图片"));
            var source = new RuntimeConfiguredImageSource(dataSourceStore, stateStore);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            InferenceRequest? frame = null;
            await foreach (var item in source.CaptureAsync(timeout.Token).WithCancellation(timeout.Token))
            {
                frame = item;
                break;
            }

            Assert.NotNull(frame);
            Assert.Equal("frame-001.png", frame!.SourceFileName);
            Assert.Equal([9, 8, 7], frame.ImageBytes);
            Assert.Equal(RuntimeDataSourceMode.LocalImageDirectory, frame.DataSourceMode);
            Assert.False(frame.IsHardwareExecutionEnabled);
            Assert.Equal(1, dataSourceStore.Get().CurrentIndex);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeCoordinator_StartMachine_CreatesOfflineDetectionSession_ForLocalDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"oceanfresh-local-source-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tempDirectory, "frame-001.png"), [1], CancellationToken.None);
            var productId = Guid.NewGuid();
            var modelId = Guid.NewGuid();
            var channelId = Guid.NewGuid();
            var stateStore = new FakeRuntimeStateStore();
            var dataSourceStore = new InMemoryRuntimeDataSourceStore();
            var dataSourceService = new RuntimeDataSourceService(dataSourceStore, stateStore);
            dataSourceService.Update(new UpdateRuntimeDataSourceRequest(
                RuntimeDataSourceMode.LocalImageDirectory,
                tempDirectory,
                1));
            var sessionRepository = new FakeDetectionSessionRepository();
            var coordinator = new RuntimeCoordinator(
                stateStore,
                dataSourceStore,
                new FakeChannelConfigRepository([
                    new ChannelConfig(
                        channelId,
                        1,
                        "1号通道",
                        productId,
                        modelId,
                        "yg.pt",
                        DefectHandlingAction.Sink,
                        0.58m,
                        true,
                        null,
                        null,
                        420m,
                        1m,
                        40,
                        25,
                        "[]")
                ]),
                new FakeModelRegistryRepository([
                    new ModelVersion(modelId, Guid.NewGuid(), "YG-MV-004", "yg.pt", "", ModelStatus.Normal, DateTimeOffset.UtcNow)
                ]),
                sessionRepository,
                new FakeHardwareInterlockService());

            await coordinator.StartAsync(CancellationToken.None);
            var session = await sessionRepository.GetActiveAsync(CancellationToken.None);

            Assert.NotNull(session);
            Assert.Equal(RuntimeDataSourceMode.LocalImageDirectory, session!.DataSourceMode);
            Assert.False(session.IsHardwareExecutionEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PlcXrayImageSource_CapturesImageFilesFromConfiguredDirectory()
    {
        var previousDirectory = Environment.GetEnvironmentVariable("OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY");
        var previousProcessExisting = Environment.GetEnvironmentVariable("OCEANFRESH_PLC_XRAY_PROCESS_EXISTING");
        var previousStableMs = Environment.GetEnvironmentVariable("OCEANFRESH_PLC_XRAY_STABLE_MS");
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"oceanfresh-plc-xray-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            Environment.SetEnvironmentVariable("OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY", tempDirectory);
            Environment.SetEnvironmentVariable("OCEANFRESH_PLC_XRAY_PROCESS_EXISTING", "true");
            Environment.SetEnvironmentVariable("OCEANFRESH_PLC_XRAY_STABLE_MS", "1");
            var imagePath = Path.Combine(tempDirectory, "frame-001.png");
            await File.WriteAllBytesAsync(imagePath, [1, 2, 3, 4], CancellationToken.None);

            var source = new PlcXrayImageSource();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            InferenceRequest? frame = null;
            await foreach (var item in source.CaptureAsync(timeout.Token).WithCancellation(timeout.Token))
            {
                frame = item;
                break;
            }

            Assert.NotNull(frame);
            Assert.Equal("frame-001.png", frame!.SourceFileName);
            Assert.Equal([1, 2, 3, 4], frame.ImageBytes);
            Assert.Contains("frame_001", frame.FrameId, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY", previousDirectory);
            Environment.SetEnvironmentVariable("OCEANFRESH_PLC_XRAY_PROCESS_EXISTING", previousProcessExisting);
            Environment.SetEnvironmentVariable("OCEANFRESH_PLC_XRAY_STABLE_MS", previousStableMs);
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StreamInspectionRecordService_AddsRecordToActiveSession()
    {
        var channelId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var sessionRepository = new FakeDetectionSessionRepository();
        var inspectionRepository = new FakeInspectionRecordRepository();
        var runtimeStateStore = new FakeRuntimeStateStore(RuntimeMode.Running);
        var session = await sessionRepository.AddAsync(
            new DetectionSession(
                Guid.NewGuid(),
                "DS-TEST",
                channelId,
                Guid.NewGuid(),
                modelId,
                DateTimeOffset.UtcNow,
                null,
                DetectionSessionStatus.Running),
            CancellationToken.None);
        var service = new StreamInspectionRecordService(sessionRepository, inspectionRepository, runtimeStateStore);

        var record = await service.AddAsync(
            new StreamInspectionRecordRequest(
                channelId,
                modelId,
                @"C:\frames\finalized.png",
                false,
                [new DefectDetection(Guid.NewGuid(), "空壳", 0.95m, 10, 20, 30, 40)],
                [new EjectCommand(Guid.NewGuid(), channelId, "空壳", DefectHandlingAction.Sink, 1, 35, 12000, 50000, DateTimeOffset.UtcNow)]),
            CancellationToken.None);

        Assert.Equal(session.Id, record.DetectionSessionId);
        Assert.True(record.IsRejected);
        Assert.Single(inspectionRepository.Items);
        Assert.Equal(1, runtimeStateStore.GetSnapshot().TotalInspected);
        Assert.Equal(1, runtimeStateStore.GetSnapshot().TotalRejected);
    }

    [Fact]
    public async Task StreamInspectionRecordService_RequiresActiveSession()
    {
        var service = new StreamInspectionRecordService(
            new FakeDetectionSessionRepository(),
            new FakeInspectionRecordRepository(),
            new FakeRuntimeStateStore(RuntimeMode.Running));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddAsync(
                new StreamInspectionRecordRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "frame.png",
                    false,
                    [],
                    []),
                CancellationToken.None));

        Assert.Equal("当前没有运行中的检测任务，请先开始检测。", exception.Message);
    }

    [Fact]
    public async Task ManualReviewService_OnlyReviewsAbnormalSet_AndCalculatesFalsePositiveRate()
    {
        var productId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var normalDetection = new DefectDetection(Guid.NewGuid(), "正常", 0.92m, 10, 10, 30, 30);
        var abnormalDetection = new DefectDetection(Guid.NewGuid(), "碎壳", 0.88m, 80, 20, 40, 40);
        var sessionRepository = new FakeDetectionSessionRepository();
        await sessionRepository.AddAsync(
            new DetectionSession(
                sessionId,
                "DS-REVIEW",
                Guid.NewGuid(),
                productId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                null,
                DetectionSessionStatus.Stopped),
            CancellationToken.None);
        var inspectionRepository = new FakeInspectionRecordRepository();
        await inspectionRepository.AddAsync(
            new InspectionRecord(
                recordId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                sessionId,
                "DS-REVIEW",
                @"C:\frames\001.png",
                true,
                false,
                DateTimeOffset.UtcNow,
                [normalDetection, abnormalDetection],
                []),
            CancellationToken.None);
        var traitRepository = new FakeSeafoodTraitRepository();
        await traitRepository.ReplaceForProductAsync(productId,
        [
            new SeafoodTrait(Guid.NewGuid(), productId, "正常", true, 1, true),
            new SeafoodTrait(Guid.NewGuid(), productId, "碎壳", false, 2, true)
        ], CancellationToken.None);
        var reviewRepository = new FakeManualReviewRepository();
        var service = new ManualReviewService(
            inspectionRepository,
            sessionRepository,
            traitRepository,
            reviewRepository,
            new FakeManualReviewPreviewGenerator());

        var beforeReview = await service.GetSessionReviewAsync(sessionId, CancellationToken.None);
        var afterReview = await service.UpsertReviewAsync(
            sessionId,
            new UpsertManualReviewRequest(recordId, abnormalDetection.Id, "正常", "tester", "误剔样本"),
            CancellationToken.None);

        Assert.Single(beforeReview.Candidates);
        Assert.Equal(abnormalDetection.Id, beforeReview.Candidates[0].DetectionId);
        Assert.Equal(1, afterReview.Summary.CandidateCount);
        Assert.Equal(1, afterReview.Summary.ReviewedCount);
        Assert.Equal(1, afterReview.Summary.FalsePositiveCount);
        Assert.Equal(100m, afterReview.Summary.FalsePositiveRate);
        Assert.Contains("严格召回率暂不计算", afterReview.Summary.RecallStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteModelAsync_DeletesModel_WhenNoChannelUsesIt()
    {
        var modelId = Guid.NewGuid();
        var modelRepository = new FakeModelRegistryRepository([
            new ModelVersion(
                modelId,
                Guid.NewGuid(),
                "HG-MV-099",
                "hg.pt",
                "可删模型",
                ModelStatus.Normal,
                DateTimeOffset.UtcNow)
        ]);
        var service = new ModelManagementService(
            modelRepository,
            new FakeModelValidator(),
            new FakeChannelConfigRepository([]));

        await service.DeleteAsync(modelId, CancellationToken.None);

        Assert.Empty(modelRepository.Items);
    }

    [Fact]
    public async Task DeleteModelAsync_ThrowsReadableMessage_WhenModelIsStillBoundToChannel()
    {
        var modelId = Guid.NewGuid();
        var modelRepository = new FakeModelRegistryRepository([
            new ModelVersion(
                modelId,
                Guid.NewGuid(),
                "HG-MV-100",
                "hg.pt",
                "绑定模型",
                ModelStatus.Normal,
                DateTimeOffset.UtcNow)
        ]);
        var channelRepository = new FakeChannelConfigRepository([
            new ChannelConfig(
                Guid.NewGuid(),
                2,
                "2号通道",
                Guid.NewGuid(),
                modelId,
                "hg.onnx",
                DefectHandlingAction.Sink,
                0.58m,
                true,
                null,
                null,
                420m,
                1m,
                40,
                25,
                """[{"nozzleNumber":1,"startX":0,"endX":383}]""")
        ]);
        var service = new ModelManagementService(
            modelRepository,
            new FakeModelValidator(),
            channelRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(modelId, CancellationToken.None));

        Assert.Equal("当前模型仍绑定到 2号通道，请先解除通道绑定后再删除。", exception.Message);
        Assert.Single(modelRepository.Items);
    }

    [Fact]
    public async Task ManualInferenceService_UsesActiveChannelModelAndBuildsEjectCommands()
    {
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9010", "油蛤", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
        ]);
        var traitRepository = new FakeSeafoodTraitRepository();
        await traitRepository.ReplaceForProductAsync(productId,
        [
            new SeafoodTrait(Guid.NewGuid(), productId, "正常", true, 1, true),
            new SeafoodTrait(Guid.NewGuid(), productId, "泥包", false, 2, true)
        ], CancellationToken.None);

        var modelRepository = new FakeModelRegistryRepository([
            new ModelVersion(
                modelId,
                Guid.NewGuid(),
                "YG-MV-001",
                @"E:\Models\YG\real-yg.pt",
                "油蛤模型",
                ModelStatus.Normal,
                DateTimeOffset.UtcNow)
        ]);
        var channelRepository = new FakeChannelConfigRepository([
            new ChannelConfig(
                channelId,
                1,
                "1号通道",
                productId,
                modelId,
                @"E:\Models\YG\real-yg.pt",
                DefectHandlingAction.Sink,
                0.58m,
                true,
                null,
                null,
                420m,
                1m,
                40,
                25,
                """[{"nozzleNumber":1,"startX":0,"endX":767},{"nozzleNumber":2,"startX":768,"endX":1536}]""")
        ]);
        var inferenceEngine = new FakeInferenceEngine(new InferenceResult(
            "manual-1",
            false,
            [
                new DefectDetection(Guid.NewGuid(), "正常", 0.91m, 100, 120, 60, 60),
                new DefectDetection(Guid.NewGuid(), "泥包", 0.73m, 900, 160, 70, 70)
            ],
            DateTimeOffset.UtcNow));
        var inspectionRepository = new FakeInspectionRecordRepository();
        var sessionRepository = new FakeDetectionSessionRepository();
        var service = new ManualInferenceService(
            channelRepository,
            productRepository,
            traitRepository,
            modelRepository,
            inferenceEngine,
            new FakeLocator(),
            inspectionRepository,
            sessionRepository);

        var result = await service.RunAsync("test.png", [1, 2, 3], CancellationToken.None);

        Assert.Equal("1号通道", result.ChannelName);
        Assert.Equal("油蛤", result.ProductName);
        Assert.Equal("YG-MV-001", result.ModelVersion);
        Assert.Equal(@"E:\Models\YG\real-yg.pt", result.ModelPath);
        Assert.Single(result.EjectCommands);
        Assert.Equal("泥包", result.EjectCommands[0].DefectLabel);
        Assert.Equal(@"E:\Models\YG\real-yg.pt", inferenceEngine.LastModelPath);
        Assert.Equal("""{"0":"正常","1":"碎壳"}""", inferenceEngine.LastLabelMapJson);
        Assert.Single(inspectionRepository.Items);
        Assert.Null(inspectionRepository.Items[0].DetectionSessionId);
    }

    private sealed class FakeSeafoodProductRepository(IReadOnlyList<SeafoodProduct> seedItems) : ISeafoodProductRepository
    {
        public List<SeafoodProduct> Items { get; } = seedItems.ToList();

        public Task<IReadOnlyList<SeafoodProduct>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeafoodProduct>>(Items.OrderBy(x => x.Name).ToList());

        public Task<SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<SeafoodProduct?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<SeafoodProduct> UpsertAsync(SeafoodProduct product, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == product.Id);
            if (existing >= 0)
            {
                Items[existing] = product;
            }
            else
            {
                Items.Add(product);
            }

            return Task.FromResult(product);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSeafoodTraitRepository : ISeafoodTraitRepository
    {
        private readonly Dictionary<Guid, List<SeafoodTrait>> _traitsByProduct = [];

        public Task<IReadOnlyList<SeafoodTrait>> GetByProductAsync(Guid productId, CancellationToken cancellationToken)
        {
            _traitsByProduct.TryGetValue(productId, out var traits);
            return Task.FromResult<IReadOnlyList<SeafoodTrait>>(traits?.ToList() ?? []);
        }

        public Task ReplaceForProductAsync(Guid productId, IReadOnlyList<SeafoodTrait> traits, CancellationToken cancellationToken)
        {
            _traitsByProduct[productId] = traits.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChannelConfigRepository(IReadOnlyList<ChannelConfig> seedItems) : IChannelConfigRepository
    {
        public List<ChannelConfig> Items { get; } = seedItems.ToList();

        public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChannelConfig>>(Items.OrderBy(x => x.ChannelNo).ToList());

        public Task<ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ChannelConfig?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<ChannelConfig> UpsertAsync(ChannelConfig channelConfig, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == channelConfig.Id);
            if (existing >= 0)
            {
                Items[existing] = channelConfig;
            }
            else
            {
                Items.Add(channelConfig);
            }

            return Task.FromResult(channelConfig);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModelRegistryRepository(IReadOnlyList<ModelVersion> seedItems) : IModelRegistryRepository
    {
        public List<ModelVersion> Items { get; } = seedItems.ToList();

        public Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelVersion>>(Items.Where(x => x.SeafoodCategoryId == categoryId).ToList());

        public Task<ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ModelVersion?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelVersion>>(Items.ToList());

        public Task<ModelVersion> UpsertAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == modelVersion.Id);
            if (existing >= 0)
            {
                Items[existing] = modelVersion;
            }
            else
            {
                Items.Add(modelVersion);
            }

            return Task.FromResult(modelVersion);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModelValidator : IModelValidator
    {
        public Task<ModelValidationResult> ValidateAsync(ModelVersion modelVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelValidationResult(true, ["ok"]));
    }

    private static ChannelConfig CreateChannelConfig(Guid productId) =>
        new(
            Guid.NewGuid(),
            1,
            "1号通道",
            productId,
            null,
            "model.pt",
            DefectHandlingAction.Sink,
            0.58m,
            true,
            null,
            null,
            420m,
            1m,
            40,
            25,
            """[{"nozzleNumber":1,"startX":0,"endX":383},{"nozzleNumber":2,"startX":384,"endX":767},{"nozzleNumber":3,"startX":768,"endX":1151},{"nozzleNumber":4,"startX":1152,"endX":1536}]""");

    private sealed class FakeProductPredictConfigStore : IProductPredictConfigStore
    {
        public Task<string> GetDefaultTemplateContentAsync(CancellationToken cancellationToken) =>
            Task.FromResult("""{"conf":0.25,"iou":0.7}""");

        public Task<string?> SaveManagedConfigAsync(string productCode, string? requestedPredictConfigPath, string? requestedPredictConfigJson, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(requestedPredictConfigPath ?? $@"product-predict-configs\{productCode}-predict.json");
    }

    private sealed class FakeInferenceEngine(InferenceResult result) : IInferenceEngine
    {
        public string? LastModelPath { get; private set; }
        public string? LastLabelMapJson { get; private set; }

        public Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            var channelRequest = Assert.IsType<ChannelInferenceRequest>(request);
            LastModelPath = channelRequest.ModelPath;
            LastLabelMapJson = channelRequest.ModelLabelMapJson;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeLocator : ILocator
    {
        public EjectCommand BuildCommand(ChannelConfig channelConfig, DefectDetection detection, DefectHandlingAction action, DateTimeOffset createdAt) =>
            new(
                Guid.NewGuid(),
                channelConfig.Id,
                detection.Label,
                action,
                2,
                180,
                32000,
                50000,
                createdAt);
    }

    private sealed class FakeInspectionRecordRepository : IInspectionRecordRepository
    {
        public List<InspectionRecord> Items { get; } = [];

        public Task AddAsync(InspectionRecord inspectionRecord, CancellationToken cancellationToken)
        {
            Items.Add(inspectionRecord);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InspectionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InspectionRecord>>(Items.TakeLast(take).ToList());

        public Task<IReadOnlyList<InspectionRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InspectionRecord>>(Items.Where(x => x.DetectionSessionId == sessionId).ToList());

        public Task<IReadOnlyList<InspectionRecord>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InspectionRecord>>(Items.Where(x => x.CapturedAt >= since).ToList());
    }

    private sealed class FakeManualReviewRepository : IManualReviewRepository
    {
        public List<ManualReviewRecord> Items { get; } = [];

        public Task<IReadOnlyList<ManualReviewRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManualReviewRecord>>(Items.Where(x => x.DetectionSessionId == sessionId).ToList());

        public Task<ManualReviewRecord> UpsertAsync(ManualReviewRecord reviewRecord, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.DetectionId == reviewRecord.DetectionId);
            if (existing >= 0)
            {
                Items[existing] = reviewRecord;
            }
            else
            {
                Items.Add(reviewRecord);
            }

            return Task.FromResult(reviewRecord);
        }
    }

    private sealed class FakeManualReviewPreviewGenerator : IManualReviewPreviewGenerator
    {
        public ManualReviewPreview BuildPreviewImage(InspectionRecord record, DefectDetection detection) =>
            new(record.ImagePath, true, "ok");
    }

    private sealed class FakeRuntimeStateStore : IRuntimeStateStore
    {
        private RuntimeSnapshot _snapshot;

        public FakeRuntimeStateStore(RuntimeMode mode = RuntimeMode.Stopped)
        {
            _snapshot = new RuntimeSnapshot(
                mode,
                mode == RuntimeMode.Running ? DeviceState.Running : DeviceState.Idle,
                "未启用通道",
                "未选择模型",
                "TEST",
                0,
                0,
                100m,
                DateTimeOffset.UtcNow,
                [],
                [],
                mode == RuntimeMode.Running ? DateTimeOffset.UtcNow : null);
        }

        public RuntimeSnapshot GetSnapshot() => _snapshot;

        public void Update(RuntimeSnapshot snapshot) => _snapshot = snapshot;
    }

    private sealed class FakeDeviceHealthProvider : IDeviceHealthProvider
    {
        public Task<IReadOnlyList<DeviceStatus>> GetStatusesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeviceStatus>>(
            [
                new DeviceStatus("传送带", DeviceState.Running, "运行中", DateTimeOffset.UtcNow),
                new DeviceStatus("X 光探测器", DeviceState.Running, "同步稳定", DateTimeOffset.UtcNow)
            ]);
    }

    private sealed class FakeHardwareInterlockService(bool canRun = true) : IHardwareInterlockService
    {
        public Task<HardwareInterlockResult> EvaluateAsync(CancellationToken cancellationToken)
        {
            var alarm = new AlarmEvent(
                Guid.NewGuid(),
                AlarmSeverity.Critical,
                "hardware",
                "XRAY_SOURCE_FAULTED",
                "X 光光源: 故障",
                DateTimeOffset.UtcNow,
                false);

            return Task.FromResult(new HardwareInterlockResult(
                canRun,
                canRun ? RuntimeMode.Running : RuntimeMode.Faulted,
                canRun ? DeviceState.Running : DeviceState.Faulted,
                [
                    new DeviceStatus("XS-001", canRun ? DeviceState.Running : DeviceState.Faulted, canRun ? "X 光光源: 运行中" : "X 光光源: 故障", DateTimeOffset.UtcNow)
                ],
                canRun ? [] : [alarm],
                canRun ? "关键硬件联锁正常，允许启动。" : "存在 1 条关键硬件联锁告警，系统禁止运行。"));
        }
    }

    private sealed class FakeDetectionSessionRepository : IDetectionSessionRepository
    {
        public List<DetectionSession> Items { get; } = [];

        public Task<DetectionSession> AddAsync(DetectionSession session, CancellationToken cancellationToken)
        {
            Items.Add(session);
            return Task.FromResult(session);
        }

        public Task<DetectionSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<DetectionSession?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<DetectionSession?> GetActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DetectionSession?>(Items.LastOrDefault(x => x.Status == DetectionSessionStatus.Running));

        public Task<DetectionSession?> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DetectionSession?>(Items.OrderByDescending(x => x.StartedAt).FirstOrDefault());

        public Task<IReadOnlyList<DetectionSession>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DetectionSession>>(Items.Where(x => x.StartedAt >= since).ToList());

        public Task<DetectionSession> UpdateAsync(DetectionSession session, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == session.Id);
            if (existing >= 0)
            {
                Items[existing] = session;
            }

            return Task.FromResult(session);
        }
    }
}
