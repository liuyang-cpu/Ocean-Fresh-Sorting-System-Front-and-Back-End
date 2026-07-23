# Techik 硬件兼容接入说明

## 已确认的现场软件证据

从用户带回的 Techik 运行目录与 Demo 中静态确认：

- 主程序为 64 位 `Techik.exe`，文件版本 `2.4.9.27`；
- 探测器插件为 `plugin/tk_driver_dt.dll`；
- I/O、剔除、X 光插件分别为 `plugin/tk_driver_io.dll`、`plugin/tk_driver_rejector.dll`、`plugin/tk_driver_xray.dll`；
- `modbus_x64.dll` 导出标准 libmodbus RTU/TCP API；
- 探测器 Demo 导出 `TDI_Init`、`TDI_Start`、`TDI_registerCallback`、`TDI_GrabEnable` 等接口；
- 当前活动配置为 `cfg/prod_default/sys_config.ini`；
- 当前样例配置包含 1 个 1536 像素线阵探测器、X 光源 COM5、I/O COM3、传送带 COM1，以及 72 路气吹输出；
- 历史图像配置为 `D:/History/img`，原配置只保存 NG 图像；
- 独立 I/O Demo 目录名表明串口参数可能为 `38400-8N2`。

`tk_txr_system.pdb` 和 `TxrUi.pdb` 保留了完整 C++ 类型信息。当前已经恢复探测器、X 光源、IO 和传送带包装类的方法签名及结构体字段。探测器 demo 的 2023 ABI 已封装为 64 位 `TechikDetectorBridge.exe`；离线检查确认必需导出全部存在，但仍需在现场验证 demo DLL 与真实 type 0 探测器兼容。

## 两种工作模式

### Bridge 模式

原 Techik 软件继续托管探测器、光源、传送带和剔除设备；Ocean Fresh 从 Techik 历史图像目录读取图片。

```powershell
$env:OCEANFRESH_HARDWARE_ADAPTER = "techik-bridge"
$env:OCEANFRESH_TECHIK_ROOT = "C:\Techik"
```

启动时会读取 `cfg/config.ini` 指向的活动配置，并在没有显式设置 `OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY` 时自动采用 Techik 的历史图像目录。

限制：带回配置中的 Techik 只保存 NG 图像，所以 Bridge 模式目前适合验证图像链路，不能代替完整的实时全量采集。Bridge 模式禁止 Ocean Fresh 下发剔除命令，避免两个程序同时控制同一设备。

### Direct 模式

Ocean Fresh 可直接启动原生桥接，不运行 `Techik.exe`。桥接完成以下工作：

- 通过 demo `tk_driver_dt.dll` 初始化 type 0 探测器，接收 16 位原始回调并聚合为 1536×300 图像；
- 通过 Windows 命名管道将聚合后的 16 位帧直接传给 .NET 有界内存队列，不在生产链路中落盘；
- 内存队列只保留最新帧，推理短时落后时丢弃最旧帧，避免阻塞厂商回调和无限积压；
- 加载原 Techik 的 `tk_driver_xray.dll`、`tk_driver_io.dll`、`tk_driver_motion.dll`；
- 使用原插件内部协议连接 X 光源 COM5、I/O COM3/COM1 和传送带 COM1；
- 从 `sys_config.ini` 构造原插件所需设备参数，从当前产品配置读取生产设定值；
- 通过独立进程隔离厂商 C++ ABI，插件异常不会直接破坏 .NET 进程。

```powershell
$env:OCEANFRESH_HARDWARE_ADAPTER = "techik-direct"
$env:OCEANFRESH_TECHIK_ENABLE_DETECTOR = "true"
$env:OCEANFRESH_TECHIK_ENABLE_PERIPHERALS = "true"
$env:OCEANFRESH_TECHIK_DETECTOR_BRIDGE = "C:\OceanFresh\HardwareBridge\TechikDetectorBridge.exe"
$env:OCEANFRESH_TECHIK_DETECTOR_SDK_ROOT = "C:\OceanFresh\DetectorSdk"
$env:OCEANFRESH_TECHIK_ROOT = "C:\OceanFresh\TechikRuntime"
```

带回配置当前读到：

- X 光范围 30–60 kV、500–8000 μA，当前产品设定 50 kV、5333 μA；
- 传送带范围 5–120，当前产品 008 的最近备份设定为速度 90、方向 true；
- 剔除端口 1000–1071、共 72 路。

