#!/usr/bin/env python3
"""竖版对齐不变量检测：与 check_portrait_isolation.py 同源（读 PortraitCapture 的 widgets dump）。

检测三条真实不变量（2026-10-07 全量审计后沉淀）：
  1. 页签带 Tab 四钮必须等分整条（x = 0/270/540/810，w = 270）。标题画面豁免——
     它的 Tab 是居中菜单钮列（x=210 w=660），不是页签带。
  2. 单列组（同页同 action 左缘唯一）的行距必须全程等差，且落在已知档位。
  3. 左缘一致性（纯列表组：y 无重复）：全组左缘必须一致，且落在已知留白档
     （40＝画布 Pad，80＝面板内再缩一层）。网格组（同 y 多成员）跳过——列分布是布局本身；
     行内按钮对与弹窗内控件豁免（左缘由容器决定：StoreIn/StoreOut 贴行右缘，
     ModalChoice 在 800 宽弹窗内缩 40）。

用法:
  PORTRAIT_DUMP=_ui_mobile/portrait_widgets.jsonl python tools/check_portrait_alignment.py
"""
import json
import os
from pathlib import Path
import sys
from collections import defaultdict

ROOT = Path(__file__).resolve().parents[1]
DUMP = Path(os.environ.get("PORTRAIT_DUMP", ROOT / "_ui_mobile/portrait_widgets.jsonl"))
TAB_X = [0, 270, 540, 810]
TAB_W = 270

# 行距允许值：通用列表 118（RowHeight），设施交互页存储行 148（StorageItemHeight=TouchComfort）。
KNOWN_STEPS = {118, 148, 132, 204, 172, 126}
# 单列组左缘允许档：40＝画布留白线，80＝面板内再缩一层留白（如状态页能力展开钮）。
KNOWN_LEFTS = {40, 80}
# 行内按钮对（贴行右缘定位）与弹窗内控件（800 宽弹窗内缩 40 → x=180）：
# 左缘由容器决定而非画布留白线，跳过左缘检测，行距检测保留。
CONTAINER_POSITIONED = {"StoreIn", "StoreOut", "ModalChoice"}


def main():
    if not DUMP.exists():
        print(f"Run PortraitCapture.tscn with --pdump={DUMP} first.")
        return 1
    widgets = [json.loads(l) for l in open(DUMP, encoding="utf-8")]
    errors = []

    groups = defaultdict(list)
    for r in widgets:
        if r["kind"] == "widget":
            groups[(r["page"], r["action"])].append(r)

    for (page, action), g in sorted(groups.items()):
        if action == "Tab":
            if page == "title":
                continue  # 标题菜单钮列（居中 660 宽），不是页签带
            xs = sorted(round(w["x"], 1) for w in g)
            ws = sorted(round(w["w"], 1) for w in g)
            if xs != TAB_X or ws != [TAB_W] * 4:
                errors.append(f"{page} Tab 等分异常: x={xs} w={ws}")
            continue
        if len(g) < 2:
            continue
        if action == "ScrollTrack":
            continue  # 滑条轨道贴右缘是设计，左缘无留白线约束
        g2 = sorted(g, key=lambda w: w["y"])
        lefts = [round(w["x"], 1) for w in g2]

        # 左缘一致性：仅对「y 无重复（非网格）且多数左缘占比 ≥ 2/3（非多列横排）」的组生效。
        # 纯列表组 100% 一致必查；注入破坏 2/3 仍触发；四列头像卡（各 25%）与网格（60%）天然不触发。
        if action not in CONTAINER_POSITIONED and len({round(w["y"], 1) for w in g2}) == len(g2):
            majority = max(set(lefts), key=lefts.count)
            if lefts.count(majority) / len(lefts) >= 2 / 3:
                stray = [w for w in g2 if abs(w["x"] - majority) > 0.5]
                if stray:
                    errors.append(f"{page} {action} 左缘不一致: 多数 {majority}, 偏离 "
                                  + ", ".join(f"#{w['index']}@{round(w['x'], 1)}" for w in stray))
                elif majority not in KNOWN_LEFTS:
                    errors.append(f"{page} {action} 多数左缘 {majority} 不在已知留白档 {sorted(KNOWN_LEFTS)} 上")

        # 行距等差（仅单列组：左缘唯一才算列表行）。
        if len(set(lefts)) == 1:
            steps = [round(g2[i + 1]["y"] - g2[i]["y"], 1) for i in range(len(g2) - 1)]
            if len(set(steps)) > 1:
                errors.append(f"{page} {action} 行距不均: {steps}")
            elif steps and steps[0] not in KNOWN_STEPS:
                errors.append(f"{page} {action} 行距 {steps[0]} 不在已知档 {sorted(KNOWN_STEPS)} 中")

    pages = sorted({r["page"] for r in widgets})
    if errors:
        print("FAIL: 对齐不变量被破坏")
        for e in errors:
            print("  " + e)
        return 1
    print(f"PASS: {len(pages)} 页对齐不变量全部成立（页签等分、单列行距等差、左缘在留白线）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
