# Techik 探测器 demo ABI 静态恢复记录

分析对象：

- `F:\tk_driver_dt_demo 23-6-19\tk_driver_dt_demo.exe`
- `F:\tk_driver_dt_demo 23-6-19\tk_driver_dt.dll`
- 架构：Windows x64，MSVC C++ ABI

本记录来自静态导入表、反汇编，以及 `F:\Techik\tk_txr_system.pdb` 的 DIA 类型信息。分析过程不执行探测器命令。

## 已确认的直接调用

demo 直接导入并调用 `TkDriverDt` 的构造、析构以及以下方法：

- `Start(st_dt_info)`
- `SetCallback(st_dt_data_callback)`
- `GrabEnable(bool)`
- `SetScanDir(bool)`
- `SetScanSpeed(double)`
- `SetGain(int)` / `SetGainH(int)`
- `GetStatus(st_dt_status&)`
- `GetDevMParam(st_dt_dev_param&)`
- `GetDevSParam(st_dt_dev_param&)`
- `SetReject(...)`
- `Exit()`

demo 主窗口在偏移 `0x208` 构造 `TkDriverDt`，下一个成员位于 `0x2B0`，因此为对象保留了 `0xA8` 字节的存储区。

## 已恢复的结构尺寸

| 结构 | 尺寸 | 静态证据 |
|---|---:|---|
| `st_dt_info` | `0x68` / 104 字节 | `Start` 调用前复制 6 个 16 字节块和 1 个 8 字节块 |
| `st_dt_data_callback` | `0x10` / 16 字节 | 注册前写入函数指针和对象上下文指针 |
| `st_dt_data_frame` | `0x28` / 40 字节 | 回调入口复制 `16 + 16 + 8` 字节 |

## 图像帧关键字段

`st_dt_data_frame` 当前可确定的布局：

| 偏移 | 大小 | 含义 |
|---:|---:|---|
| `0x00` | 4 | `dt_id`，探测器编号 |
| `0x04` | 4 | 对齐填充 |
| `0x08` | 8 | `uint16_t*` 像素数据指针 |
| `0x10` | 8 | `st_dt_line_label*` 行标签数组 |
| `0x18` | 4 | 图像宽度 |
| `0x1C` | 4 | 图像高度 |
| `0x20` | 4 | 通道数 |
| `0x24` | 4 | 未知字段 |

demo 使用 16 位读取指令访问像素。缓存排列可表示为：

```text
index = row * width * channels + channel * width + x
```

这与线阵探测器每行包含多个能量/通道块的布局一致。

## 回调 ABI

回调描述结构由两个指针组成：

```cpp
struct st_dt_data_callback {
    bool (*function)(void* context, st_dt_data_frame frame);
    void* context;
};
```

在 Windows x64 ABI 下，40 字节的 `frame` 值通过隐藏指针传递。demo 回调入口检查 `context`，复制完整 40 字节帧后进入图像处理函数，并通过 `AL` 返回布尔结果。

## Demo 初始化结构的完整字段

PDB 证明 2024 运行库中的 `st_dt_info` 为 `0x98` 字节；2023 demo 使用它的早期 `0x68` 字节版本。demo 版本各偏移已全部命名，依次为：

`id`、`type`、`tk_net_id`、`sub_card_pixel`、`line_pixels`、`channels`、
`pitch_size`、`sub_frame_height`、`bind_xray_id`、扫描方向、动态暗场跟踪、
暗场像素范围、CCD binning/双移位、IAS TDI/电压阈值/软 binning，以及动态校准类型、
暗场/满场行数和目标值。

这些字段均可直接从活动 `sys_config.ini` 读取。现场配置的关键值为
`1536` 像素、`1` 通道、`50` 行子帧、`0.4` pitch、`250/1000`
校准行数和 `52428` 满场目标值。

## 当前结论和限制

原生桥接程序 `TechikDetectorBridge.exe` 已实现并通过离线 `--inspect` 验证：

- 动态加载 demo `tk_driver_dt.dll`；
- 校验构造、析构、`Start`、`SetCallback`、`GrabEnable` 和 `Exit` 导出；
- 从活动配置接收完整 104 字节启动参数；
- 把连续 50 行、16 位原始子帧聚合为 1536×300；
- 通过 Windows 命名管道送入 C# 有界内存队列，再转换为 16 位灰度 PNG；
- `.ofxraw` 文件输出仅保留为离线诊断和兼容回退，不再作为生产实时采集主链路。

当前机器没有连接真实探测器，因此只执行了 `--inspect`，没有调用 `Start`。正式投入生产前仍须在断 X 光、断料状态下验证 demo DLL 与现场 type 0 探测器的版本兼容性。

探测器实现位于 `external/techik-detector-bridge/TechikDetectorBridge.cpp`，外围设备实现位于
`TechikPeripheralRuntime.cpp`。外围设备桥接复用本次带回的 X 光、IO、传送带插件及其内部私有协议；
离线检查只验证了 DLL 加载和工厂注册。只有现场显式启用物理输出总开关后，程序才允许开启 X 光、
启动传送带或触发剔除。
