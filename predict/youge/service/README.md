# Ocean Fresh YOLO FastAPI Service

这个目录保留旧版部署路径，并把项目内置 YOLO 推断程序封装成一个本地 `FastAPI` 推理服务。

新代码应使用产品无关的 `predict/yolo` 命名；`predict/youge` 目录仅作为兼容入口保留。产品配置只允许覆盖 `adjacent_frame_height_min` 与 `adjacent_frame_height_max`，`imgsz` 由模型版本元数据提供。

## 目录

- `app.py`
  FastAPI 服务入口。
- `requirements.txt`
  Python 依赖。
- `start_yolo_fastapi_service.bat`
  一键启动脚本。

## 依赖

建议使用独立 Python 环境，至少安装：

```bash
pip install -r requirements.txt
```

如果你的 `torch / ultralytics` 需要特定 CUDA 版本，按现场环境替换成对应版本即可。

当前这台机器已经验证可直接使用：

- `E:\Users\liuyang\anaconda3\envs\yolo\python.exe`
- `ultralytics 8.3.163`
- `torch 2.5.0`
- `fastapi 0.115.0`
- `uvicorn 0.30.6`

## 启动

双击：

`start_yolo_fastapi_service.bat`

或者手动运行：

```bash
python -m uvicorn app:app --host 127.0.0.1 --port 8010
```

可选环境变量：

- `OCEANFRESH_YOLO_PYTHON`
  指定 Python 可执行文件路径。
- `OCEANFRESH_YOLO_HOST`
  服务地址，默认 `127.0.0.1`
- `OCEANFRESH_YOLO_PORT`
  服务端口，默认 `8010`

如果不设置 `OCEANFRESH_YOLO_PYTHON`，启动脚本会优先尝试：

`E:\Users\liuyang\anaconda3\envs\yolo\python.exe`

## 接口

### 1. 健康检查

`GET /health`

### 2. 初始化通道专属 predict 配置

`POST /channel-configs/initialize`

示例请求：

```json
{
  "channel_no": 1,
  "channel_name": "1号通道"
}
```

默认会在：

`predict/youge/service_runtime/channel_configs/channel-01-predict.json`

生成一份该通道自己的配置副本。

### 3. 执行推理

`POST /infer`

示例请求：

```json
{
  "model_path": "C:\\\\models\\\\youge\\\\best.pt",
  "source_path": "C:\\\\data\\\\xray\\\\frame001.png",
  "config_path": "C:\\\\repo\\\\predict\\\\youge\\\\service_runtime\\\\channel_configs\\\\channel-01-predict.json",
  "confidence_threshold": 0.58,
  "timeout_seconds": 300
}
```

返回内容包括：

- 本次运行生成的 runtime config 路径
- 输出目录
- `phase2_postprocess_summary.json` 路径
- stdout / stderr 日志路径
- 归一后的检测框结果

## 运行产物

服务运行时会在以下目录生成文件：

- `predict/youge/service_runtime/channel_configs/`
- `predict/youge/service_runtime/runs/`
- `predict/youge/service_runtime/logs/`

## 当前说明

这版服务的目标是“最大程度保留原始 Python 脚本和 phase2 后处理逻辑”，因此：

- 服务内部仍然是调用 `predict_youge.py`
- 不会重写你原来的后处理
- 更适合作为系统与算法之间的过渡方案
