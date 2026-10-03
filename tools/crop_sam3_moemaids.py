# -*- coding: utf-8 -*-
"""
tools/crop_sam3_moemaids.py
使用 SAM3 对 3 张全新萌相女仆立绘进行精准面部检测与 1:1 头像截取。
"""
import os
import sys
import pathlib
import types
import time
from PIL import Image
import numpy as np

PROJ_DIR = pathlib.Path(r"D:/123/rimisekai")
NODE_DIR = pathlib.Path(r"D:/openclaw/ComfyUI-aki-v2/custom_nodes/tbg-sam3")
CKPT = pathlib.Path(r"D:/openclaw/ComfyUI-aki-v2/models/sam3/sam3.pt")
BPE = NODE_DIR / "sam3" / "sam3" / "assets" / "bpe_simple_vocab_16e6.txt.gz"
TARGET_SIZE = 512


def _install_triton_stub():
    try:
        import triton
        return
    except ImportError:
        pass

    class _Any:
        def __init__(self, *a, **k): pass
        def __call__(self, *a, **k): return a[0] if len(a) == 1 and callable(a[0]) else _Any()
        def __getattr__(self, name): return _Any()
        def __getitem__(self, k): return _Any()

    def _decorator(*a, **k):
        return a[0] if len(a) == 1 and callable(a[0]) and not k else (lambda f: f)

    def _module_getattr(name):
        if name.startswith("__"):
            raise AttributeError(name)
        return _Any()

    lang = types.ModuleType("triton.language")
    lang.__file__ = "<triton-language-stub>"
    lang.__path__ = []
    lang.__spec__ = None
    lang.constexpr = type("constexpr", (), {})
    lang.__getattr__ = _module_getattr

    triton = types.ModuleType("triton")
    triton.__file__ = "<triton-stub>"
    triton.__path__ = []
    triton.__spec__ = None
    triton.jit = triton.autotune = triton.heuristics = _decorator
    triton.Config = _Any
    triton.cdiv = lambda a, b: -(-a // b)
    triton.language = lang
    triton.__getattr__ = _module_getattr

    sys.modules["triton"] = triton
    sys.modules["triton.language"] = lang


def _install_pkg_resources_shim():
    try:
        import pkg_resources
        return
    except ImportError:
        pass
    import importlib.util

    mod = types.ModuleType("pkg_resources")
    def resource_filename(package: str, resource: str) -> str:
        spec = importlib.util.find_spec(package)
        if spec is None or not spec.submodule_search_locations:
            raise ImportError(f"cannot locate package {package!r}")
        return str(pathlib.Path(list(spec.submodule_search_locations)[0]) / resource)

    mod.resource_filename = resource_filename
    mod.get_distribution = lambda *a, **k: None
    mod.working_set = []
    sys.modules["pkg_resources"] = mod


def _patch_sam3_offline(bpe_path: pathlib.Path):
    import sam3.model_builder as mb
    orig = mb.build_sam3_image_model

    def patched(*a, **kw):
        kw["load_from_HF"] = False
        kw["checkpoint_path"] = None
        kw.setdefault("bpe_path", str(bpe_path))
        return orig(*a, **kw)

    def _no_download(*a, **kw):
        raise RuntimeError("offline mode: refusing to download from HuggingFace")

    mb.build_sam3_image_model = patched
    mb.download_ckpt_from_hf = _no_download


def _patch_sam3_fused_dtype():
    import torch
    from sam3.perflib import fused
    if getattr(fused.addmm_act, "_dtype_patched", False):
        return

    def addmm_act(activation, linear, mat1):
        if torch.is_grad_enabled():
            raise ValueError("Expected grad to be disabled.")
        dt = mat1.dtype
        bias = linear.bias.detach().to(dt)
        weight = linear.weight.detach().to(dt)
        mat1 = mat1.to(dt)
        mat1_flat = mat1.view(-1, mat1.shape[-1])
        is_relu = activation in (torch.nn.functional.relu, torch.nn.ReLU)
        is_gelu = activation in (torch.nn.functional.gelu, torch.nn.GELU)
        if not (is_relu or is_gelu):
            raise ValueError(f"Unexpected activation {activation}")
        y = fused.addmm_act_op(bias, mat1_flat, weight.t(), beta=1, alpha=1, use_gelu=is_gelu)
        return y.view(mat1.shape[:-1] + (y.shape[-1],))

    addmm_act._dtype_patched = True
    fused.addmm_act = addmm_act
    import sam3.model.vitdet as vitdet
    vitdet.addmm_act = addmm_act


def _patch_sam3_loader():
    import torch
    from sam3_utils import SAM3ImageSegmenter
    from sam3.model_builder import build_sam3_image_model
    from sam3.model.sam3_image_processor import Sam3Processor

    if getattr(SAM3ImageSegmenter._load_model, "_loader_patched", False):
        return

    def _load_model(self, model_path=None):
        print(f"[SAM3] Loading SAM3 image model on {self.device}...", flush=True)
        self.model = build_sam3_image_model(device="cpu", load_from_HF=False, checkpoint_path=None)
        sd = torch.load(model_path, map_location="cpu", weights_only=False)
        sd = {(k[len("detector."):] if k.startswith("detector.") else k): v for k, v in sd.items()}
        info = self.model.load_state_dict(sd, strict=False)
        print(f"[SAM3] Loaded weights: matched={len(sd) - len(info.unexpected_keys)}, missing={len(info.missing_keys)}", flush=True)
        self.model = self.model.to(self.device).eval()
        self.processor = Sam3Processor(self.model)

    _load_model._loader_patched = True
    SAM3ImageSegmenter._load_model = _load_model


def main():
    import torch
    if torch.cuda.is_available():
        torch.cuda.init()
    _install_pkg_resources_shim()
    _install_triton_stub()

    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"

    sys.path.insert(0, str(NODE_DIR / "sam3"))
    sys.path.insert(0, str(NODE_DIR))

    _patch_sam3_offline(BPE)
    _patch_sam3_fused_dtype()
    _patch_sam3_loader()

    from sam3_utils import SAM3ImageSegmenter

    dev = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"=== [SAM3] 正在加载模型 (device={dev}) ===", flush=True)
    seg = SAM3ImageSegmenter(device=dev, model_path=str(CKPT))

    targets = [
        ("立绘_女仆_萌相_差分1.png", "头像_女仆_萌相_差分1.png"),
        ("立绘_女仆_萌相_差分2.png", "头像_女仆_萌相_差分2.png"),
        ("立绘_女仆_萌相_差分3.png", "头像_女仆_萌相_差分3.png"),
    ]

    for src_name, dst_name in targets:
        src_path = PROJ_DIR / src_name
        dst_path = PROJ_DIR / dst_name
        if not src_path.exists():
            print(f"File not found: {src_name}")
            continue

        im = Image.open(src_path).convert("RGB")
        w, h = im.size

        crop_h = int(h * 0.42)
        top_crop = im.crop((0, 0, w, crop_h))
        scale = 0.5
        sim = top_crop.resize((int(w * scale), int(crop_h * scale)), Image.Resampling.BILINEAR)

        with torch.inference_mode(), torch.autocast("cuda", dtype=torch.bfloat16):
            masks, boxes, scores = seg.segment_image(sim, "face")

        if len(boxes) > 0:
            best_i = int(scores.argmax())
            sc = float(scores[best_i])
            b = boxes[best_i].tolist()
            fx1, fy1, fx2, fy2 = b[0] / scale, b[1] / scale, b[2] / scale, b[3] / scale
            fcx = (fx1 + fx2) / 2
            fcy = (fy1 + fy2) / 2
            fh = fy2 - fy1

            bs = int(max(270, min(310, fh * 2.1)))
            x1 = int(fcx - bs * 0.50)
            x2 = x1 + bs
            y1 = int(fcy - bs * 0.46)
            y2 = y1 + bs

            if x1 < 0: x2 += -x1; x1 = 0
            if x2 > w: x1 -= (x2 - w); x2 = w
            if y1 < 0: y2 += -y1; y1 = 0
            if y2 > h: y1 -= (y2 - h); y2 = h

            cropped = im.crop((x1, y1, x2, y2))
            resized = cropped.resize((TARGET_SIZE, TARGET_SIZE), Image.Resampling.LANCZOS)
            resized.save(dst_path)
            print(f"SAM3 成功截取: {dst_name} -> 脸=[{fx1:.0f},{fy1:.0f},{fx2:.0f},{fy2:.0f}] 选框=[{x1},{y1},{x2},{y2}] (边长={bs}, 得分={sc:.3f})", flush=True)


if __name__ == "__main__":
    main()
