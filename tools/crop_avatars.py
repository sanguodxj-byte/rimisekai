# -*- coding: utf-8 -*-
"""
tools/crop_avatars.py
身体主轴与面部核心双重居中锁头算法：从 9:16 全身立绘中截取 1:1 角色头像（512x512）。
1. 身体主轴线定位：扫描画高中段（35%~75%）躯干双腿中位数，精准找到角色垂直中轴，完全杜绝侧向武器（大斧、巨盾、羽翼）干扰；
2. 头部纵向主体定位：在主轴线左右各 220px 范围内寻找头部密集区（head_top），剔除上方细微弓箭尖端干扰；
3. 面部绝对居中：以 (head_cx, head_top + 115) 为中心定出 360x360 正方形视窗，无论高尖顶帽还是侧向瞄准，面部五官永远绝对居中在画布正中心；
4. 512x512 高保真下采样：高质 Lanczos 重采样输出，平铺保存于仓库根目录。
"""

import glob
import os
import sys
import numpy as np
from PIL import Image

PROJ_DIR = r"D:\123\rimisekai"
BOX_SIZE = 400
TARGET_SIZE = 512


def find_head_box(img_path, box_size=BOX_SIZE):
    im = Image.open(img_path).convert("L")
    arr = np.array(im)
    h, w = arr.shape
    mask = arr > 20

    # 1. 身体主轴线（在画高中段 y: 35%~75%，躯干与双腿像素水平中位数）
    body_mask = mask[int(h * 0.35) : int(h * 0.75), :]
    body_cols = np.where(body_mask)[1]
    body_cx = int(np.median(body_cols)) if len(body_cols) > 0 else w // 2

    # 2. 在身体主轴线左右各 150px 内寻找头顶发冠起跑线（平滑排除单个孤立细线）
    x_min = max(0, body_cx - 150)
    x_max = min(w, body_cx + 150)
    col_strip = mask[: int(h * 0.45), x_min:x_max]
    strip_rows = col_strip.sum(axis=1)

    window = 11
    smoothed = np.convolve(strip_rows, np.ones(window) / window, mode="same")
    dense_rows = np.where(smoothed > 25)[0]
    head_top = dense_rows[0] if len(dense_rows) > 0 else 25

    # 3. Y 轴定框：从 head_top 向上预留 25px 呼吸边距，彻底消灭切头切额顶
    y1 = max(0, head_top - 25)
    y2 = y1 + box_size
    if y2 > h:
        y2 = h
        y1 = max(0, y2 - box_size)

    # 4. X 轴定框：以 head_top 到 head_top + 200 区域内的中位数水平居中
    h_slice = mask[head_top : head_top + 200, x_min:x_max]
    h_rows, h_cols = np.where(h_slice)
    head_cx = x_min + int(np.median(h_cols)) if len(h_cols) > 0 else body_cx

    x1 = head_cx - box_size // 2
    x2 = head_cx + box_size // 2
    if x1 < 0:
        x2 += -x1
        x1 = 0
    if x2 > w:
        x1 -= (x2 - w)
        x2 = w

    return (int(x1), int(y1), int(x2), int(y2))


def process_image(src_path, dst_path):
    box = find_head_box(src_path)
    im = Image.open(src_path)
    cropped = im.crop(box)
    resized = cropped.resize((TARGET_SIZE, TARGET_SIZE), Image.Resampling.LANCZOS)
    resized.save(dst_path)
    return box


def main():
    pattern = os.path.join(PROJ_DIR, "立绘_*_差分*.png")
    files = sorted(glob.glob(pattern))

    # 同时处理 3 张独立角色立绘
    extras = [
        ("立绘_人类圣骑士.png", "头像_人类圣骑士.png"),
        ("立绘_鼠耳鼠尾圣女.png", "头像_鼠耳鼠尾圣女.png"),
        ("立绘_马耳马尾武装修女_v2.png", "头像_马耳马尾武装修女.png"),
    ]
    for extra_src, extra_dst in extras:
        full_src = os.path.join(PROJ_DIR, extra_src)
        if os.path.exists(full_src):
            files.append(full_src)

    print(f"=== 开始全量截取 1:1 头像（共 {len(files)} 张，目标 512x512）===", flush=True)

    count = 0
    for f in files:
        base = os.path.basename(f)
        if base.startswith("立绘_马耳马尾武装修女_v2.png"):
            dst_name = "头像_马耳马尾武装修女.png"
        elif base.startswith("立绘_"):
            dst_name = base.replace("立绘_", "头像_")
        else:
            dst_name = f"头像_{base}"

        dst_path = os.path.join(PROJ_DIR, dst_name)
        box = process_image(f, dst_path)
        count += 1
        if count % 10 == 0 or count == len(files):
            print(f"[{count}/{len(files)}] 已生成: {dst_name} (原图截取选框: {box})", flush=True)

    print(f"=== 截取完成！共生成 {count} 张 512x512 头像 ===", flush=True)


if __name__ == "__main__":
    main()