这些数值是从带回文件恢复的，不等于已在真实机器验证。Direct 模式默认仍锁死所有物理动作。首次现场只能先验证插件加载、设备连接和状态读取；完成断料、安全隔离、急停测试以及喷嘴映射核对后，才可设置：

```powershell
$env:OCEANFRESH_TECHIK_ENABLE_OUTPUT = "true"
```

光源、传送带和剔除输出都受该总开关保护。也可以用以下变量临时覆盖产品设定：

```powershell
$env:OCEANFRESH_TECHIK_XRAY_KV = "50"
$env:OCEANFRESH_TECHIK_XRAY_UA = "5333"
$env:OCEANFRESH_TECHIK_CONVEYOR_SPEED = "90"
$env:OCEANFRESH_TECHIK_CONVEYOR_DIRECTION = "true"
```

桥接只接受本次带回的固定插件版本，启动前会核对三个插件的 SHA-256。插件版本不一致时会拒绝以已恢复的固定 ABI 加载，避免调用错误虚函数地址。

## 支持的覆盖配置

| 环境变量 | 含义 | 默认值 |
| --- | --- | --- |
| `OCEANFRESH_HARDWARE_ADAPTER` | `techik-bridge`、`techik-direct` 或未启用 | 未启用 |
| `OCEANFRESH_TECHIK_ROOT` | Techik 完整运行目录 | `C:\Techik` |
| `OCEANFRESH_TECHIK_CAPTURE_DIRECTORY` | 覆盖 Techik 图像目录 | 从 `misc_config.ini` 读取 |
| `OCEANFRESH_TECHIK_ENABLE_DETECTOR` | 启动直接探测器采集 | `false` |
| `OCEANFRESH_TECHIK_ENABLE_PERIPHERALS` | 加载原 X 光、IO、传送带插件并连接设备 | `false` |
| `OCEANFRESH_TECHIK_DETECTOR_BRIDGE` | 原生 64 位桥接程序 | 安装目录 `HardwareBridge` |
| `OCEANFRESH_TECHIK_DETECTOR_SDK_ROOT` | demo 探测器 SDK 目录 | Techik 根目录下 `detector-sdk` |
| `OCEANFRESH_TECHIK_FRAME_DIRECTORY` | 旧版文件传输兼容目录；实时管道不可用时保留诊断能力 | `%LOCALAPPDATA%\OceanFreshSortingSystem\techik-frames` |
| `OCEANFRESH_TECHIK_FRAME_QUEUE_CAPACITY` | 原生桥接和应用层各自允许等待处理的完整帧数（满时丢弃最旧帧） | `16`（允许 4–128） |
| `OCEANFRESH_TECHIK_ENABLE_OUTPUT` | 允许光源、传送带和物理剔除动作 | `false` |
| `OCEANFRESH_TECHIK_XRAY_KV` | 覆盖产品 X 光电压 | 从产品配置读取 |
| `OCEANFRESH_TECHIK_XRAY_UA` | 覆盖产品 X 光电流 | 从产品配置读取 |
| `OCEANFRESH_TECHIK_CONVEYOR_SPEED` | 覆盖产品传送带速度 | 从产品配置读取 |
| `OCEANFRESH_TECHIK_CONVEYOR_DIRECTION` | 覆盖产品传送带方向 | 从产品配置读取 |

## 仍需现场验证

1. type 0 探测器的具体品牌/型号，以及 2023 demo DLL 与现场硬件的实际兼容性；
2. 直接回调能否持续获得 1536×50 子帧，以及扫描方向和灰度是否正确；
3. 原插件是否能在现场成功连接 X 光源、两个 IO 对象和传送带；
4. 恢复的 50 kV、5333 μA、速度 90、方向 true 是否就是该机器当前正确生产参数；
5. `unit_0_port=1000` 到 `unit_71_port=1071` 经原 IO 插件调用后是否对应正确物理喷嘴；
6. 喷嘴顺序、方向、保持时间和图像横坐标映射；
7. 探测器到剔除位置的真实机械距离、传送带速度单位与时延标定；
8. 急停、门禁、X 光状态与故障反馈是否满足现场安全联锁。

在以上项目验证前，部署包中的 `OCEANFRESH_TECHIK_ENABLE_OUTPUT=false` 必须保持不变。此状态允许检查文件、插件和设备连接，但会阻止开 X 光、启动传送带和剔除动作。
