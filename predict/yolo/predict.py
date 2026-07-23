"""Product-neutral entry point for the Ocean Fresh YOLO predictor.

The implementation remains at the legacy path during the compatibility window.
New callers should use this module and ``predict/yolo/predict.json``.
"""

from __future__ import annotations

import runpy
from pathlib import Path


LEGACY_SCRIPT = Path(__file__).resolve().parents[1] / "youge" / "predict_youge.py"


if __name__ == "__main__":
    runpy.run_path(str(LEGACY_SCRIPT), run_name="__main__")
