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

探测器 Demo 没有正式头文件或导入库。当前已通过静态分析恢复主要回调结构体尺寸和字段偏移，并通过 MSVC x64 离线编译检查；但尚未用现场正式版插件和真实探测器验证，因此当前版本仍不直接调用 `TDI_registerCallback`。错误的 ABI 或版本组合可能导致进程崩溃或图像数据损坏。

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

Ocean Fresh 停止依赖 Techik 主程序，使用其 `modbus_x64.dll` 直接访问 Modbus RTU 剔除控制器。

```powershell
$env:OCEANFRESH_HARDWARE_ADAPTER = "techik-direct"
$env:OCEANFRESH_TECHIK_ROOT = "C:\Techik"
$env:OCEANFRESH_TECHIK_MODBUS_PORT = "COM3"
$env:OCEANFRESH_TECHIK_MODBUS_BAUD = "38400"
$env:OCEANFRESH_TECHIK_MODBUS_PARITY = "N"
$env:OCEANFRESH_TECHIK_MODBUS_DATA_BITS = "8"
$env:OCEANFRESH_TECHIK_MODBUS_STOP_BITS = "2"
$env:OCEANFRESH_TECHIK_MODBUS_SLAVE_ID = "<现场确认的正整数站号>"
```

Direct 模式默认仍然锁死物理输出。只有在现场确认 Modbus 站号、串口参数，以及 `REJECT_BULK_AIR.unit_N_port` 确实可直接作为 libmodbus coil 地址后，才能同时设置：

```powershell
$env:OCEANFRESH_TECHIK_ENABLE_OUTPUT = "true"
$env:OCEANFRESH_TECHIK_DIRECT_PROTOCOL_CONFIRMED = "true"
$env:OCEANFRESH_TECHIK_DIRECT_PORT_MAP_CONFIRMED = "true"
```

禁止在没有断料、安全隔离和现场人员确认的情况下设置这两个开关。

## 支持的覆盖配置

| 环境变量 | 含义 | 默认值 |
| --- | --- | --- |
| `OCEANFRESH_HARDWARE_ADAPTER` | `techik-bridge`、`techik-direct` 或未启用 | 未启用 |
| `OCEANFRESH_TECHIK_ROOT` | Techik 完整运行目录 | `C:\Techik` |
| `OCEANFRESH_TECHIK_CAPTURE_DIRECTORY` | 覆盖 Techik 图像目录 | 从 `misc_config.ini` 读取 |
| `OCEANFRESH_TECHIK_MODBUS_DLL` | 覆盖 `modbus_x64.dll` 路径 | Techik 根目录 |
| `OCEANFRESH_TECHIK_MODBUS_PORT` | I/O 串口 | 从活动配置读取，缺省 `COM3` |
| `OCEANFRESH_TECHIK_MODBUS_BAUD` | 波特率 | `38400` |
| `OCEANFRESH_TECHIK_MODBUS_PARITY` | 校验位 | `N` |
| `OCEANFRESH_TECHIK_MODBUS_DATA_BITS` | 数据位 | `8` |
| `OCEANFRESH_TECHIK_MODBUS_STOP_BITS` | 停止位 | `2` |
| `OCEANFRESH_TECHIK_MODBUS_SLAVE_ID` | Modbus 从站号 | `0`（未确认，不允许输出） |
| `OCEANFRESH_TECHIK_ENABLE_OUTPUT` | 允许物理剔除输出 | `false` |
| `OCEANFRESH_TECHIK_DIRECT_PROTOCOL_CONFIRMED` | 确认 COM3 设备确实使用所配置的 Modbus RTU 协议 | `false` |
| `OCEANFRESH_TECHIK_DIRECT_PORT_MAP_CONFIRMED` | 确认 Techik 端口号就是 coil 地址 | `false` |

## 仍需现场验证

1. 探测器具体型号，以及 `st_dt_info`、`st_dt_data_frame`、回调函数的正式 C/C++ 定义；
2. Techik 全量原始图像是否可以持续落盘，目录层级与文件格式；
3. I/O 模块真实 COM 口、Modbus 从站号和 `38400-8N2` 参数；
4. `unit_0_port=1000` 到 `unit_71_port=1071` 是直接 coil 地址还是 Techik 内部逻辑端口；
5. 喷嘴方向、编号和图像横坐标映射；
6. X 光源 type 6 的串口帧协议；
7. 传送带 type 4 的启停、速度反馈和编码器协议；
8. 探测器到剔除位置的真实机械距离与时延标定。

在以上项目验证前，系统会把未确认的关键硬件标记为故障并阻止生产运行；剔除下发异常也会转为关键告警和故障停机状态。
