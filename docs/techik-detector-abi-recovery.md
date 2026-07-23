# Techik 探测器 demo ABI 静态恢复记录

分析对象：

- `F:\tk_driver_dt_demo 23-6-19\tk_driver_dt_demo.exe`
- `F:\tk_driver_dt_demo 23-6-19\tk_driver_dt.dll`
- 架构：Windows x64，MSVC C++ ABI

本记录来自静态导入表和反汇编，不执行探测器命令。

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
| `0x00` | 8 | 未知标识或时间信息 |
| `0x08` | 8 | `uint16_t*` 像素数据指针 |
| `0x10` | 8 | 每通道元数据数组指针，demo 按 16 字节步长访问 |
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

## 初始化结构的已知默认值

demo 在调用 `Start` 前构造 104 字节初始化数据。其中可以确认出现以下值：

- `128`
- `1024`
- `1`
- `0.4`
- `250`
- `1000`
- `52428`
- `0.2`
- `512`
- `50000`
- `25`
- `-200`
- `0.075`

其中 `128/1024/1/0.4/250/1000/52428/0.2` 与现场 `sys_config.ini` 中的 `sub_card_pixel`、`line_pixels`、`channels`、`pitch_size` 和动态校准参数高度对应。剩余偏移尚需继续命名，当前不得直接用于生产硬件初始化。

## 当前结论和限制

现有 demo 足以恢复一个原生 C++桥接层所需的主要 ABI：对象构造、启动、回调注册、采集开关以及帧像素布局。当前还不能安全执行 `Start`，因为 `st_dt_info` 中仍有未命名字段，并且尚未在真实探测器上验证版本兼容性。

实验性定义位于 `external/techik-detector-bridge/RecoveredTechikDetectorAbi.hpp`。该文件只用于离线编译和继续分析，不应在现场直接启用硬件输出或 X 光控制。
