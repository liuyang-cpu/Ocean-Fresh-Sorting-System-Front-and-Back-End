from __future__ import annotations

import asyncio
import json
import os
import shutil
import sys
import threading
import time
import uuid
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException
from PIL import Image
from pydantic import BaseModel, Field
from ultralytics import YOLO
import torch


SERVICE_DIR = Path(__file__).resolve().parent
YOLO_PACKAGE_DIR = (
    SERVICE_DIR
    if (SERVICE_DIR / "predict_youge.py").exists()
    else SERVICE_DIR.parent
)
PREDICT_SCRIPT_PATH = YOLO_PACKAGE_DIR / "predict_youge.py"  # Legacy filename; module is product-neutral.
TEMPLATE_CONFIG_PATH = YOLO_PACKAGE_DIR / "predict_youge.json"  # Legacy path kept for deployed bundles.
SERVICE_RUNTIME_ROOT = YOLO_PACKAGE_DIR / "service_runtime"
PRODUCT_CONFIG_ROOT = SERVICE_RUNTIME_ROOT / "product_configs"
LEGACY_CHANNEL_CONFIG_ROOT = SERVICE_RUNTIME_ROOT / "channel_configs"
RUN_ROOT = SERVICE_RUNTIME_ROOT / "runs"
LOG_ROOT = SERVICE_RUNTIME_ROOT / "logs"

PRODUCT_POSTPROCESS_KEYS = {
    "adjacent_frame_height_min",
    "adjacent_frame_height_max",
}


def ensure_service_directories() -> None:
    PRODUCT_CONFIG_ROOT.mkdir(parents=True, exist_ok=True)
    LEGACY_CHANNEL_CONFIG_ROOT.mkdir(parents=True, exist_ok=True)
    RUN_ROOT.mkdir(parents=True, exist_ok=True)
    LOG_ROOT.mkdir(parents=True, exist_ok=True)


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise ValueError(f"JSON root is not an object: {path}")
    return payload


def save_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2)


def choose_python_executable() -> str:
    return os.environ.get("OCEANFRESH_YOLO_PYTHON") or sys.executable


