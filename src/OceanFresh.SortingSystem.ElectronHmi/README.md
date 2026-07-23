# OceanFresh Electron HMI

这是替换 WPF HMI 的新前端壳，使用 `Electron + Vue 3 + TypeScript + Vite`。

第一版迁移策略：

- 继续复用现有 `.NET LocalApi`、数据库、运行协调、YOLO 服务和硬件服务。
- Electron 只负责桌面窗口、文件选择、本地图片读取和 UI 展示。
- Vue 负责首页监控界面、机器启停、单张检测和离线批量检测。

## 开发运行

一键启动方式：

```powershell
..\..\start-electron-hmi.cmd
```

或者在仓库根目录双击：

```text
start-electron-hmi.cmd
```

手动启动方式：

先启动本地 API：

```powershell
dotnet run --project ..\OceanFresh.SortingSystem.LocalApi\OceanFresh.SortingSystem.LocalApi.csproj
```

再启动 Electron HMI：

```powershell
npm install
npm run dev
```

如需修改 LocalApi 地址：

```powershell
$env:OCEANFRESH_LOCAL_API_URL="http://127.0.0.1:5188"
npm run dev
```

## 架构说明

```text
Electron + Vue HMI
        ↓ IPC
Electron Main Process
        ↓ HTTP / File System
.NET LocalApi / 本地图片
        ↓
Runtime / DeviceHost / YOLO FastAPI / SQLite
```

这样做可以避免浏览器跨域问题，也避免把 PLC、X 光探测器、剔除设备等硬件通信写进前端页面。
