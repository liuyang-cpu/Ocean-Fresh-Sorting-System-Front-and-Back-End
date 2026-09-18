# Ocean Fresh YOLO inference

This is the product-neutral entry point for YOLO inference.

- `predict.json` contains shared inference defaults.
- `predict.py` delegates to the legacy implementation during migration.
- Product configuration may override only `adjacent_frame_height_min` and
  `adjacent_frame_height_max`.
- `imgsz` is supplied by model-version metadata at runtime.

The old `predict/youge` paths remain available for compatibility and should not
be used by new integrations.
