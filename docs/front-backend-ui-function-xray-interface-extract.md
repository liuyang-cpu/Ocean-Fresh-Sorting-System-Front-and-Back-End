# 海鲜分拣系统前后端页面、API 与 X 光图像读取接口提取

更新时间：2026-06-30

## 1. 当前前端交付口径

当前交付前端以 `src/OceanFresh.SortingSystem.HMI` 的 WPF HMI 为准。`ElectronHmi` 仍保留在工程中作为原型参考，但本阶段暂停作为主交付界面。

当前界面采用 Windows 工控机 HMI 风格：

- 深蓝工业软件底色；
- 左侧固定主导航；
- 首页作为生产常驻页；
- 设置页采用顶部模块标签和二级功能标签；
- 数据中心采用海鲜产品与时间范围筛选；
- 人工复核采用触控屏友好的大按钮、大图像预览和单目标裁剪复核图。

## 2. 前端页面与功能定义

### 2.1 主导航

| 导航 | 权限 | 页面 | 功能定位 |
| --- | --- | --- | --- |
| 首页 | 操作员/管理员 | `DashboardPage` | 生产常驻页面，显示 X 光图像、当前识别结果、分类统计、运行遥测和主控操作台 |
| 用户 | 操作员/管理员 | `UserCenterPage` | 当前用户、权限范围、登录时间、管理员密码维护 |
| 数据 | 操作员/管理员 | `ProductionStatisticsPage` / `ManualReviewPage` | 历史统计、检测任务记录、良率趋势、缺陷分布、异常集合人工复核 |
| 设置 | 管理员 | `SettingsPage` | 产品、模型、通道、硬件、剔除、安全预警、软件维护 |
| 退出系统 | 操作员/管理员 | 主窗口命令 | 退出当前 HMI |

> 说明：原“监控检测”能力已合并到首页。正式生产中软件常驻首页，操作员不需要在首页和监控页之间切换。

### 2.2 首页

ViewModel：`DashboardPageViewModel`

核心功能：

- 显示当前 X 光图像或 YOLO 检测结果图。
- 显示当前通道、海鲜产品、模型版本和检测状态。
- 显示当前图片识别结果矩阵：正常、碎壳、泥包、空壳等。
- 显示当前检测任务累计分类统计：正常数量、异常数量、各缺陷数量、良率。
- 显示整机遥测：传送带速度、海鲜总数、工作时长。
- 提供一个状态按钮完成机器启动/机器停止。
- 提供数据源配置：`X 光相机` 和 `本地图片目录`。

当前统计口径：

- 海鲜总数 = 当前任务所有检测目标数量。
- 正常总数 = 当前任务中模型/产品映射为正常的目标数量。
- 异常总数 = 当前任务中非正常目标数量。
- 良率 = 正常总数 / 海鲜总数。
- 真实剔除命令数 = 实际下发到剔除设备的命令数量，仅生产模式可能大于 0。

### 2.3 数据源配置

首页数据源配置用于替代旧的“导入图片 / 选择目录 / 目录联调启动”按钮组。

| 数据源 | 当前含义 | 是否创建检测任务 | 是否写入统计 | 是否执行真实剔除 |
| --- | --- | --- | --- | --- |
| `X 光相机` | 默认生产数据源，代表真实 X 光探测器/工业相机采集链路 | 是 | 是 | 是 |
| `本地图片目录` | 离线检测数据源，按固定节拍读取目录图片 | 是 | 是 | 否 |

约束：

- 机器运行中禁止切换数据源。
- 本地图片目录必须存在并包含支持格式图片。
- 本地图片目录模式只做检测分析，不生成、不执行真实剔除动作。
- 两种数据源都使用当前启用通道绑定的产品、模型和置信度阈值。

### 2.4 数据中心

ViewModel：`ProductionStatisticsPageViewModel`

核心功能：

- 按海鲜产品选择统计对象。
- 按过去 1 天、7 天、30 天筛选。
- 历史统计页签显示正常/剔除占比、缺陷数量排行、良率趋势。
- 检测任务记录页签以 `DetectionSession` 为主要对象展示明细。
- 点击检测任务可进入任务详情或人工复核。
- 报表与追溯作为后续扩展入口。