def load_predict_module(script_path: Path):
    import importlib.util

    spec = importlib.util.spec_from_file_location("oceanfresh_yolo_predict", script_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Unable to load predict module from: {script_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_predict_defaults(config_path: Path | None) -> dict[str, Any]:
    defaults = {
        "weights": None,
        "source": None,
        "imgsz": None,
        "device": "0",
        "conf": 0.25,
        "iou": 0.4,
        "agnostic_nms": True,
        "project": "runs/predict",
        "name": "yolo_predict",
        "adjacent_frame_dedup": True,
        "adjacent_frame_edge_priority_rule": True,
        "same_class_contain_suppression": True,
        "contain_ratio_threshold": 0.5,
        "adjacent_frame_x_overlap_threshold": 0.35,
        "adjacent_frame_delta_y": 250.0,
        "adjacent_frame_height_min": 35.64,
        "adjacent_frame_height_max": 119.95,
        "adjacent_frame_height_confidence_offset_px": 1.0,
        "adjacent_frame_non_bottom_y2_correction": 15.0,
        "adjacent_frame_bottom_touch_margin_px": 3.0,
        "adjacent_frame_x_tolerance": 12.0,
        "export_penalty_hits": True,
        "penalty_hits_dirname": "penalty_hits",
        "render_adjacent_frame_guides": False,
        "exist_ok": True,
        "save_txt": True,
        "save_conf": True,
    }
    if config_path is None or not config_path.exists():
        return defaults
    user_config = load_json(config_path)
    for key, value in user_config.items():
        if value is not None:
            defaults[key] = value
    return defaults


def resolve_image_size(requested_image_size: int | None, config: dict[str, Any]) -> int:
    value = requested_image_size if requested_image_size is not None else config.get("imgsz")
    try:
        image_size = int(value)
    except (TypeError, ValueError) as exc:
        raise ValueError("模型训练分辨率 imgsz 未配置，请从模型版本元数据传入 image_size。") from exc
    if image_size <= 0:
        raise ValueError("模型训练分辨率 imgsz 必须为正整数。")
    return image_size


def apply_product_postprocess_overrides(config: dict[str, Any], overrides: dict[str, Any]) -> None:
    unsupported = sorted(set(overrides) - PRODUCT_POSTPROCESS_KEYS)
    if unsupported:
        raise ValueError(f"产品配置包含不允许的参数: {', '.join(unsupported)}")
    for key, value in overrides.items():
        config[key] = value

    height_min = float(config["adjacent_frame_height_min"])
    height_max = float(config["adjacent_frame_height_max"])
    if height_min <= 0 or height_max <= height_min:
        raise ValueError("产品高度范围必须满足 0 < adjacent_frame_height_min < adjacent_frame_height_max。")


def safe_int(value: Any, default: int = -1) -> int:
    if value is None:
        return default
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def safe_float(value: Any, default: float = 0.0) -> float:
    if value is None:
        return default
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


class HealthResponse(BaseModel):
    status: str
    predict_script_path: str
    template_config_path: str
    python_executable: str
    inference_mode: str
    cached_models: list[str]
    active_stream_sessions: int


class ChannelConfigInitRequest(BaseModel):
    channel_no: int = Field(..., gt=0)
    channel_name: str | None = None
    target_path: str | None = None
    overwrite: bool = False


class ChannelConfigInitResponse(BaseModel):
    channel_no: int
    channel_name: str
    config_path: str


class ProductConfigInitRequest(BaseModel):
    product_code: str = Field(..., min_length=1)
    target_path: str | None = None
    overwrite: bool = False


class ProductConfigInitResponse(BaseModel):
    product_code: str
    config_path: str


class InferRequest(BaseModel):
    model_path: str
    source_path: str
    config_path: str | None = None
    image_size: int | None = Field(default=None, gt=0)
    confidence_threshold: float | None = Field(default=None, gt=0.0, le=1.0)
    run_name: str | None = None
    output_root: str | None = None
    timeout_seconds: int = Field(default=300, ge=1, le=3600)
    config_overrides: dict[str, Any] = Field(default_factory=dict)


class DetectionItem(BaseModel):
    image_name: str
    class_id: int
    final_conf: float
    xyxy: list[float]
    x_center_px: float
    y_center_px: float
    phase2_decision: str | None = None
    applied_rules: list[str] = Field(default_factory=list)
    raw: dict[str, Any]


class InferResponse(BaseModel):
    run_id: str
    runtime_config_path: str
    output_directory: str
    summary_path: str
    stdout_log_path: str
    stderr_log_path: str
    rendered_image_path: str | None = None
    model_inference_milliseconds: float
    postprocess_milliseconds: float
    service_overhead_milliseconds: float
    request_total_milliseconds: float
    detections: list[DetectionItem]


class StreamSessionStartRequest(BaseModel):
    model_path: str
    config_path: str | None = None
    image_size: int | None = Field(default=None, gt=0)
    confidence_threshold: float | None = Field(default=None, gt=0.0, le=1.0)
    session_name: str | None = None
    output_root: str | None = None
    frame_interval_milliseconds: int = Field(default=68, ge=1, le=10_000)
    config_overrides: dict[str, Any] = Field(default_factory=dict)


class ModelPreloadRequest(BaseModel):
    model_path: str
    config_path: str | None = None
    image_size: int | None = Field(default=None, gt=0)
    config_overrides: dict[str, Any] = Field(default_factory=dict)


class ModelPreloadResponse(BaseModel):
    model_path: str
    preload_milliseconds: float
    warmup_milliseconds: float
    already_warmed: bool


class StreamSessionStartResponse(BaseModel):
    session_id: str
    runtime_config_path: str
    output_directory: str
    frame_interval_milliseconds: int
    model_preload_milliseconds: float
    message: str


class StreamFrameInferRequest(BaseModel):
    source_path: str
    frame_name: str | None = None
    timeout_seconds: int = Field(default=120, ge=1, le=3600)


class StreamFrameInferResponse(BaseModel):
    session_id: str
    received_frame_name: str
    finalized_frame_name: str | None = None
    finalized_image_path: str | None = None
    has_finalized_output: bool
    buffered_frame_name: str | None = None
    summary_path: str
    model_inference_milliseconds: float
    postprocess_milliseconds: float
    service_overhead_milliseconds: float
    request_total_milliseconds: float
    detections: list[DetectionItem]
    message: str


class StreamSessionFinishResponse(BaseModel):
    session_id: str
    finalized_frame_name: str | None = None
    finalized_image_path: str | None = None
    summary_path: str
    postprocess_milliseconds: float
    service_overhead_milliseconds: float
    request_total_milliseconds: float
    detections: list[DetectionItem]
    message: str


@dataclass
class CachedModel:
    path: str
    instance: YOLO
    lock: threading.Lock = field(default_factory=threading.Lock)
    warmed_signatures: set[str] = field(default_factory=set)


class YoloModelRegistry:
    def __init__(self) -> None:
        self._models: dict[str, CachedModel] = {}
        self._lock = threading.Lock()

    def get_or_load(self, model_path: Path) -> CachedModel:
        normalized = str(model_path)
        cached = self._models.get(normalized)
        if cached is not None:
            return cached

        with self._lock:
            cached = self._models.get(normalized)
            if cached is not None:
                return cached
            instance = YOLO(normalized)
            cached = CachedModel(path=normalized, instance=instance)
            self._models[normalized] = cached
            return cached

    def list_paths(self) -> list[str]:
        with self._lock:
            return sorted(self._models.keys())


@dataclass
class StreamSessionState:
    session_id: str
    model_path: Path
    base_config_path: Path
    runtime_config_path: Path
    run_root: Path
    save_dir: Path
    labels_dir: Path
    penalty_hits_dir: Path | None
    predict_config: dict[str, Any]
    frame_interval_milliseconds: int
    summary: list[dict[str, Any]] = field(default_factory=list)
    pending_frame_entry: dict[str, Any] | None = None
    received_frame_count: int = 0
    lock: threading.Lock = field(default_factory=threading.Lock, repr=False)

    @property
    def summary_path(self) -> Path:
        return self.save_dir / "phase2_postprocess_summary.json"


class StreamSessionRegistry:
    def __init__(self) -> None:
        self._sessions: dict[str, StreamSessionState] = {}
        self._lock = threading.Lock()

    def add(self, session: StreamSessionState) -> None:
        with self._lock:
            self._sessions[session.session_id] = session

    def get(self, session_id: str) -> StreamSessionState | None:
        with self._lock:
            return self._sessions.get(session_id)

    def remove(self, session_id: str) -> StreamSessionState | None:
        with self._lock:
            return self._sessions.pop(session_id, None)

    def count(self) -> int:
        with self._lock:
            return len(self._sessions)


model_registry = YoloModelRegistry()
stream_session_registry = StreamSessionRegistry()
predict_module = None


app = FastAPI(
    title="Ocean Fresh YOLO Inference Service",
    version="0.2.0",
    summary="Runs embedded YOLO predict with in-memory model caching and phase2 postprocess.",
)

@app.on_event("startup")
async def on_startup() -> None:
    global predict_module

    ensure_service_directories()
    if not PREDICT_SCRIPT_PATH.exists():
        raise RuntimeError(f"Missing embedded predict script: {PREDICT_SCRIPT_PATH}")
    if not TEMPLATE_CONFIG_PATH.exists():
        raise RuntimeError(f"Missing embedded predict config template: {TEMPLATE_CONFIG_PATH}")

    if str(YOLO_PACKAGE_DIR.parent.parent) not in sys.path:
        sys.path.insert(0, str(YOLO_PACKAGE_DIR.parent.parent))

    predict_module = load_predict_module(PREDICT_SCRIPT_PATH)
    if torch.cuda.is_available():
        torch.backends.cudnn.benchmark = True


@app.get("/health", response_model=HealthResponse)
async def health() -> HealthResponse:
    ensure_service_directories()
    return HealthResponse(
        status="ok",
        predict_script_path=str(PREDICT_SCRIPT_PATH),
        template_config_path=str(TEMPLATE_CONFIG_PATH),
        python_executable=choose_python_executable(),
        inference_mode="in_memory_yolo",
        cached_models=model_registry.list_paths(),
        active_stream_sessions=stream_session_registry.count(),
    )


@app.post("/channel-configs/initialize", response_model=ChannelConfigInitResponse)
async def initialize_channel_config(request: ChannelConfigInitRequest) -> ChannelConfigInitResponse:
    ensure_service_directories()
    target_path = Path(request.target_path).expanduser().resolve() if request.target_path else (
        LEGACY_CHANNEL_CONFIG_ROOT / f"channel-{request.channel_no:02d}-predict.json"
    )


@app.post("/product-configs/initialize", response_model=ProductConfigInitResponse)
async def initialize_product_config(request: ProductConfigInitRequest) -> ProductConfigInitResponse:
    """Create the two-field product postprocess override used by new integrations."""
    ensure_service_directories()
    safe_code = "".join(character if character.isalnum() else "_" for character in request.product_code)
    target_path = Path(request.target_path).expanduser().resolve() if request.target_path else (
        PRODUCT_CONFIG_ROOT / f"{safe_code}-postprocess.json"
    )
    if target_path.exists() and not request.overwrite:
        raise HTTPException(status_code=409, detail=f"配置文件已存在: {target_path}")

    save_json(
        target_path,
        {
            "schema_version": 1,
            "adjacent_frame_height_min": 35.64,
            "adjacent_frame_height_max": 119.95,
        },
    )
    return ProductConfigInitResponse(product_code=request.product_code, config_path=str(target_path))
    if target_path.exists() and not request.overwrite:
        raise HTTPException(status_code=409, detail=f"配置文件已存在: {target_path}")

    shutil.copyfile(TEMPLATE_CONFIG_PATH, target_path)
    payload = load_json(target_path)
    payload["name"] = f"channel_{request.channel_no:02d}_predict"
    save_json(target_path, payload)

    return ChannelConfigInitResponse(
        channel_no=request.channel_no,
        channel_name=request.channel_name or f"{request.channel_no}号通道",
        config_path=str(target_path),
    )


def build_effective_config(
    *,
    base_config_path: Path,
    request: InferRequest,
    model_path: Path,
    source_path: Path,
    run_root: Path,
) -> dict[str, Any]:
    payload = load_predict_defaults(base_config_path if base_config_path.exists() else None)
    payload["weights"] = str(model_path)
    payload["source"] = str(source_path)
    payload["project"] = str(run_root)
    payload["name"] = "predict_run"
    payload["imgsz"] = resolve_image_size(request.image_size, payload)
    if request.confidence_threshold is not None:
        payload["conf"] = request.confidence_threshold
    apply_product_postprocess_overrides(payload, request.config_overrides)
    return payload


def build_stream_session_config(
    *,
    base_config_path: Path,
    request: StreamSessionStartRequest,
    model_path: Path,
    run_root: Path,
) -> dict[str, Any]:
    payload = load_predict_defaults(base_config_path if base_config_path.exists() else None)
    payload["weights"] = str(model_path)
    payload["source"] = str(run_root / "stream_input")
    payload["project"] = str(run_root)
    payload["name"] = "predict_stream"
    payload["exist_ok"] = True
    payload["imgsz"] = resolve_image_size(request.image_size, payload)
    if request.confidence_threshold is not None:
        payload["conf"] = request.confidence_threshold
    apply_product_postprocess_overrides(payload, request.config_overrides)
    return payload


def normalize_frame_output_name(
    *,
    sequence_no: int,
    source_path: Path,
    frame_name: str | None,
) -> str:
    if frame_name:
        candidate = Path(frame_name).name
        if not Path(candidate).suffix:
            candidate = f"{candidate}{source_path.suffix or '.png'}"
    else:
        candidate = source_path.name
    return f"{sequence_no:06d}__{candidate}"


def build_detection_items(summary_items: list[dict[str, Any]]) -> list[DetectionItem]:
    detections: list[DetectionItem] = []
    for item in summary_items:
        xyxy = [safe_float(x) for x in item.get("xyxy", [])]
        x_center = safe_float(item.get("x_center_px"), (xyxy[0] + xyxy[2]) / 2 if len(xyxy) == 4 else 0.0)
        y_center = safe_float(item.get("y_center_px"), (xyxy[1] + xyxy[3]) / 2 if len(xyxy) == 4 else 0.0)
        detections.append(
            DetectionItem(
                image_name=str(item.get("image_name", "")),
                class_id=safe_int(item.get("class_id"), -1),
                final_conf=safe_float(item.get("final_conf", item.get("base_conf", 0.0)), 0.0),
                xyxy=xyxy,
                x_center_px=x_center,
                y_center_px=y_center,
                phase2_decision=item.get("phase2_decision"),
                applied_rules=[str(x) for x in item.get("applied_rules", [])],
                raw=item,
            )
        )
    return detections


def write_stream_summary(session: StreamSessionState) -> None:
    session.summary_path.write_text(
        json.dumps(session.summary, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def infer_single_frame_entry(
    *,
    session: StreamSessionState,
    source_path: Path,
    frame_name: str | None,
) -> tuple[dict[str, Any], float]:
    if predict_module is None:
        raise RuntimeError("predict 模块未初始化。")

    cached_model = model_registry.get_or_load(session.model_path)
    began = time.perf_counter()
    with cached_model.lock:
        results = cached_model.instance.predict(
            source=str(source_path),
            imgsz=int(session.predict_config["imgsz"]),
            device=str(session.predict_config["device"]),
            conf=float(session.predict_config["conf"]),
            iou=float(session.predict_config["iou"]),
            agnostic_nms=bool(session.predict_config.get("agnostic_nms", False)),
            half=bool(session.predict_config.get("half", False)),
            project=str(session.run_root),
            name="predict_stream",
            exist_ok=True,
            save=False,
            save_txt=False,
            save_conf=False,
            verbose=False,
        )

    inference_ms = (time.perf_counter() - began) * 1000.0

    if not results:
        raise RuntimeError(f"YOLO 未返回任何结果: {source_path}")

    result = results[0]
    output_name = normalize_frame_output_name(
        sequence_no=session.received_frame_count,
        source_path=source_path,
        frame_name=frame_name,
    )
    frame_entry = predict_module.build_frame_entry(
        result=result,
        output_path=session.save_dir / output_name,
        same_class_contain_suppression=bool(session.predict_config.get("same_class_contain_suppression", False)),
        contain_ratio_threshold=float(session.predict_config.get("contain_ratio_threshold", 0.5)),
    )
    frame_entry["output_name"] = output_name
    frame_entry["output_path"] = session.save_dir / output_name
    for detection in frame_entry["detections"]:
        detection["image_name"] = output_name
    return frame_entry, inference_ms


def warm_up_cached_model(
    *,
    cached_model: CachedModel,
    predict_config: dict[str, Any],
    run_root: Path,
) -> tuple[float, bool]:
    warmup_signature = json.dumps(
        {
            "imgsz": int(predict_config["imgsz"]),
            "device": str(predict_config["device"]),
            "half": predict_config.get("half"),
        },
        sort_keys=True,
    )
    warmup_dir = run_root / "_warmup"
    warmup_dir.mkdir(parents=True, exist_ok=True)
    warmup_image_path = warmup_dir / "warmup.png"
    if not warmup_image_path.exists():
        Image.new("RGB", (1536, 300), color=(0, 0, 0)).save(warmup_image_path)

    began = time.perf_counter()
    with cached_model.lock:
        if warmup_signature in cached_model.warmed_signatures:
            return 0.0, True
        cached_model.instance.predict(
            source=str(warmup_image_path),
            imgsz=int(predict_config["imgsz"]),
            device=str(predict_config["device"]),
            conf=float(predict_config["conf"]),
            iou=float(predict_config["iou"]),
            agnostic_nms=bool(predict_config.get("agnostic_nms", False)),
            half=bool(predict_config.get("half", False)),
            project=str(warmup_dir),
            name="predict_warmup",
            exist_ok=True,
            save=False,
            save_txt=False,
            save_conf=False,
            verbose=False,
        )
        cached_model.warmed_signatures.add(warmup_signature)
    return (time.perf_counter() - began) * 1000.0, False


def finalize_stream_frame(
    *,
    session: StreamSessionState,
    frame_entry: dict[str, Any],
) -> list[dict[str, Any]]:
    began = time.perf_counter()
    summary_items = predict_module.finalize_frame_entry(
        frame_entry=frame_entry,
        labels_dir=session.labels_dir,
        save_txt=bool(session.predict_config.get("save_txt", False)),
        save_conf=bool(session.predict_config.get("save_conf", False)),
        penalty_hits_dir=session.penalty_hits_dir,
        render_adjacent_frame_guides=bool(session.predict_config.get("render_adjacent_frame_guides", False)),
        adjacent_frame_dedup=bool(session.predict_config.get("adjacent_frame_dedup", False)),
        adjacent_frame_delta_y=float(session.predict_config.get("adjacent_frame_delta_y", 250.0)),
        adjacent_frame_height_max=float(session.predict_config.get("adjacent_frame_height_max", 119.95)),
    )
    postprocess_ms = (time.perf_counter() - began) * 1000.0
    session.summary.extend(summary_items)
    return summary_items, postprocess_ms


def process_stream_frame_sync(
    *,
    session: StreamSessionState,
    source_path: Path,
    frame_name: str | None,
) -> tuple[str, str | None, str | None, bool, str | None, list[dict[str, Any]], float, float, str]:
    with session.lock:
        session.received_frame_count += 1
        current_frame_entry, inference_ms = infer_single_frame_entry(
            session=session,
            source_path=source_path,
            frame_name=frame_name,
        )
        received_frame_name = str(current_frame_entry["output_name"])

        if session.pending_frame_entry is None:
            session.pending_frame_entry = current_frame_entry
            return (
                received_frame_name,
                None,
                None,
                False,
                received_frame_name,
                [],
                inference_ms,
                0.0,
                "首帧已缓冲，等待下一帧后执行相邻帧去重并输出结果。",
            )

        pair = [session.pending_frame_entry, current_frame_entry]
        if bool(session.predict_config.get("adjacent_frame_dedup", False)):
            predict_module.apply_adjacent_frame_dedup(
                pair,
                edge_priority_rule=bool(session.predict_config.get("adjacent_frame_edge_priority_rule", False)),
                x_overlap_threshold=float(session.predict_config.get("adjacent_frame_x_overlap_threshold", 0.35)),
                delta_y=float(session.predict_config.get("adjacent_frame_delta_y", 250.0)),
                height_min=float(session.predict_config.get("adjacent_frame_height_min", 35.64)),
                height_max=float(session.predict_config.get("adjacent_frame_height_max", 119.95)),
                height_confidence_offset_px=float(session.predict_config.get("adjacent_frame_height_confidence_offset_px", 1.0)),
                non_bottom_y2_correction=float(session.predict_config.get("adjacent_frame_non_bottom_y2_correction", 15.0)),
                bottom_touch_margin_px=float(session.predict_config.get("adjacent_frame_bottom_touch_margin_px", 3.0)),
                x_tolerance=float(session.predict_config.get("adjacent_frame_x_tolerance", 12.0)),
            )

        finalized_frame_name = str(session.pending_frame_entry["output_name"])
        summary_items, postprocess_ms = finalize_stream_frame(
            session=session,
            frame_entry=session.pending_frame_entry,
        )
        finalized_image_path = str(session.pending_frame_entry["output_path"])
        session.pending_frame_entry = current_frame_entry
        return (
            received_frame_name,
            finalized_frame_name,
            finalized_image_path,
            True,
            received_frame_name,
            summary_items,
            inference_ms,
            postprocess_ms,
            "当前帧已进入缓冲，上一帧已完成相邻帧去重并输出。",
        )


def finish_stream_session_sync(session: StreamSessionState) -> tuple[str | None, str | None, list[dict[str, Any]], float]:
    with session.lock:
        finalized_frame_name: str | None = None
        finalized_image_path: str | None = None
        summary_items: list[dict[str, Any]] = []
        postprocess_ms = 0.0
        if session.pending_frame_entry is not None:
            finalized_frame_name = str(session.pending_frame_entry["output_name"])
            finalized_image_path = str(session.pending_frame_entry["output_path"])
            summary_items, postprocess_ms = finalize_stream_frame(
                session=session,
                frame_entry=session.pending_frame_entry,
            )
            session.pending_frame_entry = None
        write_stream_summary(session)
        return finalized_frame_name, finalized_image_path, summary_items, postprocess_ms


def execute_inference_sync(
    *,
    model_path: Path,
    source_path: Path,
    predict_config: dict[str, Any],
    run_root: Path,
    runtime_config_path: Path,
    stdout_log_path: Path,
    stderr_log_path: Path,
) -> Path:
    if predict_module is None:
        raise RuntimeError("predict 模块未初始化。")

    save_dir = run_root / "predict_run"
    if save_dir.exists():
        shutil.rmtree(save_dir)
    save_dir.mkdir(parents=True, exist_ok=True)

    save_json(runtime_config_path, predict_config)

    cached_model = model_registry.get_or_load(model_path)
    stderr_log_path.write_text("", encoding="utf-8")

    infer_started = time.perf_counter()
    with cached_model.lock:
        results = cached_model.instance.predict(
            source=str(source_path),
            imgsz=int(predict_config["imgsz"]),
            device=str(predict_config["device"]),
            conf=float(predict_config["conf"]),
            iou=float(predict_config["iou"]),
            agnostic_nms=bool(predict_config.get("agnostic_nms", False)),
            project=str(run_root),
            name="predict_run",
            exist_ok=bool(predict_config.get("exist_ok", True)),
            save=False,
            save_txt=False,
            save_conf=False,
        )

    inference_ms = (time.perf_counter() - infer_started) * 1000.0

    postprocess_started = time.perf_counter()
    predict_module.save_adjusted_predictions(
        results=results,
        save_dir=save_dir,
        save_txt=bool(predict_config.get("save_txt", False)),
        save_conf=bool(predict_config.get("save_conf", False)),
        same_class_contain_suppression=bool(predict_config.get("same_class_contain_suppression", False)),
        contain_ratio_threshold=float(predict_config.get("contain_ratio_threshold", 0.5)),
        adjacent_frame_dedup=bool(predict_config.get("adjacent_frame_dedup", False)),
        adjacent_frame_edge_priority_rule=bool(predict_config.get("adjacent_frame_edge_priority_rule", False)),
        adjacent_frame_x_overlap_threshold=float(predict_config.get("adjacent_frame_x_overlap_threshold", 0.35)),
        adjacent_frame_delta_y=float(predict_config.get("adjacent_frame_delta_y", 250.0)),
        adjacent_frame_height_min=float(predict_config.get("adjacent_frame_height_min", 35.64)),
        adjacent_frame_height_max=float(predict_config.get("adjacent_frame_height_max", 119.95)),
        adjacent_frame_height_confidence_offset_px=float(predict_config.get("adjacent_frame_height_confidence_offset_px", 1.0)),
        adjacent_frame_non_bottom_y2_correction=float(predict_config.get("adjacent_frame_non_bottom_y2_correction", 15.0)),
        adjacent_frame_bottom_touch_margin_px=float(predict_config.get("adjacent_frame_bottom_touch_margin_px", 3.0)),
        adjacent_frame_x_tolerance=float(predict_config.get("adjacent_frame_x_tolerance", 12.0)),
        export_penalty_hits=bool(predict_config.get("export_penalty_hits", True)),
        penalty_hits_dirname=str(predict_config.get("penalty_hits_dirname", "penalty_hits")),
        render_adjacent_frame_guides=bool(predict_config.get("render_adjacent_frame_guides", False)),
    )

    postprocess_ms = (time.perf_counter() - postprocess_started) * 1000.0

    stdout_log_path.write_text(
        json.dumps(
            {
                "mode": "in_memory_yolo",
                "model_path": str(model_path),
                "source_path": str(source_path),
                "runtime_config_path": str(runtime_config_path),
                "save_dir": str(save_dir),
                "cached_model_count": len(model_registry.list_paths()),
                "model_inference_milliseconds": round(inference_ms, 2),
                "postprocess_milliseconds": round(postprocess_ms, 2),
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    return save_dir / "phase2_postprocess_summary.json"


@app.post("/stream-sessions/start", response_model=StreamSessionStartResponse)
async def start_stream_session(request: StreamSessionStartRequest) -> StreamSessionStartResponse:
    ensure_service_directories()

    model_path = Path(request.model_path).expanduser().resolve()
    if not model_path.exists():
        raise HTTPException(status_code=400, detail=f"模型文件不存在: {model_path}")

    base_config_path = Path(request.config_path).expanduser().resolve() if request.config_path else TEMPLATE_CONFIG_PATH
    if not base_config_path.exists():
        raise HTTPException(status_code=400, detail=f"predict 配置不存在: {base_config_path}")

    session_id = request.session_name or f"{datetime.now():%Y%m%d-%H%M%S}-{uuid.uuid4().hex[:8]}"
    run_root = (Path(request.output_root).expanduser().resolve() if request.output_root else RUN_ROOT) / session_id
    save_dir = run_root / "predict_stream"
    labels_dir = save_dir / "labels"
    save_dir.mkdir(parents=True, exist_ok=True)

    payload = build_stream_session_config(
        base_config_path=base_config_path,
        request=request,
        model_path=model_path,
        run_root=run_root,
    )
    runtime_config_path = run_root / "predict.runtime.json"
    save_json(runtime_config_path, payload)

    if bool(payload.get("save_txt", False)):
        labels_dir.mkdir(parents=True, exist_ok=True)
    penalty_hits_dir = save_dir / str(payload.get("penalty_hits_dirname", "penalty_hits")) if bool(payload.get("export_penalty_hits", True)) else None
    if penalty_hits_dir is not None:
        penalty_hits_dir.mkdir(parents=True, exist_ok=True)

    session = StreamSessionState(
        session_id=session_id,
        model_path=model_path,
        base_config_path=base_config_path,
        runtime_config_path=runtime_config_path,
        run_root=run_root,
        save_dir=save_dir,
        labels_dir=labels_dir,
        penalty_hits_dir=penalty_hits_dir,
        predict_config=payload,
        frame_interval_milliseconds=request.frame_interval_milliseconds,
    )
    stream_session_registry.add(session)

    preload_started = time.perf_counter()
    try:
        cached_model = await asyncio.to_thread(model_registry.get_or_load, model_path)
        warmup_ms, already_warmed = await asyncio.to_thread(
            warm_up_cached_model,
            cached_model=cached_model,
            predict_config=payload,
            run_root=run_root,
        )
    except Exception:
        stream_session_registry.remove(session_id)
        raise
    preload_ms = (time.perf_counter() - preload_started) * 1000.0

    return StreamSessionStartResponse(
        session_id=session_id,
        runtime_config_path=str(runtime_config_path),
        output_directory=str(save_dir),
        frame_interval_milliseconds=request.frame_interval_milliseconds,
        model_preload_milliseconds=round(preload_ms, 2),
        message=(
            f"流式会话已创建，模型已预加载"
            f"{'（复用已预热模型）' if already_warmed else f'并预热（预热 {warmup_ms:.2f} ms）'}。"
            "首帧将先进入缓冲区，待下一帧到达后再完成相邻帧去重。"
        ),
    )


@app.post("/models/preload", response_model=ModelPreloadResponse)
async def preload_model(request: ModelPreloadRequest) -> ModelPreloadResponse:
    ensure_service_directories()
    model_path = Path(request.model_path).expanduser().resolve()
    if not model_path.exists():
        raise HTTPException(status_code=400, detail=f"模型文件不存在: {model_path}")

    base_config_path = Path(request.config_path).expanduser().resolve() if request.config_path else TEMPLATE_CONFIG_PATH
    if not base_config_path.exists():
        raise HTTPException(status_code=400, detail=f"predict 配置不存在: {base_config_path}")

    payload = load_predict_defaults(base_config_path)
    payload["weights"] = str(model_path)
    payload["imgsz"] = resolve_image_size(request.image_size, payload)
    apply_product_postprocess_overrides(payload, request.config_overrides)

    preload_started = time.perf_counter()
    try:
        cached_model = await asyncio.to_thread(model_registry.get_or_load, model_path)
        warmup_ms, already_warmed = await asyncio.to_thread(
            warm_up_cached_model,
            cached_model=cached_model,
            predict_config=payload,
            run_root=SERVICE_RUNTIME_ROOT / "preload",
        )
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"模型预加载失败: {exc}") from exc

    return ModelPreloadResponse(
        model_path=str(model_path),
        preload_milliseconds=round((time.perf_counter() - preload_started) * 1000.0, 2),
        warmup_milliseconds=round(warmup_ms, 2),
        already_warmed=already_warmed,
    )


@app.post("/stream-sessions/{session_id}/frames", response_model=StreamFrameInferResponse)
async def infer_stream_frame(session_id: str, request: StreamFrameInferRequest) -> StreamFrameInferResponse:
    ensure_service_directories()

    session = stream_session_registry.get(session_id)
    if session is None:
        raise HTTPException(status_code=404, detail=f"未找到流式会话: {session_id}")

    source_path = Path(request.source_path).expanduser().resolve()
    if not source_path.exists():
        raise HTTPException(status_code=400, detail=f"输入源不存在: {source_path}")

    try:
        request_started = time.perf_counter()
        received_frame_name, finalized_frame_name, finalized_image_path, has_finalized_output, buffered_frame_name, summary_items, inference_ms, postprocess_ms, message = await asyncio.wait_for(
            asyncio.to_thread(
                process_stream_frame_sync,
                session=session,
                source_path=source_path,
                frame_name=request.frame_name,
            ),
            timeout=request.timeout_seconds,
        )
    except asyncio.TimeoutError as exc:
        raise HTTPException(status_code=504, detail=f"流式推理超时，超过 {request.timeout_seconds} 秒。") from exc
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"流式推理失败: {exc}") from exc

    request_total_ms = (time.perf_counter() - request_started) * 1000.0
    service_overhead_ms = max(0.0, request_total_ms - inference_ms - postprocess_ms)

    return StreamFrameInferResponse(
        session_id=session_id,
        received_frame_name=received_frame_name,
        finalized_frame_name=finalized_frame_name,
        finalized_image_path=finalized_image_path,
        has_finalized_output=has_finalized_output,
        buffered_frame_name=buffered_frame_name,
        summary_path=str(session.summary_path),
        model_inference_milliseconds=round(inference_ms, 2),
        postprocess_milliseconds=round(postprocess_ms, 2),
        service_overhead_milliseconds=round(service_overhead_ms, 2),
        request_total_milliseconds=round(request_total_ms, 2),
        detections=build_detection_items(summary_items),
        message=message,
    )


@app.post("/stream-sessions/{session_id}/finish", response_model=StreamSessionFinishResponse)
async def finish_stream_session(session_id: str) -> StreamSessionFinishResponse:
    session = stream_session_registry.get(session_id)
    if session is None:
        raise HTTPException(status_code=404, detail=f"未找到流式会话: {session_id}")

    try:
        request_started = time.perf_counter()
        finalized_frame_name, finalized_image_path, summary_items, postprocess_ms = await asyncio.to_thread(
            finish_stream_session_sync,
            session,
        )
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"结束流式会话失败: {exc}") from exc
    finally:
        stream_session_registry.remove(session_id)

    request_total_ms = (time.perf_counter() - request_started) * 1000.0
    service_overhead_ms = max(0.0, request_total_ms - postprocess_ms)

    return StreamSessionFinishResponse(
        session_id=session_id,
        finalized_frame_name=finalized_frame_name,
        finalized_image_path=finalized_image_path,
        summary_path=str(session.summary_path),
        postprocess_milliseconds=round(postprocess_ms, 2),
        service_overhead_milliseconds=round(service_overhead_ms, 2),
        request_total_milliseconds=round(request_total_ms, 2),
        detections=build_detection_items(summary_items),
        message="流式会话已结束，最后一帧已完成输出。",
    )


@app.post("/infer", response_model=InferResponse)
async def infer(request: InferRequest) -> InferResponse:
    ensure_service_directories()
    request_started = time.perf_counter()

    model_path = Path(request.model_path).expanduser().resolve()
    source_path = Path(request.source_path).expanduser().resolve()
    if not model_path.exists():
        raise HTTPException(status_code=400, detail=f"模型文件不存在: {model_path}")
    if not source_path.exists():
        raise HTTPException(status_code=400, detail=f"输入源不存在: {source_path}")

    base_config_path = Path(request.config_path).expanduser().resolve() if request.config_path else TEMPLATE_CONFIG_PATH
    if not base_config_path.exists():
        raise HTTPException(status_code=400, detail=f"predict 配置不存在: {base_config_path}")

    run_id = request.run_name or f"{datetime.now():%Y%m%d-%H%M%S}-{uuid.uuid4().hex[:8]}"
    run_root = (Path(request.output_root).expanduser().resolve() if request.output_root else RUN_ROOT) / run_id
    run_root.mkdir(parents=True, exist_ok=True)

    runtime_config_path = run_root / "predict.runtime.json"
    stdout_log_path = LOG_ROOT / f"{run_id}.stdout.log"
    stderr_log_path = LOG_ROOT / f"{run_id}.stderr.log"

    payload = build_effective_config(
        base_config_path=base_config_path,
        request=request,
        model_path=model_path,
        source_path=source_path,
        run_root=run_root,
    )

    try:
        summary_path = await asyncio.wait_for(
            asyncio.to_thread(
                execute_inference_sync,
                model_path=model_path,
                source_path=source_path,
                predict_config=payload,
                run_root=run_root,
                runtime_config_path=runtime_config_path,
                stdout_log_path=stdout_log_path,
                stderr_log_path=stderr_log_path,
            ),
            timeout=request.timeout_seconds,
        )
    except asyncio.TimeoutError as exc:
        raise HTTPException(status_code=504, detail=f"推理超时，超过 {request.timeout_seconds} 秒。") from exc
    except Exception as exc:
        stderr_log_path.write_text(str(exc), encoding="utf-8")
        raise HTTPException(
            status_code=500,
            detail={
                "message": f"内存常驻 YOLO 推理失败: {exc}",
                "stderr_log_path": str(stderr_log_path),
                "stdout_log_path": str(stdout_log_path),
            },
        ) from exc

    if not summary_path.exists():
        raise HTTPException(
            status_code=500,
            detail={
                "message": "未找到 phase2_postprocess_summary.json，当前运行可能没有走 phase2 输出链路。",
                "summary_path": str(summary_path),
                "stdout_log_path": str(stdout_log_path),
                "stderr_log_path": str(stderr_log_path),
            },
        )

    summary_items = json.loads(summary_path.read_text(encoding="utf-8"))
    detections = build_detection_items(summary_items)
    stdout_payload = {}
    try:
        stdout_payload = json.loads(stdout_log_path.read_text(encoding="utf-8"))
    except Exception:
        stdout_payload = {}
    rendered_image_path = None
    if detections:
        candidate = run_root / "predict_run" / detections[0].image_name
        if candidate.exists():
            rendered_image_path = str(candidate)
    request_total_ms = (time.perf_counter() - request_started) * 1000.0
    service_overhead_ms = max(
        0.0,
        request_total_ms
        - float(stdout_payload.get("model_inference_milliseconds", 0.0))
        - float(stdout_payload.get("postprocess_milliseconds", 0.0)),
    )

    return InferResponse(
        run_id=run_id,
        runtime_config_path=str(runtime_config_path),
        output_directory=str(run_root / "predict_run"),
        summary_path=str(summary_path),
        stdout_log_path=str(stdout_log_path),
        stderr_log_path=str(stderr_log_path),
        rendered_image_path=rendered_image_path,
        model_inference_milliseconds=round(float(stdout_payload.get("model_inference_milliseconds", 0.0)), 2),
        postprocess_milliseconds=round(float(stdout_payload.get("postprocess_milliseconds", 0.0)), 2),
        service_overhead_milliseconds=round(service_overhead_ms, 2),
        request_total_milliseconds=round(request_total_ms, 2),
        detections=detections,
    )
