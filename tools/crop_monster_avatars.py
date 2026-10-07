# -*- coding: utf-8 -*-
"""
tools/crop_monster_avatars.py
怪物头像批量截取：从 assets/portraits/monster/ 的 1:1 立绘截取上半部头像方图，
输出 512x512 到 assets/avatars/monster/（保留黑底，不做去背）。
定位：亮部像素（>25）的质心定水平轴；亮区顶界 + 4% 呼吸边距定上缘；
边长取画宽 66%，越界时贴边回退。Lanczos 下采样。
"""

import os
import sys
import numpy as np
from PIL import Image

PROJ = r"D:\123\rimisekai"
SRC = os.path.join(PROJ, "assets", "portraits", "monster")
DST = os.path.join(PROJ, "assets", "avatars", "monster")
SIDE_RATIO = 0.66
TOP_MARGIN = 0.04
TARGET = 512

# 逐文件人工定框：(水平中心, 上缘, 边长)，均为垫黑方画布的比例。未列出的走启发式。
OVERRIDES = {
    "monster_boss_hobgoblin": (0.40, 0.00, 0.45),
    "monster_boss_necromancer_lord": (0.45, 0.00, 0.45),
    "monster_boss_old_god": (0.50, 0.00, 0.75),
    "monster_cave_bat": (0.50, 0.38, 0.34),
    "monster_corrupted_spider": (0.47, 0.25, 0.34),
    "monster_dire_wolf": (0.48, 0.30, 0.34),
    "monster_fire_elemental": (0.50, 0.12, 0.34),
    "monster_floating_eye": (0.50, 0.22, 0.42),
    "monster_gargoyle": (0.50, 0.25, 0.34),
    "monster_giant_rat": (0.50, 0.25, 0.34),
    "monster_goblin": (0.51, 0.28, 0.30),
    "monster_ice_elemental": (0.50, 0.20, 0.34),
    "monster_man_eating_plant": (0.46, 0.12, 0.40),
    "monster_mimic_chest": (0.49, 0.26, 0.42),
    "monster_mushroom_crawler": (0.52, 0.24, 0.36),
    "monster_shadow_tendril": (0.50, 0.18, 0.32),
    "monster_skeleton_archer": (0.42, 0.20, 0.28),
    "monster_skeleton_swordsman": (0.47, 0.08, 0.26),
    "monster_slime": (0.52, 0.47, 0.30),
    "monster_wraith": (0.52, 0.06, 0.34),
    "monster_wyvern_hatchling": (0.42, 0.22, 0.30),
}


def crop_box(arr):
    h, w = arr.shape
    mask = arr > 25
    if not mask.any():
        cx, top = w // 2, 0
    else:
        ys, xs = np.where(mask)
        cx = int(np.median(xs))
        top = int(ys.min())
    side = int(w * SIDE_RATIO)
    x1 = cx - side // 2
    x1 = max(0, min(x1, w - side))
    y1 = max(0, int(top - w * TOP_MARGIN))
    y1 = min(y1, h - side)
    return x1, y1, side


def load_square(src_path):
    """居中垫黑成方画布：后续覆盖表坐标一律在方画布上表达。"""
    img = Image.open(src_path).convert("RGB")
    w, h = img.size
    side = max(w, h)
    canvas = Image.new("RGB", (side, side), (0, 0, 0))
    canvas.paste(img, ((side - w) // 2, (side - h) // 2))
    return canvas


def main():
    os.makedirs(DST, exist_ok=True)
    names = sorted(f for f in os.listdir(SRC) if f.endswith(".png"))
    for name in names:
        src_path = os.path.join(SRC, name)
        canvas = load_square(src_path)
        arr = np.array(canvas.convert("L"))
        w, h = canvas.size
        key = name[:-4]
        if key in OVERRIDES:
            cx_frac, top_frac, side = OVERRIDES[key]
            side = int(w * side)
            x1 = int(w * cx_frac) - side // 2
            y1 = int(h * top_frac)
        else:
            x1, y1, side = crop_box(arr)
        x1 = max(0, min(x1, w - side))
        y1 = max(0, min(y1, h - side))
        out = canvas.crop((x1, y1, x1 + side, y1 + side)).resize((TARGET, TARGET), Image.LANCZOS)
        out_path = os.path.join(DST, name)
        out.save(out_path)
        print(f"{name}: box=({x1},{y1},{side})")
    print(f"done: {len(names)} avatars -> {DST}")


if __name__ == "__main__":
    sys.exit(main())