良率趋势当前设计：

- 横坐标按检测任务绘制，例如 `06-16 #1`。
- 时间范围用于过滤检测任务，不强制把 1 天切成每小时或把 7 天切成每天。
- 点击趋势点可定位到对应检测任务记录。

### 2.5 人工复核

页面：`ManualReviewPage`

复核对象：

- 当前检测任务中的异常检测框集合 `S`。
- 每个异常检测框单独成为一个复核对象。
- 预览图使用“原图 + 数据库/phase2 检测框信息”生成单目标裁剪图，只显示当前复核对象的红框。

复核结果：

| 结果 | 含义 |
| --- | --- |
| 确认异常 | 模型判定异常，人工确认确实异常 |
| 正常误检 | 模型判定异常，人工认为实际正常 |
| 类别修正 | 模型判定异常，人工认为确实异常但类别需要修正 |

复核规则：

- 所有异常对象都完成复核后，本次复核任务才算完成。
- 当前只复核异常集合 `S`，不覆盖未剔除集合抽检，因此不宣称严格召回率。
- 可计算异常确认率、误检率、类别修正数量。

### 2.6 设置页

| 一级模块 | 二级模块 | 当前功能 |
| --- | --- | --- |
| 产品设置 | 海鲜产品 | 产品名称、正常项、缺陷项、类别标签文件、标签到业务性状映射、产品级 YOLO 配置 |
| 产品设置 | 模型管理 | 模型导入、版本编号展示、产品绑定、描述编辑、删除约束 |
| 产品设置 | 通道配置 | 通道名称、绑定产品、绑定模型、置信度阈值、启停状态 |
| 硬件设置 | 设备管理 | X 光光源、X 光探测器、传送带、剔除设备、控制器状态和自检 |
| 剔除设置 | 剔除控制 | 剔除方式、剔除位、气吹/推杆/翻板/下沉等参数 |
| 剔除设置 | 安全预警 | 关键硬件故障告警、联锁状态、告警确认 |
| 系统设置 | 软件维护 | 软件版本、构建时间、数据库版本、YOLO 服务版本、更新包校验 |

## 3. 后端 LocalApi 接口

默认地址：

```text
http://127.0.0.1:5188/
```

### 3.1 认证

| 方法 | 路径 | 功能 |
| --- | --- | --- |
| `POST` | `/api/auth/operator-login` | 操作员无密码登录 |
| `POST` | `/api/auth/admin-login` | 管理员密码登录 |
| `POST` | `/api/auth/admin-password` | 修改管理员密码 |

### 3.2 产品、模型、通道

| 方法 | 路径 | 功能 |
| --- | --- | --- |
| `GET` | `/api/products` | 获取海鲜产品、性状、类别映射和产品配置 |
| `POST` | `/api/products` | 新增或编辑海鲜产品 |
| `DELETE` | `/api/products/{productId}` | 删除海鲜产品 |
| `GET` | `/api/products/predict-config-template` | 获取内置 YOLO predict 配置模板 |
| `GET` | `/api/models` | 获取模型列表 |
| `GET` | `/api/models/category/{categoryId}` | 按类别获取模型 |
| `POST` | `/api/models/import` | 导入模型 |
| `POST` | `/api/models` | 新增或编辑模型 |
| `DELETE` | `/api/models/{modelId}` | 删除模型 |
| `GET` | `/api/channels` | 获取通道列表 |
| `POST` | `/api/channels` | 新增或编辑通道 |
| `DELETE` | `/api/channels/{channelId}` | 删除通道 |

当前产品关键字段：

- `Code`
- `Name`
- `ClassesFilePath`
- `LabelMapJson`
- `PredictConfigPath`
- `Traits`

当前模型关键字段：

- `SeafoodCategoryId`
- `Version`
- `SourceWeightPath`
- `Notes`
- `Status`
- `CreatedAt`

当前通道关键字段：

