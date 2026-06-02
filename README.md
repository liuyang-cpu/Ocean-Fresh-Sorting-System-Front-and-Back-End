# Ocean Fresh Sorting System

Windows industrial sorting system scaffold for X-ray based seafood quality inspection.

## Solution Layout

- `src/OceanFresh.SortingSystem.Domain`
  Core entities, enums, and device/runtime contracts.
- `src/OceanFresh.SortingSystem.Application`
  Application services, DTOs, and orchestration logic.
- `src/OceanFresh.SortingSystem.Infrastructure`
  In-memory repositories and bootstrap data for V1 development.
- `src/OceanFresh.SortingSystem.LocalApi`
  Local ASP.NET Core API for product, model, runtime, and record management.
- `src/OceanFresh.SortingSystem.Runtime`
  Runtime worker that simulates the real-time X-ray -> inference -> eject flow.
- `src/OceanFresh.SortingSystem.DeviceHost`
  Device worker for hardware health and ejector control simulation.
- `src/OceanFresh.SortingSystem.HMI`
  WPF desktop HMI shell for operators and administrators.
- `tests/OceanFresh.SortingSystem.Tests`
  Unit tests for core application rules.

## Current Scope

This implementation focuses on a clean V1 architecture skeleton:

- seafood category / recipe / model version management
- multi-version model activation and rollback
- SQLite-backed local persistence with seeded seafood / recipe / model data
- runtime control and monitoring contracts
- simulated real-time pipeline and device host workers
- local API endpoints for the main management workflows
- WPF HMI shell with pages aligned to the industrial UI design

## Prerequisites

- `.NET SDK 8.0` is required to build and run the solution.
- `Microsoft.WindowsDesktop.App` is required for the HMI project.
- For real deployment, replace simulation services with vendor SDK adapters and ONNX Runtime CUDA integration.

## Next Steps

1. Install `.NET SDK 8.0`.
2. Restore and build the solution.
3. Replace in-memory repositories with SQLite-backed implementations.
4. Replace simulated runtime/device adapters with actual X-ray, encoder, IO, and inference components.
