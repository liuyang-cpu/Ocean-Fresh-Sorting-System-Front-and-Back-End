# Ocean Fresh 现场离线部署

## 固定安装目录

整套软件放在：

```text
E:\deepLearning\deploy
```

不要求压缩，也不需要安装到 C 盘。请复制整个 `deploy` 文件夹并保持目录结构不变。

主要目录：

- `PythonRuntime`：Python 3.11、PyTorch 2.5.0、CUDA 11.8、Ultralytics 8.3.163；
- `YoloService`：本地 YOLO FastAPI 服务和当前油蛤模型；
- `TechikRuntime`：带回的 Techik 配置、产品参数和外围设备插件；
- `DetectorSdk`：探测器 demo SDK；
- `HardwareBridge`：Ocean Fresh 原生硬件桥接；
- `Data`：数据库、探测器图像、推理输入输出和日志，全部保存在 E 盘。

## 现场顺序

1. 确认 NVIDIA 驱动已经安装，Windows 驱动版本应不低于 `452.39`；
2. 确认原 `Techik.exe` 已退出，避免探测器和 COM 口被占用；
3. 双击 `check-environment.cmd`，确认三项均显示 `OK`；
4. 保持 `field-config.cmd` 中 `OCEANFRESH_TECHIK_ENABLE_OUTPUT=false`；
5. 双击 `start-oceanfresh.cmd` 启动软件；
6. 先检查探测器、X 光源、传送带和 IO 在线状态；
7. 完成断料、安全隔离、急停和喷嘴映射验证后，才允许启用物理输出。

`check-environment.cmd` 只加载 Python、模型依赖和 Techik 插件，不连接真实设备、不启动 X 光、
不运行传送带，也不触发剔除。

## 当前推理基线

- 模型：`YoloService\models\oil_clam\v1\best.pt`
- 模型类别：`broken`、`normal`、`muddy`、`empty`
- 训练分辨率：`640`
- 推理设备：`0`（第一块 NVIDIA 显卡）
- CUDA Runtime：`11.8`

GT 1030 可以尝试运行该 CUDA 11.8 环境，但显存和速度明显弱于开发机。现场应重点记录模型预热时间、
连续帧推理耗时、显存占用和是否出现 CUDA out-of-memory。若无法满足输送带节拍，应降低采集/推理频率，
或更换更高性能显卡，而不是直接降低模型训练分辨率。