- `ChannelNo`
- `Name`
- `SeafoodProductId`
- `ModelVersionId`
- `ModelPath`
- `ConfidenceThreshold`
- `IsEnabled`
- `LastRuntimePredictConfigPath`
- `LastRuntimeOutputDirectory`
- `CameraToEjectDistanceMillimeters`
- `MillimetersPerPixelY`
- `SoftwareLatencyMilliseconds`
- `ActuatorDelayMilliseconds`
- `HorizontalLaneMappingJson`

说明：

- 传送带速度已归入整机运行参数，默认值在 `MachineRuntimeDefaults.ConveyorSpeedMetersPerSecond`。
- X 光电压、电流已归入整机/硬件默认参数，默认值在 `ChannelRuntimeDefaults`。
- 剔除动作参数应进入剔除设置，不再作为通道编辑页的主要配置项。

### 3.3 运行控制与数据源

| 方法 | 路径 | 功能 |
| --- | --- | --- |
| `GET` | `/api/runtime/dashboard` | 获取首页聚合数据 |
| `GET` | `/api/runtime/snapshot` | 获取运行快照 |
| `GET` | `/api/runtime/data-source` | 获取当前数据源配置 |
| `POST` | `/api/runtime/data-source` | 更新数据源配置 |
| `POST` | `/api/runtime/machine/start` | 启动机器 |
| `POST` | `/api/runtime/machine/stop` | 停止机器 |
| `POST` | `/api/runtime/detection/start` | 开始检测任务 |
| `POST` | `/api/runtime/detection/stop` | 停止检测任务 |
| `POST` | `/api/runtime/manual-infer` | 内部调试：单张图片推理 |
| `POST` | `/api/runtime/stream-record` | 内部调试：写入流式检测记录 |

`POST /api/runtime/data-source` 请求：

```json
{
  "mode": "XrayCamera",
  "localDirectoryPath": null,
  "frameIntervalMilliseconds": 68
}
```

或：

```json
{
  "mode": "LocalImageDirectory",
  "localDirectoryPath": "E:\\xray-images\\youge",
  "frameIntervalMilliseconds": 68
}
```

### 3.4 统计与人工复核

| 方法 | 路径 | 功能 |
| --- | --- | --- |
| `GET` | `/api/statistics/production?range=1d|7d|30d` | 获取生产统计、检测任务和趋势数据 |
| `GET` | `/api/statistics/sessions/{sessionId}/manual-review` | 获取某检测任务的异常对象复核列表 |
| `GET` | `/api/statistics/sessions/{sessionId}/manual-review/preview?inspectionRecordId=...&detectionId=...` | 获取单目标裁剪复核图 |
| `POST` | `/api/statistics/sessions/{sessionId}/manual-review` | 保存复核结果 |

### 3.5 设备、告警、软件维护

| 方法 | 路径 | 功能 |
| --- | --- | --- |
| `GET` | `/api/devices` | 获取设备列表 |
| `POST` | `/api/devices` | 新增或编辑设备 |
| `POST` | `/api/devices/{deviceId}/self-check` | 设备自检 |
| `DELETE` | `/api/devices/{deviceId}` | 删除设备 |
| `GET` | `/api/alarms/active` | 获取当前未确认告警 |
| `POST` | `/api/alarms/{alarmId}/acknowledge` | 确认告警 |
| `GET` | `/api/system/software-version` | 获取软件版本信息 |
| `POST` | `/api/system/software-update/validate` | 校验离线更新包 |

## 4. X 光图像读取链路

### 4.1 当前统一链路

```text
数据源配置
  ├─ X 光相机
  │    └─ 生产采集链路，允许剔除输出
  └─ 本地图片目录
       └─ 离线检测链路，不允许剔除输出
        ↓
当前启用通道
        ↓
产品标签映射 + 产品级 predict 配置 + 通道置信度
        ↓
YOLO FastAPI 服务
        ↓
检测结果 / phase2 后处理结果
        ↓
InspectionRecord + DetectionSession
        ↓
首页实时统计 / 数据中心 / 人工复核
```

### 4.2 X 光相机模式

