from __future__ import annotations

import base64
import importlib
import io
import os
import sys
from pathlib import Path
from typing import Any

import torch
import uvicorn
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from PIL import Image, ImageDraw


app = FastAPI(title="Local SAM 3.1 worker", version="0.1.0")
_model: Any | None = None
_processor: Any | None = None
_load_error: str | None = None


class DetectRequest(BaseModel):
    prompt: str = Field(min_length=1, max_length=500)
    image_base64: str = Field(min_length=1)


def _load_model() -> tuple[Any, Any]:
    global _model, _processor, _load_error
    if _model is not None and _processor is not None:
        return _model, _processor
    try:
        sam_repo = os.environ.get("SAM3_REPO")
        if sam_repo:
            repo_path = Path(sam_repo).expanduser().resolve()
            if not repo_path.is_dir():
                raise RuntimeError(f"SAM3_REPO does not exist: {repo_path}")
            sys.path.insert(0, str(repo_path))
            importlib.invalidate_caches()

        from sam3.model_builder import build_sam3_image_model
        from sam3.model.sam3_image_processor import Sam3Processor

        _model = build_sam3_image_model()
        _processor = Sam3Processor(_model)
        _load_error = None
        return _model, _processor
    except Exception as exc:
        _load_error = str(exc)
        raise


@app.get("/health")
def health() -> dict[str, Any]:
    return {
        "service": "sam3.1-local",
        "status": "ready" if _processor is not None else "not-loaded",
        "model_loaded": _processor is not None,
        "device": "cuda" if torch.cuda.is_available() else "cpu",
        "cuda_available": torch.cuda.is_available(),
        "gpu_name": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
        "load_error": _load_error,
    }


@app.post("/detect")
def detect(request: DetectRequest) -> dict[str, Any]:
    try:
        _, processor = _load_model()
        image_bytes = base64.b64decode(request.image_base64, validate=True)
        with Image.open(io.BytesIO(image_bytes)) as source:
            image = source.convert("RGB")
        state = processor.set_image(image)
        output = processor.set_text_prompt(state=state, prompt=request.prompt)
        boxes = output["boxes"].detach().cpu().tolist()
        scores = output["scores"].detach().cpu().tolist()
        masks = output["masks"].detach().cpu().numpy()
        if masks.ndim == 4:
            masks = masks[:, 0]

        preview = image.convert("RGBA")
        tint = Image.new("RGBA", image.size, (0, 0, 0, 0))
        palette = [(38, 210, 170), (255, 184, 77), (116, 160, 255), (255, 110, 134)]
        for index, mask in enumerate(masks):
            alpha = Image.fromarray((mask.astype("uint8") * 78), mode="L")
            color = palette[index % len(palette)]
            layer = Image.new("RGBA", image.size, (*color, 0))
            layer.putalpha(alpha)
            tint = Image.alpha_composite(tint, layer)
        preview = Image.alpha_composite(preview, tint)
        draw = ImageDraw.Draw(preview)
        for index, (box, score) in enumerate(zip(boxes, scores, strict=True)):
            x0, y0, x1, y1 = (int(round(value)) for value in box)
            color = palette[index % len(palette)]
            draw.rectangle((x0, y0, x1, y1), outline=(*color, 255), width=3)
            draw.text((x0, max(0, y0 - 18)), f"{request.prompt} {score:.2f}", fill=(*color, 255))
        preview_buffer = io.BytesIO()
        preview.convert("RGB").save(preview_buffer, format="PNG", optimize=True)
        return {
            "prompt": request.prompt,
            "image_width": image.width,
            "image_height": image.height,
            "boxes": boxes,
            "scores": scores,
            "coordinate_space": "image-pixels-xyxy",
            "preview_base64": base64.b64encode(preview_buffer.getvalue()).decode("ascii"),
            "note": "2D detections only; no Revit world coordinates or placement rotation are inferred.",
        }
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"SAM 3.1 inference failed: {exc}") from exc


if __name__ == "__main__":
    host = os.environ.get("SAM3_HOST", "127.0.0.1")
    port = int(os.environ.get("SAM3_PORT", "8767"))
    if host not in {"127.0.0.1", "localhost"}:
        raise RuntimeError("The local worker must bind to loopback only.")
    uvicorn.run(app, host=host, port=port, log_level="info")
