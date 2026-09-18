# Ocean Fresh Sorting System

> 首次运行请先阅读：[运行说明](docs/运行说明.md)。

## Quick Start

首次使用，在仓库根目录双击：

```text
setup-environment.cmd
```

环境准备成功后，以后直接双击：

```text
start-oceanfresh.cmd
```

主程序会自动启动 LocalApi、YOLO 服务和 SQLite 数据库，不需要分别启动三个终端。详细环境要求、首次配置和故障排查请阅读上面的运行说明。

Windows industrial HMI and local backend for X-ray seafood inspection, YOLO inference, production statistics, manual review, device monitoring, and ejector-control integration.

## System Goal

The system is designed for an industrial PC deployed beside a seafood sorting line. Its current goal is to provide a deliverable Windows HMI that can:

- manage seafood products, product traits, model label mappings, YOLO models, and channel bindings;
- start and stop the machine from a single production home page;
- read X-ray images from either a production X-ray camera source or a local image directory data source;
- call the YOLO FastAPI service through the current enabled channel and model;
- record inspection results, detection sessions, normal/abnormal counts, yield rate, and defect distribution;
- disable real ejector output for offline/local-directory inspection tasks;
- support manual review of abnormal detections for model evaluation;
- expose hardware/device status, critical alarms, and software maintenance information for later production deployment.

The current delivery frontend is the WPF HMI in `src/OceanFresh.SortingSystem.HMI`. The Electron/Vue prototype is retained in the repository, but frontend replacement work is paused for the current delivery.

## Solution Layout

- `src/OceanFresh.SortingSystem.Domain`
  Core entities, enums, runtime data-source models, detection sessions, manual review records, device states, alarms, and software-version records.
- `src/OceanFresh.SortingSystem.Application`
  Application services, DTOs, product/model/channel rules, runtime orchestration, statistics aggregation, manual review workflow, and validation logic.
- `src/OceanFresh.SortingSystem.Infrastructure`
  Local persistence and infrastructure adapters used by the current desktop/backend runtime.
- `src/OceanFresh.SortingSystem.LocalApi`
  Local ASP.NET Core API for HMI pages, product/model/channel management, runtime control, statistics, devices, alarms, software maintenance, and YOLO integration.
- `src/OceanFresh.SortingSystem.Runtime`
  Runtime worker abstractions for image source, inference, locating, inspection recording, and ejector control.
- `src/OceanFresh.SortingSystem.DeviceHost`
  Device-facing worker host for hardware status and ejector-control integration simulation/adaptation.
- `src/OceanFresh.SortingSystem.HMI`
  Current WPF desktop HMI for operators and administrators.
- `src/OceanFresh.SortingSystem.ElectronHmi`
  Electron + Vue 3 prototype retained for reference. It is not the current delivery frontend.
- `predict/youge`
  Managed YOLO predict assets and service runtime files used for local integration.
- `tests/OceanFresh.SortingSystem.Tests`
  Unit tests for application rules, runtime workflows, statistics, HMI view models, and integration boundaries.
- `docs`
  Functional specification, technical plan, diagrams, API/interface extraction, and delivery notes.

## Current Functional Scope

### HMI Pages

- Home page: production resident page for X-ray image display, recognition result, classification statistics, telemetry, data-source configuration, and machine start/stop.
- User center: current user, role permission scope, login time, and administrator password change.
- Data center: product-based historical statistics, detection-task records, yield trend, defect distribution, and manual review entry.
- Settings: product, model, channel, hardware device, ejector-control, safety alarm, and software-maintenance settings.

### Runtime Data Sources

- `XrayCamera`: default production mode. It represents the real X-ray detector/camera acquisition chain and allows hardware ejector execution.
- `LocalImageDirectory`: offline inspection mode. It reads images from a selected directory at a configured frame interval, creates detection sessions and inspection records, but does not generate or execute real ejector commands.

### Detection and Review

- Products define business traits and model-label mappings through a required label file such as `classes.txt`.
- Models are bound to seafood products and identified by generated version numbers.
- Channels bind product, model, confidence threshold, and runtime enablement.
- Detection sessions group each production or offline inspection run.
- Inspection records store per-image detections and optional eject commands.
- Manual review focuses on abnormal detections only, with three outcomes: confirmed abnormal, false positive, and relabeled abnormal.

### Hardware and Safety

The current hardware-facing design covers:

- X-ray source;
- X-ray detector;
- conveyor;
- ejector device;
- controller/PLC.

Critical hardware faults should raise red safety alarms and block normal running. Actual vendor SDK/PLC protocol adapters are expected to be implemented behind the existing device/runtime abstractions.

## Local API Overview

Default LocalApi address:

```text
http://127.0.0.1:5188/
```

Important API groups:

- `/api/auth/*`: operator login, administrator login, administrator password update.
- `/api/products`: seafood product, trait, label file, label mapping, and product-level predict config.
- `/api/models`: YOLO model import, editing, deletion, and product binding.
- `/api/channels`: channel configuration and current enabled channel.
- `/api/runtime/*`: machine start/stop, runtime dashboard, data-source configuration, manual/internal inference, stream records.
- `/api/statistics/*`: product statistics, detection-task records, yield trend, defect distribution, and abnormal-object manual review.
- `/api/devices`: hardware device list, editing, and self-check.
- `/api/alarms`: active alarms and acknowledgement.
- `/api/system/*`: software version and update-package checks.

## Prerequisites

- `.NET SDK 8.0`.
- `Microsoft.WindowsDesktop.App` for the WPF HMI project.
- Python/Conda environment for the YOLO FastAPI service when using the current Python inference path.
- Real deployment requires replacing simulation/file-directory adapters with vendor SDK, PLC protocol, and hardware controller adapters.

## Build and Test

```powershell
dotnet build OceanFresh.SortingSystem.sln --no-restore
dotnet test tests\OceanFresh.SortingSystem.Tests\OceanFresh.SortingSystem.Tests.csproj --no-build
```

If the WPF HMI is running, the build may fail because the executable is locked. Close the HMI before rebuilding.

## Current Startup Notes

The current delivery UI is the WPF HMI. Run `setup-environment.cmd` once, then use `start-oceanfresh.cmd` for normal startup. The HMI automatically starts LocalApi and the YOLO service.

The Electron/Vue one-click startup script and desktop shortcut may still exist for prototype preview, but they are not the active delivery path for the current specification.

## Documentation

- `docs/功能规格说明书.md`: current visible product functionality and acceptance scope.
- `docs/front-backend-ui-function-xray-interface-extract.md`: frontend pages, API groups, X-ray image-source interfaces, and detection links.
- `docs/ocean-fresh-sorting-system-technical-plan.md`: product/technical architecture, runtime data sources, YOLO service, hardware integration, and delivery roadmap.