当前第一版可以继续使用文件夹监听作为真实采集链路的过渡方式：

- PLC 或 X 光探测器采集程序将图片写入采集目录；
- 后端 worker 监听新图片；
- 图片稳定后进入当前启用通道推理；
- 非正常结果在生产模式下可生成并执行剔除命令。

建议保留的环境变量：

| 环境变量 | 默认值 | 说明 |
| --- | --- | --- |
| `OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY` | `%LocalAppData%\OceanFreshSortingSystem\plc-xray-input` | PLC 或 X 光采集程序写入图片的目录 |
| `OCEANFRESH_PLC_XRAY_POLL_MS` | `20` | 目录轮询间隔 |
| `OCEANFRESH_PLC_XRAY_STABLE_MS` | `40` | 文件稳定等待时间 |
| `OCEANFRESH_PLC_XRAY_PROCESS_EXISTING` | `false` | 是否处理目录已有旧图片 |

### 4.3 本地图片目录模式

本地图片目录模式用于离线检测、演示和批量验证：

- 按文件名顺序读取目录图片；
- 默认按 `68ms/帧` 送入检测链路；
- 创建 `DetectionSession`；
- 写入 `InspectionRecord`；
- 正常计算总数、异常数、良率和缺陷分布；
- 不生成、不执行真实 `EjectCommand`。

### 4.4 未来真实相机接口建议

建议新增或保留统一帧模型：

```csharp
public sealed record XrayFrame(
    string FrameId,
    byte[] ImageBytes,
    int Width,
    int Height,
    DateTimeOffset CapturedAt,
    long? EncoderPosition,
    string? SourceDeviceId);
```

建议由采集适配器实现：

```csharp
public interface IXrayFrameSource
{
    IAsyncEnumerable<XrayFrame> ReadFramesAsync(CancellationToken cancellationToken);
}
```

适配器可来自：

- 厂商 SDK；
- GigE Vision / GenICam；
- Camera Link / CoaXPress；
- USB3 Vision；
- PLC/采集程序写目录；
- gRPC/TCP 流；
- 共享内存或环形缓冲区。

## 5. 当前 YOLO 服务接入

当前推荐路径仍是 FastAPI YOLO 服务：

- 服务启动后加载模型并常驻内存；
- 后续请求复用模型，避免每帧重复加载权重；
- 保留原 Python predict 与 phase2 后处理逻辑；
- 支持相邻帧去重；
- 返回检测框、标签、置信度、后处理图路径和耗时信息。

后端使用产品层 `LabelMapJson` 将 `class_id` 转换为业务性状名称，例如 `正常`、`碎壳`、`泥包`、`空壳`。

## 6. 数据闭环

1. 管理员维护海鲜产品、性状、类别标签文件和标签映射。
2. 管理员导入模型并绑定海鲜产品。
3. 管理员配置通道并绑定产品、模型和置信度阈值。
4. 操作员在首页选择数据源。
5. 操作员点击机器启动。
6. 系统创建检测任务。
7. 图像进入 YOLO 服务推理。
8. 系统写入检测记录和统计数据。
9. 生产模式下异常目标可生成真实剔除命令。
10. 离线模式下异常目标只用于统计和复核。
11. 数据中心按产品和检测任务查看历史统计。
12. 人工复核页面对异常集合进行模型能力评估。

## 7. 仍需后续确认的接口边界

| 问题 | 当前建议 |
| --- | --- |
| 真实 X 光探测器 SDK 未确定 | 继续通过 `IXrayFrameSource` 或等价适配层隔离 |
| PLC 协议未确定 | 优先支持 Modbus TCP / OPC UA / PROFINET / EtherNet/IP 中现场可用协议 |
| 每 68ms 一帧是否走 HTTP | 正式生产不建议每帧 HTTP 进入 LocalApi，优先使用进程内 SDK、gRPC 流或共享内存 |
| 结果图保存策略 | 建议优先保存异常图和复核裁剪图，正常图按任务或时间清理 |
| 多通道并发 | 当前按单当前运行通道设计，多通道并发需要重新设计队列、统计和剔除控制 |
